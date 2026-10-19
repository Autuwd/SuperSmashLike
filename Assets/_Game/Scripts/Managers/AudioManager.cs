using System.Collections;
using UnityEngine;

// ============================================================
// AudioManager — 全局音频管理器（单例，跨场景常驻）
// 职责：
//   1. SFX / UI / BGM 三条独立声部（AudioSource 全部代码创建，无需预制体）
//   2. 音量总线：Master × 分组（SFX/BGM/UI），支持 Muted 一键静音
//   3. BGM 淡入淡出 + 随 GameManager 状态自动切曲
// 架构位置：Managers 层（命名空间按契约放 SuperSmashLike.Core，与 GameManager 同域）
// 【为什么不用 AudioMixer 也能工作】：mixer 为空时直接用 AudioSource.volume 做增益；
//   mixer 非空时把有效音量换算成 dB 写进 mixer 参数，AudioSource 只保留每次播放的
//   volumeScale。两条路径由 ApplyVolumes() 统一刷新。
// 【为什么不引用 SettingsStore】：M8 各模块并行开发，直接引用会造成编译竞争。
//   初值取本类序列化默认值；设置面板推值走 SetBusVolume / 属性，再调 ApplyVolumesFromSettings()。
// ============================================================
namespace SuperSmashLike.Core
{
    /// <summary>音频总线分组。与 mixer 暴露参数一一对应（MasterVolume/SFXVolume/BGMVolume/UIVolume）。</summary>
    public enum AudioBus { Master = 0, SFX = 1, BGM = 2, UI = 3 }

    public class AudioManager : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Config")]
        public AudioCatalog catalog;

        [Tooltip("可选：若指定 AudioMixer，则用暴露参数 MasterVolume/SFXVolume/BGMVolume/UIVolume（dB）控制；为空则用 AudioSource 增益")]
        public UnityEngine.Audio.AudioMixer mixer;

        [Tooltip("Start 时订阅 GameManager 状态事件，随 Title/Select/Battle/Result 自动切 BGM")]
        public bool autoBindGameStateMusic = true;

        [Header("默认音量（无外部设置数据时的初值，与 SettingsData.audio 默认一致）")]
        [Range(0f, 1f)] public float defaultMaster = 0.8f;
        [Range(0f, 1f)] public float defaultSfx = 0.9f;
        [Range(0f, 1f)] public float defaultBgm = 0.6f;
        [Range(0f, 1f)] public float defaultUi = 0.8f;

        #endregion

        #region 2. 运行时状态

        public static AudioManager Instance { get; private set; }

        // 分组音量（0..1）与静音；实际播放音量 = volumeScale × bus × master
        private float _master;
        private float _sfx;
        private float _bgm;
        private float _ui;
        private bool _muted;

        private const int SfxVoiceCount = 16;   // 同屏最多 16 路并发音效，满了复用最早播放的
        private const int UiVoiceCount = 2;     // UI 提示音独立 2 路，避免被战斗音效挤掉

        private AudioSource[] _sfxSources;      // SFX 声部池
        private AudioSource[] _uiSources;       // UI 声部池
        private float[] _sfxStartedAt;          // unscaledTime，用于"复用最早播放的"
        private float[] _uiStartedAt;
        private float[] _sfxScale;              // 每路最近一次播放的 volumeScale（音量变化时重刷）
        private float[] _uiScale;

        private AudioSource _bgmSource;         // BGM 独立 1 路
        private float _bgmFade = 1f;            // 淡入淡出系数 0..1（与总线音量相乘）
        private Coroutine _bgmRoutine;          // 当前淡入淡出协程（切换时先停旧的）

        private bool _subscribedGameState;      // 是否已订阅 GameManager 状态事件

        #endregion

        #region 3. 音量属性与总线 API

        // 【为什么用带 body 的属性而不是自动属性】：调用方改完音量即时生效，
        //   不必记得再调 ApplyVolumes()（设置面板滑块拖动场景）。
        public float MasterVolume
        {
            get => _master;
            set { _master = Mathf.Clamp01(value); ApplyVolumes(); }
        }

        public float SfxVolume
        {
            get => _sfx;
            set { _sfx = Mathf.Clamp01(value); ApplyVolumes(); }
        }

        public float BgmVolume
        {
            get => _bgm;
            set { _bgm = Mathf.Clamp01(value); ApplyVolumes(); }
        }

        public float UiVolume
        {
            get => _ui;
            set { _ui = Mathf.Clamp01(value); ApplyVolumes(); }
        }

        public bool Muted
        {
            get => _muted;
            set { _muted = value; ApplyVolumes(); }
        }

        public void SetBusVolume(AudioBus bus, float volume01)
        {
            switch (bus)
            {
                case AudioBus.Master: MasterVolume = volume01; break;
                case AudioBus.SFX: SfxVolume = volume01; break;
                case AudioBus.BGM: BgmVolume = volume01; break;
                case AudioBus.UI: UiVolume = volume01; break;
            }
        }

        public float GetBusVolume(AudioBus bus)
        {
            return bus switch
            {
                AudioBus.Master => _master,
                AudioBus.SFX => _sfx,
                AudioBus.BGM => _bgm,
                AudioBus.UI => _ui,
                _ => 0f,
            };
        }

        // 把当前音量刷到 mixer 参数 / 全部 AudioSource（幂等，可反复调用）
        public void ApplyVolumes()
        {
            float master = EffectiveMaster();

            // 路径 A：有 mixer —— 写 dB 参数；AudioSource.volume 只保留 volumeScale
            if (mixer != null)
            {
                mixer.SetFloat("MasterVolume", VolumeToDb(master));
                mixer.SetFloat("SFXVolume", VolumeToDb(_sfx * master));
                mixer.SetFloat("BGMVolume", VolumeToDb(_bgm * master));
                mixer.SetFloat("UIVolume", VolumeToDb(_ui * master));
            }

            // 路径 B / A 的声部侧：无 mixer 时把 bus×master 乘进去；有 mixer 时只恢复 volumeScale
            RefreshSourceVolumes();
        }

        // 设置面板推完音量后的统一生效入口（本类不读 SettingsStore，由调用方负责推值）
        public void ApplyVolumesFromSettings()
        {
            ApplyVolumes();
        }

        // 0 -> -80dB（Unity AudioMixer 的静音下限），1 -> 0dB，中间 20*log10
        public static float VolumeToDb(float v01)
        {
            if (v01 <= 0.0001f) return -80f;
            return Mathf.Clamp(Mathf.Log10(v01) * 20f, -80f, 0f);
        }

        // VolumeToDb 的逆变换：-80 -> 0（完全静音），0 -> 1
        public static float DbToVolume(float db)
        {
            if (db <= -80f) return 0f;
            return Mathf.Clamp01(Mathf.Pow(10f, db / 20f));
        }

        #endregion

        #region 4. SFX / UI 播放

        public void PlaySFX(SfxId id, float volumeScale = 1f, float pitch = 1f)
        {
            if (catalog == null) return;              // 无目录：静默降级
            PlaySFX(catalog.GetSfx(id), volumeScale, pitch);
        }

        public void PlaySFX(AudioClip clip, float volumeScale = 1f, float pitch = 1f)
        {
            if (clip == null) return;                 // 资源缺失：静默 no-op，不刷警告
            var sources = EnsureSfxPool();
            int i = RentVoice(sources, _sfxStartedAt);
            _sfxScale[i] = volumeScale;
            _sfxStartedAt[i] = Time.unscaledTime;
            PlayOn(sources[i], clip, AudioBus.SFX, volumeScale, pitch);
        }

        // UI 音独立走 UI 总线（战斗音效再挤也不影响菜单反馈音量）
        public void PlayUI(SfxId id, float volumeScale = 1f)
        {
            if (catalog == null) return;
            var clip = catalog.GetSfx(id);
            if (clip == null) return;

            var sources = EnsureUiPool();
            int i = RentVoice(sources, _uiStartedAt);
            _uiScale[i] = volumeScale;
            _uiStartedAt[i] = Time.unscaledTime;
            PlayOn(sources[i], clip, AudioBus.UI, volumeScale, pitch: 1f);
        }

        // 静态安全入口：AudioManager 不存在时静默 no-op（调用点无需判空）
        public static void Sfx(SfxId id, float volumeScale = 1f, float pitch = 1f)
        {
            if (Instance == null) return;
            Instance.PlaySFX(id, volumeScale, pitch);
        }

        public static void Ui(SfxId id, float volumeScale = 1f)
        {
            if (Instance == null) return;
            Instance.PlayUI(id, volumeScale);
        }

        // 场景中查找已有实例；找不到返回 null（不自动创建，避免无 catalog 的哑管理器）
        public static AudioManager EnsureInstance()
        {
            return FindObjectOfType<AudioManager>(true);
        }

        #endregion

        #region 5. BGM 播放

        public void PlayMusic(MusicId id, bool loop = true, float fadeSeconds = 0.4f)
        {
            if (catalog == null) return;
            PlayMusic(catalog.GetMusic(id), loop, fadeSeconds);
        }

        public void PlayMusic(AudioClip clip, bool loop = true, float fadeSeconds = 0.4f)
        {
            if (clip == null || _bgmSource == null) return;

            // 同一首正在播：不重启（避免状态抖动导致 BGM 从头再来）
            if (_bgmRoutine == null && _bgmSource.isPlaying && _bgmSource.clip == clip) return;

            if (_bgmRoutine != null) { StopCoroutine(_bgmRoutine); _bgmRoutine = null; }

            if (fadeSeconds <= 0f || !_bgmSource.isPlaying)
            {
                SwitchBgm(clip, loop);
                _bgmFade = 1f;
                RefreshBgmVolume();
                return;
            }
            _bgmRoutine = StartCoroutine(CrossfadeRoutine(clip, loop, fadeSeconds));
        }

        // 随 GameManager 状态切曲（Paused/Boot/BattleLoading 不在映射内 => 保持当前 BGM）
        public void PlayMusicForState(GameState state)
        {
            switch (state)
            {
                case GameState.Title:
                    PlayMusic(MusicId.Title); break;
                case GameState.CharacterSelect:
                case GameState.StageSelect:
                case GameState.RuleSelect:
                    PlayMusic(MusicId.Select); break;
                case GameState.Battle:
                case GameState.Training:
                    PlayMusic(MusicId.Battle); break;
                case GameState.Result:
                    PlayMusic(MusicId.Result); break;
                // Paused：继续播当前曲；Boot / BattleLoading：尚未进入主题阶段，不动
                default:
                    break;
            }
        }

        public void StopMusic(float fadeSeconds = 0.4f)
        {
            if (_bgmSource == null) return;
            if (_bgmRoutine != null) { StopCoroutine(_bgmRoutine); _bgmRoutine = null; }

            if (fadeSeconds <= 0f || !_bgmSource.isPlaying)
            {
                _bgmSource.Stop();
                _bgmFade = 0f;
                return;
            }
            _bgmRoutine = StartCoroutine(StopRoutine(fadeSeconds));
        }

        #endregion

        #region 6. Unity 生命周期

        private void Awake()
        {
            // 单例纪律：重复实例直接销毁（跨场景常驻的只有一个）
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // 初值取序列化默认值（不引用 SettingsStore，见文件头说明）
            _master = Mathf.Clamp01(defaultMaster);
            _sfx = Mathf.Clamp01(defaultSfx);
            _bgm = Mathf.Clamp01(defaultBgm);
            _ui = Mathf.Clamp01(defaultUi);
            _muted = false;

            BuildSources();   // 全部 AudioSource 代码创建，无需预制体
            ApplyVolumes();
        }

        private void Start()
        {
            if (!autoBindGameStateMusic) return;

            var gm = GameManager.Instance;
            if (gm == null) return;   // GameManager 尚未就绪：不订阅（静态入口仍可用）

            gm.OnGameStateChanged += OnGameStateChangedHandler;
            _subscribedGameState = true;

            // 进场景时可能已处于某状态（如直接进战斗），先同步一次当前状态的 BGM
            PlayMusicForState(gm.CurrentGameState);
        }

        private void OnDestroy()
        {
            if (_subscribedGameState)
            {
                // GameManager 可能已先被销毁，Unity 假 null 判断保护
                if (GameManager.Instance != null)
                    GameManager.Instance.OnGameStateChanged -= OnGameStateChangedHandler;
                _subscribedGameState = false;
            }

            if (Instance == this) Instance = null;
        }

        #endregion

        #region 7. 调试入口

        [ContextMenu("Test SFX")]
        private void DebugTestSfx()
        {
            PlaySFX(SfxId.HitHeavy, 1f, Random.Range(0.95f, 1.05f));
        }

        [ContextMenu("Test BGM")]
        private void DebugTestBgm()
        {
            PlayMusic(MusicId.Battle, true, 0.4f);
        }

        #endregion

        #region 8. 私有工具 — 声部池

        // 懒创建：Awake 里 BuildSources 已建好，这里只兜底（防止脚本被外部禁用重建）
        private AudioSource[] EnsureSfxPool()
        {
            if (_sfxSources == null || _sfxSources.Length != SfxVoiceCount) BuildSources();
            return _sfxSources;
        }

        private AudioSource[] EnsureUiPool()
        {
            if (_uiSources == null || _uiSources.Length != UiVoiceCount) BuildSources();
            return _uiSources;
        }

        // 选一路空闲声部；全忙则复用最早开始播放的（合同要求：满了复用最早播放的）
        private static int RentVoice(AudioSource[] sources, float[] startedAt)
        {
            int oldest = 0;
            for (int i = 0; i < sources.Length; i++)
            {
                if (!sources[i].isPlaying) return i;
                if (startedAt[i] < startedAt[oldest]) oldest = i;
            }
            return oldest;
        }

        private void PlayOn(AudioSource src, AudioClip clip, AudioBus bus, float volumeScale, float pitch)
        {
            src.clip = clip;
            src.pitch = pitch > 0f ? pitch : 1f;
            src.volume = volumeScale * BusGain(bus);   // 有 mixer 时 BusGain=1（dB 由 mixer 处理）
            src.Play();
        }

        // 无 mixer：返回 bus×master 的线性增益；有 mixer：返回 1（增益已在 mixer 参数里）
        private float BusGain(AudioBus bus)
        {
            if (mixer != null) return 1f;
            float master = EffectiveMaster();
            return bus switch
            {
                AudioBus.SFX => _sfx * master,
                AudioBus.BGM => _bgm * master,
                AudioBus.UI => _ui * master,
                _ => master,
            };
        }

        // Muted 时 master 视为 0（所有总线连带静音）
        private float EffectiveMaster() => _muted ? 0f : _master;

        // 把最新增益刷回所有正在使用的声部（音量/静音变更时保持已播声音同步变化）
        private void RefreshSourceVolumes()
        {
            if (_sfxSources != null)
                for (int i = 0; i < _sfxSources.Length; i++)
                    _sfxSources[i].volume = _sfxScale[i] * BusGain(AudioBus.SFX);

            if (_uiSources != null)
                for (int i = 0; i < _uiSources.Length; i++)
                    _uiSources[i].volume = _uiScale[i] * BusGain(AudioBus.UI);

            RefreshBgmVolume();
        }

        private void RefreshBgmVolume()
        {
            if (_bgmSource != null)
                _bgmSource.volume = _bgmFade * BusGain(AudioBus.BGM);
        }

        private void BuildSources()
        {
            _sfxSources = new AudioSource[SfxVoiceCount];
            _sfxStartedAt = new float[SfxVoiceCount];
            _sfxScale = new float[SfxVoiceCount];
            for (int i = 0; i < SfxVoiceCount; i++)
                _sfxSources[i] = CreateChild("SFX_" + i);

            _uiSources = new AudioSource[UiVoiceCount];
            _uiStartedAt = new float[UiVoiceCount];
            _uiScale = new float[UiVoiceCount];
            for (int i = 0; i < UiVoiceCount; i++)
                _uiSources[i] = CreateChild("UI_" + i);

            if (_bgmSource == null)
            {
                _bgmSource = CreateChild("BGM");
                _bgmSource.loop = true;
            }
        }

        // 创建挂载在本物体下的 2D 音源（playOnAwake 关闭，全部由代码显式驱动）
        private AudioSource CreateChild(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;          // 纯 2D，不随距离衰减
            return src;
        }

        #endregion

        #region 9. 私有工具 — BGM 协程

        // 换曲核心：换 clip / loop 并立即起播（淡入淡出由协程负责调节 _bgmFade）
        private void SwitchBgm(AudioClip clip, bool loop)
        {
            _bgmSource.clip = clip;
            _bgmSource.loop = loop;
            _bgmSource.Play();
        }

        // 交叉淡化协程：旧曲淡出 -> 换曲 -> 新曲淡入。
        // 【为什么用 WaitForSecondsRealtime】：暂停时 Time.timeScale=0，
        //   WaitForSeconds 会永久卡住，realtime 版本在暂停菜单里也能正常淡。
        private IEnumerator CrossfadeRoutine(AudioClip next, bool loop, float fadeSeconds)
        {
            const float step = 0.05f;   // 每 50ms 推进一步，听感平滑且不吃帧
            int steps = Mathf.Max(1, Mathf.CeilToInt(fadeSeconds / step));
            float stepTime = fadeSeconds / steps;

            // 阶段 1：淡出当前曲（从中断点 _bgmFade 继续，支持连击切歌）
            float from = _bgmFade;
            for (int i = 1; i <= steps; i++)
            {
                yield return new WaitForSecondsRealtime(stepTime);
                _bgmFade = Mathf.Lerp(from, 0f, (float)i / steps);
                RefreshBgmVolume();
            }

            // 阶段 2：换曲（音量保持 0，避免换 clip 瞬间爆音）
            SwitchBgm(next, loop);
            _bgmFade = 0f;
            RefreshBgmVolume();

            // 阶段 3：新曲淡入
            for (int i = 1; i <= steps; i++)
            {
                yield return new WaitForSecondsRealtime(stepTime);
                _bgmFade = Mathf.Lerp(0f, 1f, (float)i / steps);
                RefreshBgmVolume();
            }

            _bgmFade = 1f;
            RefreshBgmVolume();
            _bgmRoutine = null;
        }

        // 停止 BGM 的淡出协程（同上用 realtime，暂停时也能停干净）
        private IEnumerator StopRoutine(float fadeSeconds)
        {
            const float step = 0.05f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(fadeSeconds / step));
            float stepTime = fadeSeconds / steps;

            float from = _bgmFade;
            for (int i = 1; i <= steps; i++)
            {
                yield return new WaitForSecondsRealtime(stepTime);
                _bgmFade = Mathf.Lerp(from, 0f, (float)i / steps);
                RefreshBgmVolume();
            }

            _bgmSource.Stop();
            _bgmFade = 0f;
            RefreshBgmVolume();
            _bgmRoutine = null;
        }

        // GameManager 状态回调：只在有映射的状态上行动（见 PlayMusicForState）
        private void OnGameStateChangedHandler(GameState newState)
        {
            SmashDebug.Log(DebugChannel.State, $"[Audio] 状态 {newState} -> 切 BGM");
            PlayMusicForState(newState);
        }

        #endregion
    }
}
