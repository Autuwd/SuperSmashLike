using System.IO;
using UnityEngine;

// ============================================================
// SettingsStore — 全局设置的唯一持久化入口（静态类）
// 职责：
//   1. 持有当前设置数据（SettingsData），任何模块通过 SettingsStore.Data 读写
//   2. 把设置以 JSON 单文件形式持久化到 persistentDataPath（不使用 PlayerPrefs）
//   3. 提供变更通知事件（音频 / 画面 / 按键），配套 Notify*Changed 一键保存+广播
// 架构位置：Core 层，被 AudioManager / SettingsPanel / InputRebindService 共同依赖
// 【重点】本文件是项目第一份持久化代码，写入采用「先写 .tmp 再替换」的准原子策略：
//   崩溃时最多丢失一次未保存的改动，不会留下半截损坏的 settings.json
// ============================================================
namespace SuperSmashLike.Core
{
    // ============================================================
    // 数据类（全部 [System.Serializable]，供 JsonUtility 序列化）
    // 【重点】字段名一旦发布不可改名——JSON 里存的就是这些名字，
    //   改名会让老玩家的 settings.json 静默回退到默认值
    // ============================================================

    // 音频设置：四个总线音量（0..1）+ 全局静音
    // AudioManager 在 Awake/Start 读取此数据并应用到 AudioSource / AudioMixer
    [System.Serializable]
    public class AudioSettingsData
    {
        public float master = 0.8f;   // 主音量（乘在所有总线上）
        public float sfx = 0.9f;      // 音效总线
        public float bgm = 0.6f;      // 背景音乐总线
        public float ui = 0.8f;       // UI 音效总线
        public bool muted = false;    // 静音开关（静音时 master 视为 0）
    }

    // 画面设置：四个下拉框索引，具体取值表在 GraphicsSettingsApplier 中
    // 【重点】存的是「索引」而非原始值——分辨率/帧率等都从固定候选表里选，
    //   索引比原始值更稳定（换显示器后 1920x1080 仍有效，而具体 Hz 可能过期）
    [System.Serializable]
    public class GraphicsSettingsData
    {
        public int resolutionIndex = 0;   // GraphicsSettingsApplier.Resolutions 索引
        public int fullScreenMode = 0;    // 0=全屏 1=无边框窗口 2=窗口
        public int fpsLimitIndex = 2;     // GraphicsSettingsApplier.FpsLimits 索引（默认 2 = 不限）
        public int qualityIndex = 2;      // GraphicsSettingsApplier.QualityLabels 索引（默认 高）
    }

    // 游戏性设置：默认规则预设（进入规则界面前的初值）
    [System.Serializable]
    public class GameplaySettingsData
    {
        public int defaultMatchMode = 0;      // 默认规则模式索引
        public int defaultStock = 3;          // 默认命数
        public int defaultCpuDifficulty = 1;  // 默认 CPU 难度（0/1/2/3 = 简单/普通/困难/疯狂，与 GameSettings.GetCpuDifficultyName 对齐）
        public string language = "zh-CN";     // 语言（预留，当前仅简体中文）
    }

    // 按键设置：InputRebindService 把整个 InputActionAsset 的 binding overrides
    // 序列化成一份 JSON 字符串存在这里（单一字段，避免为每个键建结构）
    [System.Serializable]
    public class ControlsSettingsData
    {
        public string overridesJson = "";   // InputActionAsset.ApplyBindingOverridesAsJson 的 JSON
    }

    // 顶层设置数据：版本号 + 四个分区
    [System.Serializable]
    public class SettingsData
    {
        public int version = 1;   // 【重点】格式版本号，将来结构变更时用于迁移判断
        public AudioSettingsData audio = new();
        public GraphicsSettingsData graphics = new();
        public GameplaySettingsData gameplay = new();
        public ControlsSettingsData controls = new();
    }

    // ============================================================
    // SettingsStore 本体（静态类）
    // ============================================================
    public static class SettingsStore
    {
        // ---------- 字段与属性 ----------

        // 【重点】Data 始终非 null：静态初始化器先给默认实例，
        //   调用方（AudioManager.Start 等）即使抢在 Load 之前读也不会炸
        public static SettingsData Data { get; private set; } = new SettingsData();

        // 持久化文件路径：persistentDataPath/supersmashlike/settings.json
        public static string FilePath { get; } =
            Path.Combine(Application.persistentDataPath, "supersmashlike", "settings.json");

        // 是否已完成过一次 Load（Load 幂等性的守卫标记）
        public static bool Loaded { get; private set; }

        // ---------- 变更事件 ----------

        // 【做什么】三个分区各自独立的变更广播，Save 由 Notify* 统一负责
        // 【为什么】订阅方各取所需：AudioManager 只听音频、SettingsPanel 只听画面，
        //   不必在每个回调里再判断"这次改的是哪个分区"
        public static event System.Action OnAudioChanged;
        public static event System.Action OnGraphicsChanged;
        public static event System.Action OnControlsChanged;

        // ---------- 核心 API ----------

        // 【做什么】从磁盘加载设置；幂等——已加载过则直接返回
        // 【行为】文件缺失 -> 保持默认值；JSON 解析失败 -> 默认值 + LogWarning；
        //   加载成功后把画面设置即时应用到 Screen/QualitySettings
        public static void Load()
        {
            if (Loaded) return;   // 幂等守卫：AutoLoad 与手动调用不会重复读盘
            Loaded = true;

            if (!File.Exists(FilePath))
            {
                // 首次运行：无文件，保持 Data 的默认实例
                Data = new SettingsData();
                ApplyGraphicsSafe();
                return;
            }

            try
            {
                string json = File.ReadAllText(FilePath);
                SettingsData parsed = JsonUtility.FromJson<SettingsData>(json);
                Data = parsed ?? new SettingsData();
            }
            catch (System.Exception e)
            {
                // 解析失败（半截文件 / 手改坏 / 编码问题）不致命：回退默认并继续
                Debug.LogWarning($"[SettingsStore] 设置文件解析失败，使用默认值：{e.Message}");
                Data = new SettingsData();
            }

            // 防御：JSON 若把某个分区写成 null，补回默认实例，保证下游判空无忧
            if (Data.audio == null) Data.audio = new AudioSettingsData();
            if (Data.graphics == null) Data.graphics = new GraphicsSettingsData();
            if (Data.gameplay == null) Data.gameplay = new GameplaySettingsData();
            if (Data.controls == null) Data.controls = new ControlsSettingsData();

            ApplyGraphicsSafe();
        }

        // 【做什么】把当前 Data 序列化写入磁盘（准原子：先写 .tmp 再替换）
        // 【重点】绝不向调用方抛异常——IO 失败只 LogError，设置面板的交互不能因磁盘问题崩掉
        public static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonUtility.ToJson(Data, true);

                // 先写临时文件：崩溃时主文件仍是完整的上一份
                string tmpPath = FilePath + ".tmp";
                File.WriteAllText(tmpPath, json);

                // 再替换主文件：Replace 在目标存在时更接近原子；不存在则直接 Move
                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(tmpPath, FilePath, null);
                    }
                    catch
                    {
                        // 某些文件系统不支持 Replace：退化为 删除+移动
                        File.Delete(FilePath);
                        File.Move(tmpPath, FilePath);
                    }
                }
                else
                {
                    File.Move(tmpPath, FilePath);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SettingsStore] 保存设置失败：{e.Message}");
            }
        }

        // 【做什么】恢复全部默认值：重置数据 + 删除磁盘文件 + 重新保存 + 广播三个事件
        public static void ResetAll()
        {
            Data = new SettingsData();

            // 删除旧文件再 Save，保证磁盘上只剩一份默认设置
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SettingsStore] 删除旧设置文件失败：{e.Message}");
            }

            Save();

            // 三个分区全部变化，逐一广播（各自判空）
            OnAudioChanged?.Invoke();
            OnGraphicsChanged?.Invoke();
            OnControlsChanged?.Invoke();
        }

        // ---------- 变更通知（保存 + 广播）----------

        // 【做什么】音频设置改动后的统一出口：落盘 + 广播
        // 【为什么】调用方（SettingsPanel 滑块回调）只改 Data.audio 然后调这一个方法，
        //   保存时机与事件触发时机永远一致，不会出现"改了没存"的漂移
        public static void NotifyAudioChanged()
        {
            Save();
            OnAudioChanged?.Invoke();
        }

        // 画面设置改动后的统一出口：落盘 + 广播（画面本身由调用方先 Apply 再 Notify）
        public static void NotifyGraphicsChanged()
        {
            Save();
            OnGraphicsChanged?.Invoke();
        }

        // 按键覆盖改动后的统一出口：落盘 + 广播
        public static void NotifyControlsChanged()
        {
            Save();
            OnControlsChanged?.Invoke();
        }

        // ---------- 启动自动加载 ----------

        // 【做什么】进入任何场景之前自动 Load 一次
        // 【重点】BeforeSceneLoad：保证 GameManager/AudioManager 的 Awake/Start
        //   读 SettingsStore.Data 时拿到的已是磁盘数据而非默认值
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoLoad()
        {
            Load();
        }

        // ---------- 调试 / 工具 ----------

        // 【做什么】强制重新从磁盘加载（丢弃内存中的未保存改动）
        // 【用途】调试用：手改 settings.json 后不重启 Play 即可生效
        public static void Reload()
        {
            Loaded = false;
            Load();
        }

        // ---------- 私有辅助 ----------

        // 安全应用画面设置：GraphicsSettingsApplier 只碰 Screen/QualitySettings，
        // 在 BeforeSceneLoad 阶段调用是合法的；万一抛异常也不影响设置加载本身
        private static void ApplyGraphicsSafe()
        {
            try
            {
                GraphicsSettingsApplier.ApplyAll(Data.graphics);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SettingsStore] 应用画面设置失败：{e.Message}");
            }
        }
    }
}
