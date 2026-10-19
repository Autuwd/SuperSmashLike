using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SuperSmashLike.Core;
using SuperSmashLike.InputSystem;

// ============================================================
// SettingsPanel — 全屏设置面板（M8-D3/D4）
// 职责：
//   1. 四页设置 UI（音频 / 画面 / 按键 / 游戏）的交互与数据回填
//   2. 改动即时写 SettingsStore（落盘）并推给 AudioManager / GraphicsSettingsApplier
//   3. 按键重绑定 UI（InputRebindService 的唯一消费者）
// 架构位置：UI 层，挂在 PausePanel 下（默认 inactive，Open/Close 切 SetActive）
// 依赖：SettingsStore（持久化）、GraphicsSettingsApplier（画面应用）、
//       InputRebindService（重绑定）、AudioManager（音量总线，可缺席）
//
// 【重点】_suppressCallbacks 守卫：Open()/OnEnable 从 SettingsStore 回填控件值时，
//   Slider/Dropdown/Toggle 的 value 赋值会触发 onValueChanged —— 不吞掉会把
//   "读取" 误当成 "用户改动"，造成一次多余的 Save + Apply。
//
// 【重点】Esc 屏蔽（见 Update 内注释）：KeyHintBar 与 InputManager 会把 Esc 送进
//   GameManager.TogglePause → ResumeGame，若不拦会让 "Esc 关设置面板" 同帧变成
//   "关面板 + 恢复比赛"。
// ============================================================
namespace SuperSmashLike.UI
{
    public class SettingsPanel : MonoBehaviour
    {
        #region 1. Inspector 配置（字段名被 SettingsPanelWizard 按名赋值，不可改）

        [Header("Root")]
        public GameObject contentRoot;

        [Header("Tabs")]
        public Button tabAudio;
        public Button tabGraphics;
        public Button tabControls;
        public Button tabGameplay;
        public GameObject pageAudio;
        public GameObject pageGraphics;
        public GameObject pageControls;
        public GameObject pageGameplay;

        [Header("Audio")]
        public Slider masterSlider;
        public Slider sfxSlider;
        public Slider bgmSlider;
        public Slider uiSlider;
        public TMP_Text masterValue;
        public TMP_Text sfxValue;
        public TMP_Text bgmValue;
        public TMP_Text uiValue;
        public Toggle muteToggle;

        [Header("Graphics")]
        public TMP_Dropdown resolutionDropdown;
        public TMP_Dropdown fullScreenDropdown;
        public TMP_Dropdown fpsDropdown;
        public TMP_Dropdown qualityDropdown;

        [Header("Controls")]
        public Button schemeKeyboardP1;
        public Button schemeKeyboardP2;
        public Button schemeGamepad;
        public RectTransform controlRowRoot;
        public Button resetControlsButton;
        public TMP_Text controlsHint;
        public InputActionAsset controlsAsset;

        [Header("Gameplay")]
        public TMP_Dropdown defaultModeDropdown;
        public TMP_Dropdown defaultStockDropdown;
        public TMP_Dropdown defaultDifficultyDropdown;
        public TMP_Text languageLabel;

        [Header("Close")]
        public Button closeButton;

        #endregion

        #region 2. 内部状态

        // 【做什么】面板是否打开（PausePanel.Update 用它决定是否停摆导航）
        public bool IsOpen => gameObject.activeSelf;

        // 当前 Tab 高亮色（契约 §8.1：黄色）
        private static readonly Color TabActiveColor = new Color(1f, 0.85f, 0.2f, 1f);

        private bool _suppressCallbacks;   // 程序化回填时吞掉 onValueChanged
        private bool _rebinding;           // 交互式重绑定进行中（吞掉关闭键）
        private string _currentScheme = InputRebindService.SchemeKeyboardP1;

        private TMP_Text[] _tabLabels;     // 四个 Tab 的文字（用于高亮切换）
        private string[] _schemes;         // 与 _schemeButtons 一一对应的方案名
        private TMP_Text[] _schemeLabels;  // 三个方案按钮的文字

        // GameManager.s_lastPauseToggleTime 反射缓存（见 PinPauseCooldown）
        private static FieldInfo s_pauseCooldownField;
        private static bool s_pauseCooldownResolved;

        // 语言行固定文案（契约 §8.1：简体中文（预留））
        private const string LanguageText = "语言：简体中文（预留）";

        #endregion

        #region 3. Unity 生命周期

        private void Awake()
        {
            // master 克隆初始化（幂等：InputRebindService 内部有 _master != null 守卫）
            InputRebindService.Initialize(controlsAsset);

            CacheLabels();
            WireButtons();
        }

        // 【做什么】每次打开面板：订阅事件 + 从 SettingsStore 回填全部控件 + 落到默认页
        // 【重点】回填必须包在 _suppressCallbacks 里 —— value 赋值会触发 onValueChanged
        private void OnEnable()
        {
            InputRebindService.OnOverridesChanged += OnOverridesChanged;

            _suppressCallbacks = true;
            PullAudioFromStore();
            PullGraphicsFromStore();
            PullGameplayFromStore();
            _suppressCallbacks = false;

            SelectTab(0);                       // 默认落在音频页
            SelectScheme(_currentScheme);       // 刷新方案高亮 + 重绑定行
        }

        private void OnDisable()
        {
            InputRebindService.OnOverridesChanged -= OnOverridesChanged;
        }

        private void Update()
        {
            // 【重点】Esc 屏蔽：KeyHintBar.HandleEsc 与 InputManager 的 Pause 动作都会
            //   调 GameManager.TogglePause → Paused 状态下会 ResumeGame（恢复比赛）。
            //   若不拦，"Esc 关设置面板" 会同帧变成 "关面板 + 恢复比赛"，违反契约
            //   "Esc 关闭只关面板，不调 ResumeGame"。
            // 【手段】TogglePause 有 0.2s 静态冷却（GameManager.s_lastPauseToggleTime）：
            //   面板打开期间每帧把该时间戳刷新为当前时刻 → 两条 Pause 通道全程落在
            //   冷却窗内被静默吞掉；面板关闭后停止刷新，0.2s 后 Esc 恢复正常。
            // 【降级】反射失败（字段改名）时静默退化为 "Esc 同帧恢复比赛"，不影响编译。
            PinPauseCooldown();

            // 重绑定等待按键中：K/Esc/B 全交给 RebindingOperation（Esc 是它的取消键）
            if (_rebinding) return;

            var gp = Gamepad.current;
            if (Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.Escape) ||
                (gp != null && gp.buttonEast.wasPressedThisFrame))
                Close();
        }

        #endregion

        #region 4. 公开 API（契约 §8.1）

        // 【做什么】打开面板（默认 inactive，SetActive 触发 OnEnable 回填）
        public void Open()
        {
            gameObject.SetActive(true);
        }

        // 【做什么】只关面板 —— 绝不调 ResumeGame，游戏保持暂停（PausePanel 重新露出）
        public void Close()
        {
            gameObject.SetActive(false);
        }

        #endregion

        #region 5. Tab 切页

        // 【做什么】点击四个 Tab 按钮之一 → 切页 + 高亮
        private void SelectTab(int index)
        {
            if (pageAudio != null)    pageAudio.SetActive(index == 0);
            if (pageGraphics != null) pageGraphics.SetActive(index == 1);
            if (pageControls != null) pageControls.SetActive(index == 2);
            if (pageGameplay != null) pageGameplay.SetActive(index == 3);

            if (_tabLabels == null) return;
            for (int i = 0; i < _tabLabels.Length; i++)
            {
                if (_tabLabels[i] != null)
                    _tabLabels[i].color = i == index ? TabActiveColor : Color.white;
            }
        }

        #endregion

        #region 6. 音频页

        // 【做什么】Master 滑块改动 → 写数据 + 落盘 + 推总线
        private void OnMasterChanged(float v)
        {
            if (_suppressCallbacks) return;
            SettingsStore.Data.audio.master = v;
            if (masterValue != null) masterValue.text = ToPercent(v);
            SettingsStore.NotifyAudioChanged();
            PushBus(AudioBus.Master, v);
        }

        private void OnSfxChanged(float v)
        {
            if (_suppressCallbacks) return;
            SettingsStore.Data.audio.sfx = v;
            if (sfxValue != null) sfxValue.text = ToPercent(v);
            SettingsStore.NotifyAudioChanged();
            PushBus(AudioBus.SFX, v);
        }

        private void OnBgmChanged(float v)
        {
            if (_suppressCallbacks) return;
            SettingsStore.Data.audio.bgm = v;
            if (bgmValue != null) bgmValue.text = ToPercent(v);
            SettingsStore.NotifyAudioChanged();
            PushBus(AudioBus.BGM, v);
        }

        private void OnUiChanged(float v)
        {
            if (_suppressCallbacks) return;
            SettingsStore.Data.audio.ui = v;
            if (uiValue != null) uiValue.text = ToPercent(v);
            SettingsStore.NotifyAudioChanged();
            PushBus(AudioBus.UI, v);
        }

        // 【做什么】静音开关 → 写数据 + 落盘 + 推给 AudioManager（若存在）
        private void OnMuteChanged(bool muted)
        {
            if (_suppressCallbacks) return;
            SettingsStore.Data.audio.muted = muted;
            SettingsStore.NotifyAudioChanged();

            // AudioManager 缺席时静默跳过（契约 §0：所有 AudioManager 调用都要可缺席）
            if (AudioManager.Instance != null)
                AudioManager.Instance.Muted = muted;
        }

        // 【做什么】把音量推给 AudioManager 总线（实例不存在时静默）
        private static void PushBus(AudioBus bus, float v)
        {
            if (AudioManager.Instance != null)
                AudioManager.Instance.SetBusVolume(bus, v);
        }

        // 【做什么】从 SettingsStore 回填音频页（OnEnable 调用，处于 _suppressCallbacks 内）
        private void PullAudioFromStore()
        {
            var a = SettingsStore.Data.audio;

            if (masterSlider != null) masterSlider.value = Mathf.Clamp01(a.master);
            if (sfxSlider != null)    sfxSlider.value    = Mathf.Clamp01(a.sfx);
            if (bgmSlider != null)    bgmSlider.value    = Mathf.Clamp01(a.bgm);
            if (uiSlider != null)     uiSlider.value     = Mathf.Clamp01(a.ui);

            if (masterValue != null) masterValue.text = ToPercent(a.master);
            if (sfxValue != null)    sfxValue.text    = ToPercent(a.sfx);
            if (bgmValue != null)    bgmValue.text    = ToPercent(a.bgm);
            if (uiValue != null)     uiValue.text     = ToPercent(a.ui);

            if (muteToggle != null) muteToggle.isOn = a.muted;
        }

        // 【做什么】0..1 → "80%" 文本（契约：显示 0-100 整数）
        private static string ToPercent(float v)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(v) * 100f) + "%";
        }

        #endregion

        #region 7. 画面页

        private void OnResolutionChanged(int idx)
        {
            if (_suppressCallbacks) return;
            SettingsStore.Data.graphics.resolutionIndex = idx;
            GraphicsSettingsApplier.ApplyResolution(idx);
            SettingsStore.NotifyGraphicsChanged();
        }

        private void OnFullScreenChanged(int idx)
        {
            if (_suppressCallbacks) return;
            SettingsStore.Data.graphics.fullScreenMode = idx;
            GraphicsSettingsApplier.ApplyFullScreenMode(idx);
            SettingsStore.NotifyGraphicsChanged();
        }

        private void OnFpsChanged(int idx)
        {
            if (_suppressCallbacks) return;
            SettingsStore.Data.graphics.fpsLimitIndex = idx;
            GraphicsSettingsApplier.ApplyFpsLimit(idx);
            SettingsStore.NotifyGraphicsChanged();
        }

        private void OnQualityChanged(int idx)
        {
            if (_suppressCallbacks) return;
            SettingsStore.Data.graphics.qualityIndex = idx;
            GraphicsSettingsApplier.ApplyQuality(idx);
            SettingsStore.NotifyGraphicsChanged();
        }

        // 【做什么】从 SettingsStore 回填画面页：先灌候选文本，再回填当前索引
        private void PullGraphicsFromStore()
        {
            SetDropdownOptions(resolutionDropdown, GraphicsSettingsApplier.ResolutionLabels);
            SetDropdownOptions(fullScreenDropdown, GraphicsSettingsApplier.FullScreenLabels);
            SetDropdownOptions(fpsDropdown, GraphicsSettingsApplier.FpsLabels);
            SetDropdownOptions(qualityDropdown, GraphicsSettingsApplier.QualityLabels);

            var g = SettingsStore.Data.graphics;
            SetDropdownValue(resolutionDropdown, g.resolutionIndex);
            SetDropdownValue(fullScreenDropdown, g.fullScreenMode);
            SetDropdownValue(fpsDropdown, g.fpsLimitIndex);
            SetDropdownValue(qualityDropdown, g.qualityIndex);
        }

        #endregion

        #region 8. 按键页

        // 【做什么】切换方案 Tab（键盘P1 / 键盘P2 / 手柄）→ 高亮 + 重建重绑定行
        private void SelectScheme(string scheme)
        {
            _currentScheme = scheme;

            if (_schemeLabels != null && _schemes != null)
            {
                for (int i = 0; i < _schemeLabels.Length; i++)
                {
                    if (_schemeLabels[i] != null)
                        _schemeLabels[i].color = _schemes[i] == scheme ? TabActiveColor : Color.white;
                }
            }

            RefreshControlRows();
        }

        // 【做什么】重建 controlRowRoot 下的重绑定行（每次打开面板 / 方案切换 / 覆盖变化）
        // 【重点】先清空再生成：行是纯运行时产物，不做任何持久化
        private void RefreshControlRows()
        {
            if (controlRowRoot == null) return;

            for (int i = controlRowRoot.childCount - 1; i >= 0; i--)
                Destroy(controlRowRoot.GetChild(i).gameObject);

            var entries = InputRebindService.GetEntries(_currentScheme);
            if (entries.Count == 0)
            {
                SetHint(InputRebindService.Initialized
                    ? "该方案暂无可重绑按键"
                    : "按键服务未初始化");
                return;
            }

            foreach (var e in entries)
                CreateControlRow(e);
        }

        // 【做什么】生成一行：动作名 TMP_Text + 当前按键 Button
        private void CreateControlRow(RebindEntry entry)
        {
            TMP_FontAsset font = controlsHint != null ? controlsHint.font : TMP_Settings.defaultFontAsset;

            var row = new GameObject("Row_" + entry.action, typeof(RectTransform));
            row.transform.SetParent(controlRowRoot, false);

            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.minHeight = 46f;
            rowLe.preferredHeight = 46f;

            var rowHg = row.AddComponent<HorizontalLayoutGroup>();
            rowHg.padding = new RectOffset(12, 12, 4, 4);
            rowHg.spacing = 10f;
            rowHg.childAlignment = TextAnchor.MiddleCenter;
            rowHg.childForceExpandWidth = false;
            rowHg.childForceExpandHeight = false;
            rowHg.childControlWidth = true;
            rowHg.childControlHeight = true;

            // 行底色（半透明，与深色面板区分）
            var rowImg = row.AddComponent<Image>();
            rowImg.color = new Color(1f, 1f, 1f, 0.05f);

            // 动作名（如 "跳跃" / "攻击"）
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(row.transform, false);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = entry.displayName;
            label.fontSize = 24f;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.color = Color.white;
            if (font != null) label.font = font;
            var labelLe = labelGo.AddComponent<LayoutElement>();
            labelLe.minWidth = 140f;
            labelLe.preferredWidth = 160f;

            // 当前按键按钮（点击进入等待态）
            var btnGo = new GameObject("RebindButton", typeof(RectTransform));
            btnGo.transform.SetParent(row.transform, false);
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.22f, 0.34f, 0.55f, 1f);
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            var btnLe = btnGo.AddComponent<LayoutElement>();
            btnLe.minWidth = 220f;
            btnLe.preferredWidth = 260f;

            var btnLabel = CreateText(btnGo.transform, "Text", entry.bindingDisplay, 22f,
                TextAlignmentOptions.Center, font);
            Stretch(btnLabel.rectTransform);

            btn.onClick.AddListener(() => OnRebindClicked(entry, btnLabel));
        }

        // 【做什么】点击某行的按键按钮 → 进入等待态，发起交互式重绑定
        private void OnRebindClicked(RebindEntry entry, TMP_Text btnLabel)
        {
            if (_rebinding) return;   // 已有一次重绑定在等待，忽略连点
            _rebinding = true;

            if (btnLabel != null) btnLabel.text = "按任意键…";
            SetHint("等待按键…（Esc 取消）");

            InputRebindService.BeginRebind(entry.scheme, entry.action, (ok, msg) =>
            {
                _rebinding = false;

                // 无论成功/失败都重建行：成功刷新显示新键，失败恢复原文本
                RefreshControlRows();

                if (ok) SetHint("已更新按键");
                else SetHint(string.IsNullOrEmpty(msg) ? "重绑定失败" : msg);
            });
        }

        // 【做什么】恢复当前方案默认按键（OnOverridesChanged 会触发行刷新）
        private void OnResetControls()
        {
            InputRebindService.ResetScheme(_currentScheme);
            SetHint("已恢复默认按键");
        }

        // 【做什么】InputRebindService.OnOverridesChanged → 重建行
        private void OnOverridesChanged()
        {
            RefreshControlRows();
        }

        // 【做什么】设置面板打开期间持续钉住 TogglePause 冷却（见 Update 注释）
        private static void PinPauseCooldown()
        {
            if (!s_pauseCooldownResolved)
            {
                s_pauseCooldownResolved = true;
                s_pauseCooldownField = typeof(GameManager).GetField(
                    "s_lastPauseToggleTime",
                    BindingFlags.NonPublic | BindingFlags.Static);
            }

            // 反射失败（字段改名等）：静默退化，不打断面板功能
            if (s_pauseCooldownField == null) return;

            s_pauseCooldownField.SetValue(null, Time.unscaledTime);
        }

        // 【做什么】底部提示文案（冲突信息 / 等待态 / 结果）
        private void SetHint(string msg)
        {
            if (controlsHint != null) controlsHint.text = msg;
        }

        #endregion

        #region 9. 游戏页

        private void OnModeChanged(int idx)
        {
            if (_suppressCallbacks) return;
            SettingsStore.Data.gameplay.defaultMatchMode = idx;
            SettingsStore.Save();   // 游戏页无 Notify* 通道，直接落盘
        }

        // 【做什么】默认命数：选项 "1 条/2 条/3 条" → 值 = 索引 + 1
        private void OnStockChanged(int idx)
        {
            if (_suppressCallbacks) return;
            SettingsStore.Data.gameplay.defaultStock = idx + 1;
            SettingsStore.Save();
        }

        private void OnDifficultyChanged(int idx)
        {
            if (_suppressCallbacks) return;
            SettingsStore.Data.gameplay.defaultCpuDifficulty = idx;
            SettingsStore.Save();
        }

        // 【做什么】从 SettingsStore 回填游戏页（候选文本也在此兜底重灌，防 Wizard 漏灌）
        private void PullGameplayFromStore()
        {
            SetDropdownOptions(defaultModeDropdown, new[] { "命数制", "时间制" });
            SetDropdownOptions(defaultStockDropdown, new[] { "1 条", "2 条", "3 条" });
            SetDropdownOptions(defaultDifficultyDropdown, new[] { "简单", "普通", "困难", "疯狂" });

            var g = SettingsStore.Data.gameplay;
            SetDropdownValue(defaultModeDropdown, g.defaultMatchMode);
            SetDropdownValue(defaultStockDropdown, g.defaultStock - 1);   // 值 1..3 → 索引 0..2
            SetDropdownValue(defaultDifficultyDropdown, g.defaultCpuDifficulty);

            if (languageLabel != null) languageLabel.text = LanguageText;
        }

        #endregion

        #region 10. 私有辅助

        // 【做什么】缓存 Tab / 方案按钮的文字（Awake 调用一次）
        private void CacheLabels()
        {
            _tabLabels = new[]
            {
                LabelOf(tabAudio),
                LabelOf(tabGraphics),
                LabelOf(tabControls),
                LabelOf(tabGameplay),
            };

            _schemes = new[]
            {
                InputRebindService.SchemeKeyboardP1,
                InputRebindService.SchemeKeyboardP2,
                InputRebindService.SchemeGamepad,
            };
            _schemeLabels = new[]
            {
                LabelOf(schemeKeyboardP1),
                LabelOf(schemeKeyboardP2),
                LabelOf(schemeGamepad),
            };
        }

        // 【做什么】把全部按钮/滑块/下拉/开关的监听挂上（Awake 调用一次，全部判空）
        private void WireButtons()
        {
            if (tabAudio != null)    tabAudio.onClick.AddListener(() => SelectTab(0));
            if (tabGraphics != null) tabGraphics.onClick.AddListener(() => SelectTab(1));
            if (tabControls != null) tabControls.onClick.AddListener(() => SelectTab(2));
            if (tabGameplay != null) tabGameplay.onClick.AddListener(() => SelectTab(3));

            if (masterSlider != null) masterSlider.onValueChanged.AddListener(OnMasterChanged);
            if (sfxSlider != null)    sfxSlider.onValueChanged.AddListener(OnSfxChanged);
            if (bgmSlider != null)    bgmSlider.onValueChanged.AddListener(OnBgmChanged);
            if (uiSlider != null)     uiSlider.onValueChanged.AddListener(OnUiChanged);
            if (muteToggle != null)   muteToggle.onValueChanged.AddListener(OnMuteChanged);

            if (resolutionDropdown != null)   resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
            if (fullScreenDropdown != null)   fullScreenDropdown.onValueChanged.AddListener(OnFullScreenChanged);
            if (fpsDropdown != null)          fpsDropdown.onValueChanged.AddListener(OnFpsChanged);
            if (qualityDropdown != null)      qualityDropdown.onValueChanged.AddListener(OnQualityChanged);

            if (schemeKeyboardP1 != null)
                schemeKeyboardP1.onClick.AddListener(() => SelectScheme(InputRebindService.SchemeKeyboardP1));
            if (schemeKeyboardP2 != null)
                schemeKeyboardP2.onClick.AddListener(() => SelectScheme(InputRebindService.SchemeKeyboardP2));
            if (schemeGamepad != null)
                schemeGamepad.onClick.AddListener(() => SelectScheme(InputRebindService.SchemeGamepad));

            if (resetControlsButton != null) resetControlsButton.onClick.AddListener(OnResetControls);
            if (closeButton != null)         closeButton.onClick.AddListener(Close);

            if (defaultModeDropdown != null)      defaultModeDropdown.onValueChanged.AddListener(OnModeChanged);
            if (defaultStockDropdown != null)     defaultStockDropdown.onValueChanged.AddListener(OnStockChanged);
            if (defaultDifficultyDropdown != null) defaultDifficultyDropdown.onValueChanged.AddListener(OnDifficultyChanged);
        }

        // 【做什么】取按钮下第一个 TMP 文字（Tab / 方案按钮的标签）
        private static TMP_Text LabelOf(Button b)
        {
            return b != null ? b.GetComponentInChildren<TMP_Text>(true) : null;
        }

        // 【做什么】整表替换 Dropdown 候选文本并刷新显示
        private static void SetDropdownOptions(TMP_Dropdown dd, string[] labels)
        {
            if (dd == null || labels == null) return;

            var opts = new List<string>(labels.Length);
            for (int i = 0; i < labels.Length; i++) opts.Add(labels[i]);

            dd.options.Clear();
            dd.AddOptions(opts);
            dd.RefreshShownValue();
        }

        // 【做什么】回填 Dropdown 当前值（钳制到候选范围，刷新显示文本）
        private static void SetDropdownValue(TMP_Dropdown dd, int idx)
        {
            if (dd == null || dd.options.Count == 0) return;

            dd.value = Mathf.Clamp(idx, 0, dd.options.Count - 1);
            dd.RefreshShownValue();
        }

        // 【做什么】运行时生成 TMP 文字（重绑定行用；向导侧另有一份同名实现）
        private static TMP_Text CreateText(Transform parent, string name, string text,
            float size, TextAlignmentOptions align, TMP_FontAsset font)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = Color.white;
            if (font != null) tmp.font = font;

            return tmp;
        }

        // 【做什么】把 RectTransform 拉伸铺满父物体（运行时生成的文字用）
        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        #endregion
    }
}
