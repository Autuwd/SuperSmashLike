using System.Collections.Generic;
using SuperSmashLike.Core;
using SuperSmashLike.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ============================================================
// SettingsPanelWizard — 一键在 BattleTest 场景搭出设置面板（M8-D3/D4）
// 职责：
//   1. 在 Canvas/PausePanel 下构建完整设置面板（四页 UGUI + 向导专用「设置」按钮）
//   2. 赋好 SettingsPanel 全部 public 引用 + PausePanel 的 settingsButton/settingsPanel
//   3. 幂等：重复运行只重建，不堆叠
// 架构位置：Editor 层（菜单 SuperSmashLike/41. + 静态入口供 -executeMethod）
//
// 【重点】本项目约定不手改 .unity YAML —— 一切场景结构改动经由本向导在编辑器内完成。
//
// 【重点】暂停菜单按钮布局：原四个按钮（出招表 203.8 / 继续 99.72 / 重新开始 -3 /
//   返回标题 -107，间距约 103.6）整体下移一格，「设置」占据最上槽。
//   原因：若把「设置」直接放到 307.8（原间距外推），会与 Title（RectTransform
//   纵向跨度约 289~432）重叠；下移方案保持标题位置与按钮间距不变，全部在屏内。
//   槽位用绝对坐标写死 → 幂等（重复运行不会二次下移）。
// ============================================================
public static class SettingsPanelWizard
{
    private const string ScenePath = "Assets/_Game/Scenes/BattleTest.unity";
    private const string PanelName = "SettingsPanel";
    private const string SettingsBtnName = "SettingsBtn";
    private const string ControlsAssetPath = "Assets/_Game/Input/Controls.inputactions";
    private const string FontAssetPath = "Assets/_Game/Fonts/MSYH_Dynamic SDF.asset";

    // 暂停菜单按钮槽位（见文件头注释）
    private const float SlotX = 35f;
    private const float SlotTop = 203.8f;      // 第 0 槽：设置
    private const float SlotStep = 103.6f;     // 槽间距

    private static readonly Color TabButtonBg = new Color(0.16f, 0.19f, 0.27f, 1f);
    private static readonly Color PageRowBg = new Color(1f, 1f, 1f, 0.05f);
    private static readonly Color ControlBg = new Color(0.15f, 0.17f, 0.22f, 1f);

    [MenuItem("SuperSmashLike/41. Build Settings Panel (M8-D3/D4)", false, 41)]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // ---------- 1. 定位 Canvas / PausePanel ----------
        var pausePanelT = FindInScene("PausePanel");
        if (pausePanelT == null)
        {
            Debug.LogError("[SettingsPanelWizard] 未找到 PausePanel，中止。");
            return;
        }
        var pausePanel = pausePanelT.GetComponent<PausePanel>();
        if (pausePanel == null)
        {
            Debug.LogError("[SettingsPanelWizard] PausePanel 上没有 PausePanel 组件，中止。");
            return;
        }

        // ---------- 2. 字体：优先复用场景现有 TMP ----------
        TMP_FontAsset font = ResolveFont(pausePanelT);

        // ---------- 3. 幂等清理：销毁旧产物 ----------
        DestroyChildIfExists(pausePanelT, PanelName);
        Transform oldBtn = FindDirectChild(pausePanelT, SettingsBtnName);
        if (oldBtn != null) Object.DestroyImmediate(oldBtn.gameObject);

        // ---------- 4. 「设置」按钮：克隆出招表按钮样式 + 五个槽位落位 ----------
        var moveListBtnT = FindDeep(pausePanelT, "MoveListButton");
        if (moveListBtnT == null)
        {
            Debug.LogError("[SettingsPanelWizard] 未找到 MoveListButton，无法克隆按钮样式，中止。");
            return;
        }

        var settingsBtnT = Object.Instantiate(moveListBtnT, pausePanelT);
        settingsBtnT.name = SettingsBtnName;
        var settingsBtn = settingsBtnT.GetComponent<Button>();
        var settingsBtnLabel = settingsBtnT.GetComponentInChildren<TextMeshProUGUI>(true);
        if (settingsBtnLabel != null)
        {
            if (font != null) settingsBtnLabel.font = font;
            settingsBtnLabel.text = "设置";
        }
        // 克隆体可能带运行时导航残留，清掉（运行时 PausePanel.BuildNavButtons 会重建）
        var nav = settingsBtn.navigation;
        nav.mode = Navigation.Mode.None;
        settingsBtn.navigation = nav;

        // 五个按钮按槽位落位（绝对坐标 → 幂等）
        PlaceAtSlot(FindDeep(pausePanelT, SettingsBtnName), 0);
        PlaceAtSlot(moveListBtnT, 1);
        PlaceAtSlot(FindDeep(pausePanelT, "ResumeButton"), 2);
        PlaceAtSlot(FindDeep(pausePanelT, "RestartButton"), 3);
        PlaceAtSlot(FindDeep(pausePanelT, "QuitButton"), 4);

        // ---------- 5. 构建 SettingsPanel ----------
        GameObject panelGo = BuildPanel(pausePanelT, font, out var sp);

        // ---------- 6. 字段赋值 ----------
        WireSettingsPanel(sp);

        // PausePanel 的两个字段是 private [SerializeField] → SerializedObject 赋值
        var so = new SerializedObject(pausePanel);
        so.FindProperty("settingsButton").objectReferenceValue = settingsBtn;
        so.FindProperty("settingsPanel").objectReferenceValue = sp;
        so.ApplyModifiedPropertiesWithoutUndo();

        // ---------- 7. UI 音效绑定器：挂到 Canvas 根（幂等） ----------
        EnsureUiAudioBinder(pausePanelT);

        // 【收尾】面板默认 inactive（Open/Close 切换），向导跑完不打扰暂停菜单
        panelGo.SetActive(false);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[SettingsPanelWizard] 完成：{ScenePath} 已构建 Canvas/PausePanel/{PanelName}" +
                  "（设置按钮 + 音频/画面/按键/游戏四页 + 按键重绑定）+ Canvas 级 UiAudioBinder，场景已保存。");
    }

    // ============================================================
    // 场景查找
    // ============================================================

    // 【做什么】遍历活动场景全部根物体，按名深度查找（不用 GameObject.Find —— 只找 active 对象）
    private static Transform FindInScene(string name)
    {
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == name) return root.transform;
            var hit = FindDeep(root.transform, name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    // 【做什么】只在直接子物体里找（幂等清理用，避免误删深层同名物体）
    private static Transform FindDirectChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            if (parent.GetChild(i).name == name) return parent.GetChild(i);
        }
        return null;
    }

    private static void DestroyChildIfExists(Transform parent, string name)
    {
        var t = FindDirectChild(parent, name);
        if (t != null) Object.DestroyImmediate(t.gameObject);
    }

    // 【做什么】字体解析：场景现有 TMP → 指定资产路径 → 全工程搜索 → TMP 默认
    private static TMP_FontAsset ResolveFont(Transform pausePanelT)
    {
        var existing = pausePanelT.GetComponentInChildren<TextMeshProUGUI>(true);
        if (existing != null && existing.font != null) return existing.font;

        var fromPath = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (fromPath != null) return fromPath;

        string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font != null) return font;
        }

        return TMP_Settings.defaultFontAsset;
    }

    // 【做什么】把暂停菜单按钮放到指定槽位（只动 anchoredPosition 的 y，x/尺寸保持原样）
    private static void PlaceAtSlot(Transform t, int slot)
    {
        var rt = t != null ? t as RectTransform : null;
        if (rt == null) return;
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, SlotTop - slot * SlotStep);
    }

    // 【做什么】确保 UI 音效绑定器挂在 Canvas 根上（M8-D2 的 UiAudioBinder 依赖 GetComponentInParent<Canvas>）
    // 【幂等】Canvas 上已有该组件则直接复用，不重复添加
    // 【注意】只挂一次即可覆盖整个 Canvas 下的按钮/滑条；面板默认 inactive 也会被 GetComponentsInChildren(true) 扫到
    private static void EnsureUiAudioBinder(Transform anyUnderCanvas)
    {
        var canvas = anyUnderCanvas != null ? anyUnderCanvas.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
        {
            Debug.LogWarning("[SettingsPanelWizard] 未找到 Canvas，跳过 UiAudioBinder 挂载。");
            return;
        }

        if (canvas.GetComponent<UiAudioBinder>() != null)
        {
            Debug.Log($"[SettingsPanelWizard] UiAudioBinder 已存在于 {canvas.name}，复用。");
            return;
        }

        canvas.gameObject.AddComponent<UiAudioBinder>();
        Debug.Log($"[SettingsPanelWizard] 已在 {canvas.name} 挂载 UiAudioBinder。");
    }

    // ============================================================
    // 面板构建
    // ============================================================

    // 【做什么】搭出 SettingsPanel 完整层级并挂组件；返回根物体与组件
    private static GameObject BuildPanel(Transform pausePanelT, TMP_FontAsset font, out SettingsPanel sp)
    {
        // ---- 根：全屏压暗遮罩 ----
        var panelGo = CreateUi(PanelName, pausePanelT);
        StretchFull(panelGo.GetComponent<RectTransform>());
        var panelImg = panelGo.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.55f);   // raycastTarget 默认 true，挡住下层点击
        sp = panelGo.AddComponent<SettingsPanel>();

        // ---- 内容盒 ----
        var content = CreateUi("Content", panelGo.transform);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0.5f, 0.5f);
        contentRt.anchorMax = new Vector2(0.5f, 0.5f);
        contentRt.pivot = new Vector2(0.5f, 0.5f);
        contentRt.sizeDelta = new Vector2(1060f, 700f);
        contentRt.anchoredPosition = Vector2.zero;
        // 【注意】内容盒必须完全不透明（alpha=1）：0.98 会让父级 PausePanel 的
        // 暂停菜单按钮（设置/出招表/继续/重新开始/返回标题）透出来，视觉上像"按钮压在面板上"。
        // 实测 0.98 时按钮处像素 R38/G42/B56 vs 面板底色 R34/G34/B39，肉眼可见。改为 1.0 彻底遮住。
        content.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.14f, 1f);

        // ---- 四个 Tab（内容盒顶部左起）----
        string[] tabNames = { "TabAudio", "TabGraphics", "TabControls", "TabGameplay" };
        string[] tabTexts = { "音频", "画面", "按键", "游戏" };
        var tabs = new Button[4];
        for (int i = 0; i < 4; i++)
        {
            var tab = CreateButton(content.transform, tabNames[i], tabTexts[i], font, TabButtonBg);
            var rt = tab.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(180f, 52f);
            rt.anchoredPosition = new Vector2(24f + i * 192f, -24f);
            tabs[i] = tab;
        }

        // ---- 关闭按钮（内容盒右上）----
        var close = CreateButton(content.transform, "CloseButton", "关闭", font, TabButtonBg);
        var closeRt = close.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1f, 1f);
        closeRt.anchorMax = new Vector2(1f, 1f);
        closeRt.pivot = new Vector2(1f, 1f);
        closeRt.sizeDelta = new Vector2(130f, 52f);
        closeRt.anchoredPosition = new Vector2(-24f, -24f);

        // ---- 四个页（内容盒主体区域，Tab 下方）----
        GameObject pageAudio    = CreatePage(content.transform, "PageAudio");
        GameObject pageGraphics = CreatePage(content.transform, "PageGraphics");
        GameObject pageControls = CreatePage(content.transform, "PageControls");
        GameObject pageGameplay = CreatePage(content.transform, "PageGameplay");

        // ---- 音频页：四个音量滑块行 + 静音开关行 ----
        BuildAudioPage(pageAudio.transform, font,
            out var masterS, out var sfxS, out var bgmS, out var uiS,
            out var masterV, out var sfxV, out var bgmV, out var uiV, out var mute);

        // ---- 画面页：四个下拉行 ----
        BuildGraphicsPage(pageGraphics.transform, font,
            out var resDd, out var fsDd, out var fpsDd, out var qDd);

        // ---- 按键页：方案 Tab + 行容器 + 恢复默认 + 提示 ----
        BuildControlsPage(pageControls.transform, font,
            out var schemeP1, out var schemeP2, out var schemeGp,
            out var rowRoot, out var resetBtn, out var hint);

        // ---- 游戏页：三个下拉行 + 语言行 ----
        BuildGameplayPage(pageGameplay.transform, font,
            out var modeDd, out var stockDd, out var diffDd, out var langLabel);

        // ---- 全部引用一次性挂到 SettingsPanel ----
        sp.contentRoot = content;
        sp.tabAudio = tabs[0]; sp.tabGraphics = tabs[1];
        sp.tabControls = tabs[2]; sp.tabGameplay = tabs[3];
        sp.pageAudio = pageAudio; sp.pageGraphics = pageGraphics;
        sp.pageControls = pageControls; sp.pageGameplay = pageGameplay;

        sp.masterSlider = masterS; sp.sfxSlider = sfxS;
        sp.bgmSlider = bgmS; sp.uiSlider = uiS;
        sp.masterValue = masterV; sp.sfxValue = sfxV;
        sp.bgmValue = bgmV; sp.uiValue = uiV;
        sp.muteToggle = mute;

        sp.resolutionDropdown = resDd; sp.fullScreenDropdown = fsDd;
        sp.fpsDropdown = fpsDd; sp.qualityDropdown = qDd;

        sp.schemeKeyboardP1 = schemeP1; sp.schemeKeyboardP2 = schemeP2; sp.schemeGamepad = schemeGp;
        sp.controlRowRoot = rowRoot;
        sp.resetControlsButton = resetBtn;
        sp.controlsHint = hint;
        sp.controlsAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ControlsAssetPath);
        if (sp.controlsAsset == null)
            Debug.LogWarning($"[SettingsPanelWizard] 未找到 {ControlsAssetPath}，按键页运行时将提示未初始化。");

        sp.defaultModeDropdown = modeDd; sp.defaultStockDropdown = stockDd;
        sp.defaultDifficultyDropdown = diffDd; sp.languageLabel = langLabel;
        sp.closeButton = close;

        return panelGo;
    }

    // 【做什么】音频页：主音量/音效/背景音乐/界面音 四行（标签+滑块+百分比）+ 静音行
    private static void BuildAudioPage(Transform page, TMP_FontAsset font,
        out Slider master, out Slider sfx, out Slider bgm, out Slider ui,
        out TMP_Text masterV, out TMP_Text sfxV, out TMP_Text bgmV, out TMP_Text uiV,
        out Toggle mute)
    {
        string[] labels = { "主音量", "音效", "背景音乐", "界面音" };
        var sliders = new Slider[4];
        var values = new TMP_Text[4];

        for (int i = 0; i < 4; i++)
        {
            var row = CreateRow(page, "Row_" + labels[i], i);
            AddRowLabel(row, labels[i], font);

            var slider = CreateSlider(row, "Slider");
            var sliderLe = slider.gameObject.AddComponent<LayoutElement>();
            sliderLe.minWidth = 240f;
            sliderLe.flexibleWidth = 1f;
            // 【注意】Row 的 HorizontalLayoutGroup 开了 childControlHeight，
            // 若不显式给高度，布局系统按 preferredHeight=-1 把滑块压成 0 高 →
            // 轨道/Fill 全部不可见，只剩固定尺寸的 Handle。必须给高度。
            sliderLe.minHeight = 28f;
            sliderLe.preferredHeight = 28f;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            sliders[i] = slider;

            var val = CreateText(row, "Value", "0%", 24f, TextAlignmentOptions.MidlineRight, font);
            var valLe = val.gameObject.AddComponent<LayoutElement>();
            valLe.minWidth = 70f;
            valLe.preferredWidth = 70f;
            values[i] = val;
        }

        // 静音行
        var muteRow = CreateRow(page, "Row_Mute", 4);
        AddRowLabel(muteRow, "静音", font);
        mute = CreateToggle(muteRow, "MuteToggle", "关闭所有声音", font);
        var muteLe = mute.gameObject.AddComponent<LayoutElement>();
        muteLe.minWidth = 200f;
        muteLe.preferredWidth = 220f;

        master = sliders[0]; sfx = sliders[1]; bgm = sliders[2]; ui = sliders[3];
        masterV = values[0]; sfxV = values[1]; bgmV = values[2]; uiV = values[3];
    }

    // 【做什么】画面页：分辨率/显示模式/帧率/画质 四个下拉行（候选项由代码侧兜底灌入）
    private static void BuildGraphicsPage(Transform page, TMP_FontAsset font,
        out TMP_Dropdown res, out TMP_Dropdown fs, out TMP_Dropdown fps, out TMP_Dropdown quality)
    {
        string[] labels = { "分辨率", "显示模式", "帧率上限", "画质" };
        var dds = new TMP_Dropdown[4];

        for (int i = 0; i < 4; i++)
        {
            var row = CreateRow(page, "Row_" + labels[i], i);
            AddRowLabel(row, labels[i], font);

            var dd = CreateDropdown(row, "Dropdown", font, i == 0
                ? new List<string>(GraphicsSettingsApplier.ResolutionLabels)
                : i == 1 ? new List<string>(GraphicsSettingsApplier.FullScreenLabels)
                : i == 2 ? new List<string>(GraphicsSettingsApplier.FpsLabels)
                         : new List<string>(GraphicsSettingsApplier.QualityLabels));
            var ddLe = dd.gameObject.AddComponent<LayoutElement>();
            ddLe.minWidth = 300f;
            ddLe.preferredWidth = 340f;
            dds[i] = dd;
        }

        res = dds[0]; fs = dds[1]; fps = dds[2]; quality = dds[3];
    }

    // 【做什么】按键页：三个方案按钮 + 行容器（VerticalLayoutGroup）+ 恢复默认 + 提示
    private static void BuildControlsPage(Transform page, TMP_FontAsset font,
        out Button p1, out Button p2, out Button gp,
        out RectTransform rowRoot, out Button reset, out TMP_Text hint)
    {
        // 方案 Tab（顶部横排）
        string[] names = { "SchemeP1", "SchemeP2", "SchemeGamepad" };
        string[] texts = { "键盘 P1", "键盘 P2", "手柄" };
        var btns = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            var b = CreateButton(page, names[i], texts[i], font, TabButtonBg);
            var rt = b.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(170f, 48f);
            rt.anchoredPosition = new Vector2(10f + i * 182f, -16f);
            btns[i] = b;
        }

        // 行容器：运行时由 SettingsPanel 生成 7 行（跳跃/攻击/必杀/防御/抓取/嘲讽/暂停）
        var rowGo = CreateUi("ControlRowRoot", page);
        var rowRt = rowGo.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.offsetMin = new Vector2(12f, 0f);
        rowRt.offsetMax = new Vector2(-12f, 0f);
        rowRt.anchoredPosition = Vector2.zero;
        rowRt.sizeDelta = new Vector2(-24f, 0f);

        var vl = rowGo.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(0, 0, 0, 0);
        vl.spacing = 8f;
        vl.childAlignment = TextAnchor.UpperCenter;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;
        vl.childControlWidth = true;
        vl.childControlHeight = true;

        var fitter = rowGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 行容器锚定在方案按钮下方，向下生长
        rowRt.anchoredPosition = new Vector2(0f, -80f);
        rowRoot = rowRt;

        // 恢复默认按键
        reset = CreateButton(page, "ResetControlsButton", "恢复默认按键", font, TabButtonBg);
        var resetRt = reset.GetComponent<RectTransform>();
        resetRt.anchorMin = new Vector2(0f, 1f);
        resetRt.anchorMax = new Vector2(0f, 1f);
        resetRt.pivot = new Vector2(0f, 1f);
        resetRt.sizeDelta = new Vector2(220f, 48f);
        resetRt.anchoredPosition = new Vector2(10f, -480f);

        // 提示行（冲突/等待态文案）
        hint = CreateText(page.transform, "ControlsHint",
            "点击按键后按下新键完成重绑定", 22f, TextAlignmentOptions.MidlineLeft, font);
        hint.color = new Color(1f, 0.85f, 0.2f, 1f);
        var hintRt = hint.rectTransform;
        hintRt.anchorMin = new Vector2(0f, 1f);
        hintRt.anchorMax = new Vector2(1f, 1f);
        hintRt.pivot = new Vector2(0.5f, 1f);
        hintRt.offsetMin = new Vector2(12f, 0f);
        hintRt.offsetMax = new Vector2(-12f, 0f);
        hintRt.anchoredPosition = new Vector2(0f, -540f);
        hintRt.sizeDelta = new Vector2(-24f, 40f);

        p1 = btns[0]; p2 = btns[1]; gp = btns[2];
    }

    // 【做什么】游戏页：默认规则/默认命数/CPU 难度 三个下拉行 + 语言行
    private static void BuildGameplayPage(Transform page, TMP_FontAsset font,
        out TMP_Dropdown mode, out TMP_Dropdown stock, out TMP_Dropdown diff, out TMP_Text lang)
    {
        var modeDd = BuildDropdownRow(page, font, 0, "默认规则",
            new List<string> { "命数制", "时间制" });
        var stockDd = BuildDropdownRow(page, font, 1, "默认命数",
            new List<string> { "1 条", "2 条", "3 条" });
        var diffDd = BuildDropdownRow(page, font, 2, "CPU 难度",
            new List<string> { "简单", "普通", "困难", "疯狂" });

        // 语言行（预留，仅展示）
        var langRow = CreateRow(page, "Row_语言", 3);
        AddRowLabel(langRow, "语言", font);
        lang = CreateText(langRow.transform, "Value", "简体中文（预留）", 24f,
            TextAlignmentOptions.MidlineLeft, font);
        var langLe = lang.gameObject.AddComponent<LayoutElement>();
        langLe.minWidth = 240f;
        lang.color = new Color(0.75f, 0.75f, 0.75f, 1f);

        mode = modeDd; stock = stockDd; diff = diffDd;
    }

    // 【做什么】一行「标签 + 下拉」（游戏页复用）
    private static TMP_Dropdown BuildDropdownRow(Transform page, TMP_FontAsset font,
        int index, string labelText, List<string> options)
    {
        var row = CreateRow(page, "Row_" + labelText, index);
        AddRowLabel(row, labelText, font);

        var dd = CreateDropdown(row, "Dropdown", font, options);
        var ddLe = dd.gameObject.AddComponent<LayoutElement>();
        ddLe.minWidth = 300f;
        ddLe.preferredWidth = 340f;
        return dd;
    }

    // ============================================================
    // UGUI 控件工厂
    // ============================================================

    private static GameObject CreateUi(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void Stretch(RectTransform rt) => StretchFull(rt);

    // 【做什么】页：主体区域（Tab 下方 96px 起，四周留边）
    private static GameObject CreatePage(Transform content, string name)
    {
        var go = CreateUi(name, content);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(28f, 28f);
        rt.offsetMax = new Vector2(-28f, -96f);
        return go;
    }

    // 【做什么】行：顶部对齐、横向布局、按序号下移（音频/画面/游戏页共用）
    private static RectTransform CreateRow(Transform page, string name, int index, float height = 72f)
    {
        var go = CreateUi(name, page);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(-12f, height);
        rt.anchoredPosition = new Vector2(0f, -16f - index * (height + 12f));

        var hg = go.AddComponent<HorizontalLayoutGroup>();
        hg.padding = new RectOffset(8, 8, 4, 4);
        hg.spacing = 12f;
        hg.childAlignment = TextAnchor.MiddleLeft;
        hg.childForceExpandWidth = false;
        hg.childForceExpandHeight = false;
        hg.childControlWidth = true;
        hg.childControlHeight = true;

        return rt;
    }

    // 【做什么】行内标签（固定宽，左对齐）
    private static TMP_Text AddRowLabel(Transform row, string text, TMP_FontAsset font)
    {
        var t = CreateText(row, "Label", text, 24f, TextAlignmentOptions.MidlineLeft, font);
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.minWidth = 170f;
        le.preferredWidth = 190f;
        return t;
    }

    // 【做什么】纯 UI 物体（无 Image）
    private static TMP_Text CreateText(Transform parent, string name, string text,
        float size, TextAlignmentOptions align, TMP_FontAsset font)
    {
        var go = CreateUi(name, parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = align;
        tmp.color = Color.white;
        if (font != null) tmp.font = font;
        return tmp;
    }

    private static Button CreateButton(Transform parent, string name, string label,
        TMP_FontAsset font, Color bg)
    {
        var go = CreateUi(name, parent);
        var img = go.AddComponent<Image>();
        img.color = bg;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.92f, 0.55f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.selectedColor = new Color(1f, 0.85f, 0.2f, 1f);
        colors.fadeDuration = 0.05f;
        btn.colors = colors;

        var text = CreateText(go.transform, "Text", label, 24f, TextAlignmentOptions.Center, font);
        Stretch(text.rectTransform);

        return btn;
    }

    // 【做什么】音量滑块：Background + Fill + Handle 标准结构
    private static Slider CreateSlider(Transform parent, string name)
    {
        var go = CreateUi(name, parent);

        var bg = CreateUi("Background", go.transform);
        Stretch(bg.GetComponent<RectTransform>());
        bg.AddComponent<Image>().color = ControlBg;

        var fillArea = CreateUi("Fill Area", go.transform);
        var fillAreaRt = fillArea.GetComponent<RectTransform>();
        fillAreaRt.anchorMin = new Vector2(0f, 0.25f);
        fillAreaRt.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRt.offsetMin = Vector2.zero;
        fillAreaRt.offsetMax = Vector2.zero;

        var fill = CreateUi("Fill", fillArea.transform);
        Stretch(fill.GetComponent<RectTransform>());
        fill.AddComponent<Image>().color = new Color(0.35f, 0.55f, 0.9f, 1f);

        var handleArea = CreateUi("Handle Slide Area", go.transform);
        Stretch(handleArea.GetComponent<RectTransform>());
        var handleAreaRt = handleArea.GetComponent<RectTransform>();
        handleAreaRt.offsetMin = new Vector2(10f, 0f);
        handleAreaRt.offsetMax = new Vector2(-10f, 0f);

        var handle = CreateUi("Handle", handleArea.transform);
        var handleRt = handle.GetComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(20f, 26f);
        handle.AddComponent<Image>().color = Color.white;

        var slider = go.AddComponent<Slider>();
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handleRt;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    // 【做什么】复选框：Background + Checkmark + Label
    private static Toggle CreateToggle(Transform parent, string name, string label, TMP_FontAsset font)
    {
        var go = CreateUi(name, parent);

        var bg = CreateUi("Background", go.transform);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.5f);
        bgRt.anchorMax = new Vector2(0f, 0.5f);
        bgRt.pivot = new Vector2(0f, 0.5f);
        bgRt.sizeDelta = new Vector2(30f, 30f);
        bgRt.anchoredPosition = Vector2.zero;
        bg.AddComponent<Image>().color = ControlBg;

        var check = CreateUi("Checkmark", bg.transform);
        var checkRt = check.GetComponent<RectTransform>();
        checkRt.anchorMin = Vector2.zero;
        checkRt.anchorMax = Vector2.one;
        checkRt.offsetMin = new Vector2(7f, 7f);
        checkRt.offsetMax = new Vector2(-7f, -7f);
        check.AddComponent<Image>().color = new Color(1f, 0.85f, 0.2f, 1f);

        var text = CreateText(go.transform, "Label", label, 22f, TextAlignmentOptions.MidlineLeft, font);
        var textRt = text.rectTransform;
        textRt.anchorMin = new Vector2(0f, 0f);
        textRt.anchorMax = new Vector2(1f, 1f);
        textRt.offsetMin = new Vector2(40f, 0f);
        textRt.offsetMax = Vector2.zero;

        var toggle = go.AddComponent<Toggle>();
        toggle.targetGraphic = bg.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.transition = Selectable.Transition.ColorTint;
        return toggle;
    }

    // 【做什么】下拉框：标准 TMP_Dropdown 结构（Label + Arrow + Template/Viewport/Content/Item）
    // 【重点】Template 必须 inactive —— TMP 运行时按 option 数克隆 Item
    private static TMP_Dropdown CreateDropdown(Transform parent, string name,
        TMP_FontAsset font, List<string> options)
    {
        var go = CreateUi(name, parent);
        var rootImg = go.AddComponent<Image>();
        rootImg.color = ControlBg;

        var label = CreateText(go.transform, "Label",
            options.Count > 0 ? options[0] : "", 22f, TextAlignmentOptions.MidlineLeft, font);
        var labelRt = label.rectTransform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(12f, 0f);
        labelRt.offsetMax = new Vector2(-36f, 0f);

        var arrow = CreateUi("Arrow", go.transform);
        var arrowRt = arrow.GetComponent<RectTransform>();
        arrowRt.anchorMin = new Vector2(1f, 0f);
        arrowRt.anchorMax = new Vector2(1f, 1f);
        arrowRt.pivot = new Vector2(1f, 0.5f);
        arrowRt.sizeDelta = new Vector2(36f, 0f);
        arrow.AddComponent<Image>().color = new Color(0.8f, 0.8f, 0.8f, 1f);

        // Template
        var template = CreateUi("Template", go.transform);
        var templateRt = template.GetComponent<RectTransform>();
        templateRt.anchorMin = new Vector2(0f, 0f);
        templateRt.anchorMax = new Vector2(1f, 0f);
        templateRt.pivot = new Vector2(0.5f, 1f);
        templateRt.anchoredPosition = new Vector2(0f, 2f);
        templateRt.sizeDelta = new Vector2(0f, 150f);
        template.AddComponent<Image>().color = new Color(0.12f, 0.13f, 0.16f, 1f);

        var viewport = CreateUi("Viewport", template.transform);
        Stretch(viewport.GetComponent<RectTransform>());
        viewport.AddComponent<Image>().color = Color.white;
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = CreateUi("Content", viewport.transform);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.sizeDelta = Vector2.zero;

        var vl = content.AddComponent<VerticalLayoutGroup>();
        vl.childControlWidth = true;
        vl.childControlHeight = false;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;
        vl.spacing = 2f;

        var cf = content.AddComponent<ContentSizeFitter>();
        cf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var item = CreateUi("Item", content.transform);
        var itemImg = item.AddComponent<Image>();
        itemImg.color = new Color(0f, 0f, 0f, 0.15f);
        var itemLe = item.AddComponent<LayoutElement>();
        itemLe.minHeight = 36f;
        itemLe.preferredHeight = 36f;

        var itemBg = CreateUi("Item Background", item.transform);
        Stretch(itemBg.GetComponent<RectTransform>());
        itemBg.AddComponent<Image>().color = new Color(0.2f, 0.22f, 0.28f, 1f);

        var itemCheck = CreateUi("Item Checkmark", item.transform);
        Stretch(itemCheck.GetComponent<RectTransform>());
        itemCheck.GetComponent<RectTransform>().offsetMin = new Vector2(8f, 8f);
        itemCheck.GetComponent<RectTransform>().offsetMax = new Vector2(-8f, -8f);
        itemCheck.AddComponent<Image>().color = new Color(1f, 0.85f, 0.2f, 1f);

        var itemLabel = CreateText(item.transform, "Item Label", "选项", 22f,
            TextAlignmentOptions.MidlineLeft, font);
        var itemLabelRt = itemLabel.rectTransform;
        itemLabelRt.anchorMin = Vector2.zero;
        itemLabelRt.anchorMax = Vector2.one;
        itemLabelRt.offsetMin = new Vector2(12f, 0f);
        itemLabelRt.offsetMax = new Vector2(-12f, 0f);

        var itemToggle = item.AddComponent<Toggle>();
        itemToggle.targetGraphic = itemImg;
        itemToggle.graphic = itemCheck.GetComponent<Image>();

        // 组装 TMP_Dropdown
        var dd = go.AddComponent<TMP_Dropdown>();
        dd.targetGraphic = rootImg;
        dd.captionText = label;
        dd.template = templateRt;
        dd.itemText = itemLabel;
        dd.options = new List<TMP_Dropdown.OptionData>(options.Count);
        foreach (string opt in options) dd.options.Add(new TMP_Dropdown.OptionData(opt));
        dd.RefreshShownValue();

        // 【重点】Template 保持 inactive，TMP 运行时自行克隆展开列表
        template.SetActive(false);

        return dd;
    }

    // ============================================================
    // 字段赋值（SettingsPanel 全部 public 字段 —— 名字与契约 §8.1 一致）
    // ============================================================

    // 【做什么】BuildPanel 内已直接赋值；此方法保留给"复用已有面板"路径（当前未用）
    private static void WireSettingsPanel(SettingsPanel sp)
    {
        if (sp == null) return;
        // 面板本体在 BuildPanel 中已全部赋值，这里只做一次空值体检
        if (sp.controlsAsset == null)
            sp.controlsAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ControlsAssetPath);
    }
}
