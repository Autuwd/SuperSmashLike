using System.Collections.Generic;
using SuperSmashLike.Core;
using SuperSmashLike.Managers;
using SuperSmashLike.Training;
using SuperSmashLike.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ============================================================
// TrainingSceneWizard — M5 训练模式场景接线（编辑器工具）
// 职责：
//   1. 在 BattleTest 场景中创建 TrainingModeController
//   2. 在 Canvas 下创建 TrainingHudRoot（含 4 个 TMP 文本）
//   3. 自动接线所有引用（player1/player2/trainingHudRoot + HUD 文本）
// 使用：
//   菜单 SuperSmashLike → 40. Setup Training Mode
//   或 -batchmode -executeMethod TrainingSceneWizard.Run
// 【注意】可重复执行（幂等）：已存在的对象会被删除后重建
// ============================================================
public static class TrainingSceneWizard
{
    private const string ScenePath = "Assets/_Game/Scenes/BattleTest.unity";

    [MenuItem("SuperSmashLike/40. Setup Training Mode (M5)", false, 40)]
    public static void Setup()
    {
        Run();
    }

    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError($"[TrainingSceneWizard] 无法打开场景 {ScenePath}");
            return;
        }

        // 查找必要对象
        var canvas = GameObject.Find("Canvas");
        var fighterP1 = GameObject.Find("Fighter_P1");
        var fighterP2 = GameObject.Find("Fighter_P2");

        if (canvas == null)
        {
            Debug.LogError("[TrainingSceneWizard] 场景中找不到 Canvas");
            return;
        }
        if (fighterP1 == null)
        {
            Debug.LogError("[TrainingSceneWizard] 场景中找不到 Fighter_P1");
            return;
        }
        if (fighterP2 == null)
        {
            Debug.LogError("[TrainingSceneWizard] 场景中找不到 Fighter_P2");
            return;
        }

        var fighterCtrl1 = fighterP1.GetComponent<FighterController>();
        var fighterCtrl2 = fighterP2.GetComponent<FighterController>();
        if (fighterCtrl1 == null || fighterCtrl2 == null)
        {
            Debug.LogError("[TrainingSceneWizard] Fighter_P1/P2 缺少 FighterController 组件");
            return;
        }

        // 1. 清理已有对象（幂等）
        var existingTmc = GameObject.Find("TrainingModeController");
        if (existingTmc != null)
        {
            Object.DestroyImmediate(existingTmc);
            Debug.Log("[TrainingSceneWizard] 已删除现有 TrainingModeController");
        }

        // 清理 Canvas 下的 TrainingHudRoot
        Transform canvasTrans = canvas.transform;
        for (int i = canvasTrans.childCount - 1; i >= 0; i--)
        {
            Transform child = canvasTrans.GetChild(i);
            if (child.name == "TrainingHudRoot")
            {
                Object.DestroyImmediate(child.gameObject);
                Debug.Log("[TrainingSceneWizard] 已删除现有 TrainingHudRoot");
            }
        }

        // 2. 创建 TrainingHudRoot
        var hudRoot = new GameObject("TrainingHudRoot", typeof(RectTransform));
        hudRoot.transform.SetParent(canvas.transform, false);
        var hudRect = hudRoot.GetComponent<RectTransform>();
        hudRect.anchorMin = Vector2.zero;
        hudRect.anchorMax = Vector2.one;
        hudRect.offsetMin = Vector2.zero;
        hudRect.offsetMax = Vector2.zero;
        hudRect.pivot = new Vector2(0.5f, 0.5f);

        // 查找现有 TMP 字体
        // 【注意】TitlePanel 在场景里常为 inactive（UI 状态驱动），GameObject.Find 找不到 → 深度查找（含 inactive）
        TMP_FontAsset fontAsset = null;
        var titlePanel = FindDeep(canvas.transform, "TitlePanel");
        if (titlePanel != null)
        {
            var tmpTexts = titlePanel.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (tmpTexts.Length > 0 && tmpTexts[0].font != null)
            {
                fontAsset = tmpTexts[0].font;
            }
        }
        // 兜底：从 Canvas 下任意 TMP（含 inactive）借一个字体
        if (fontAsset == null)
        {
            var anyTmp = canvas.GetComponentInChildren<TextMeshProUGUI>(true);
            if (anyTmp != null) fontAsset = anyTmp.font;
        }
        if (fontAsset == null)
        {
            fontAsset = TMP_Settings.defaultFontAsset;
        }

        // 创建 4 个 TMP 文本
        var p1Damage = CreateTmpText("P1Damage", hudRoot.transform, fontAsset, new Vector2(0.02f, 0.95f), new Vector2(0.25f, 0.08f), TextAlignmentOptions.TopLeft, "P1: 0.0%");
        var p2Damage = CreateTmpText("P2Damage", hudRoot.transform, fontAsset, new Vector2(0.98f, 0.95f), new Vector2(0.25f, 0.08f), TextAlignmentOptions.TopRight, "P2: 0.0%");
        var combo = CreateTmpText("Combo", hudRoot.transform, fontAsset, new Vector2(0.5f, 0.95f), new Vector2(0.3f, 0.08f), TextAlignmentOptions.Top, "Combo: 0");
        var hint = CreateTmpText("Hint", hudRoot.transform, fontAsset, new Vector2(0.5f, 0.02f), new Vector2(0.9f, 0.08f), TextAlignmentOptions.Bottom, "R:重置 T:CPU切换 I:无敌 Shift:慢动作 H:HUD Esc:返回");

        // 添加 TrainingHud 组件
        var trainingHud = hudRoot.AddComponent<TrainingHud>();
        var hudSo = new SerializedObject(trainingHud);
        hudSo.FindProperty("p1DamageText").objectReferenceValue = p1Damage;
        hudSo.FindProperty("p2DamageText").objectReferenceValue = p2Damage;
        hudSo.FindProperty("comboText").objectReferenceValue = combo;
        hudSo.FindProperty("hintText").objectReferenceValue = hint;
        hudSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(trainingHud);

        // 帧数据面板（挂在 TrainingHudRoot 下）
        var frameData = CreateTmpText("FrameData", hudRoot.transform, fontAsset, new Vector2(0.02f, 0.60f), new Vector2(520f, 300f), TextAlignmentOptions.TopLeft, "P1: -");
        frameData.fontSize = 22;
        var framePanel = hudRoot.AddComponent<TrainingFrameDataPanel>();
        var fdSo = new SerializedObject(framePanel);
        fdSo.FindProperty("frameText").objectReferenceValue = frameData;
        fdSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(framePanel);

        // 3. 创建 TrainingModeController
        var tmcGo = new GameObject("TrainingModeController");
        var tmc = tmcGo.AddComponent<TrainingModeController>();
        var boxOverlay = tmcGo.AddComponent<TrainingBoxOverlay>();
        var tmcSo = new SerializedObject(tmc);
        tmcSo.FindProperty("player1").objectReferenceValue = fighterCtrl1;
        tmcSo.FindProperty("player2").objectReferenceValue = fighterCtrl2;
        tmcSo.FindProperty("trainingHudRoot").objectReferenceValue = hudRoot;
        tmcSo.FindProperty("slowMoScale").floatValue = 0.5f;
        tmcSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(tmc);

        // 3.5 训练菜单（Esc 开关）
        BuildTrainingMenu(canvas, fontAsset, tmc, boxOverlay, framePanel);

        // 4. 尝试在 TitlePanel 下创建 TrainingBtn（可选）
        bool createdTrainingBtn = false;
        if (titlePanel != null)
        {
            var existingBtn = titlePanel.Find("TrainingBtn");
            if (existingBtn != null)
            {
                Object.DestroyImmediate(existingBtn.gameObject);
            }
            // 克隆标题界面的"开始"按钮（实际名为 "Start"；优先从 TitlePanel.startButton 引用取，更稳）
            var titlePanelComp = titlePanel.GetComponent<TitlePanel>();
            Transform startBtnTf = null;
            if (titlePanelComp != null)
            {
                var tpSo0 = new SerializedObject(titlePanelComp);
                var sbProp = tpSo0.FindProperty("startButton");
                if (sbProp != null && sbProp.objectReferenceValue is Button sbBtn && sbBtn != null)
                    startBtnTf = sbBtn.transform;
            }
            if (startBtnTf == null)
            {
                foreach (var nm in new[] { "Start", "StartBtn", "StartButton" })
                {
                    var t = titlePanel.Find(nm);
                    if (t != null) { startBtnTf = t; break; }
                }
            }
            if (startBtnTf != null)
            {
                var clone = Object.Instantiate(startBtnTf.gameObject, titlePanel);
                clone.name = "TrainingBtn";
                var rect = clone.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y - 80f);
                }
                var tmp = clone.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null)
                {
                    tmp.text = "训练模式 (T)";
                }
                var btn = clone.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();   // 清运行时监听
                    // 清持久化 onClick（避免克隆按钮继承"开始游戏"行为）
                    var soBtn = new SerializedObject(btn);
                    var onClicks = soBtn.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                    if (onClicks != null && onClicks.isArray) onClicks.arraySize = 0;
                    soBtn.ApplyModifiedPropertiesWithoutUndo();
                }
                if (titlePanelComp != null)
                {
                    var soTp = new SerializedObject(titlePanelComp);
                    soTp.FindProperty("trainingButton").objectReferenceValue = btn;
                    soTp.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(titlePanelComp);
                }
                createdTrainingBtn = true;
                Debug.Log("[TrainingSceneWizard] 已创建 TrainingBtn 并绑定到 TitlePanel.trainingButton");
            }
            else
            {
                Debug.Log("[TrainingSceneWizard] 未找到开始按钮，跳过创建 TrainingBtn");
            }
        }

        // 4.5 主菜单（竖直列表）—— 放在最后，确保隐藏掉上面克隆出的 TrainingBtn
        if (titlePanel != null)
            BuildMainMenu(canvas, fontAsset, titlePanel.gameObject);

        // 4.6 全局底部按键提示条
        BuildKeyHintBar(canvas, fontAsset);

        // 5. 保存场景
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 6. 输出验证信息
        Debug.Log("[TrainingSceneWizard] ===== 完成 =====");
        Debug.Log($"[TrainingSceneWizard] TrainingModeController: {(tmcGo != null ? "OK" : "FAIL")}");
        Debug.Log($"[TrainingSceneWizard] - player1 非空: {(tmc.player1 != null)}");
        Debug.Log($"[TrainingSceneWizard] - player2 非空: {(tmc.player2 != null)}");
        Debug.Log($"[TrainingSceneWizard] - trainingHudRoot 非空: {(tmc.trainingHudRoot != null)}");
        Debug.Log($"[TrainingSceneWizard] TrainingHudRoot: {(hudRoot != null ? "OK" : "FAIL")} (子节点数: {hudRoot.transform.childCount})");
        Debug.Log($"[TrainingSceneWizard] - P1Damage: {(p1Damage != null)}");
        Debug.Log($"[TrainingSceneWizard] - P2Damage: {(p2Damage != null)}");
        Debug.Log($"[TrainingSceneWizard] - Combo: {(combo != null)}");
        Debug.Log($"[TrainingSceneWizard] - Hint: {(hint != null)}");
        Debug.Log($"[TrainingSceneWizard] - FrameData: {(frameData != null)} / FramePanel: {(framePanel != null)}");
        Debug.Log("[TrainingSceneWizard] TrainingBoxOverlay: 已挂载到 TrainingModeController");
        Debug.Log($"[TrainingSceneWizard] TrainingBtn: {(createdTrainingBtn ? "已创建并绑定" : "未创建（依赖快捷键 T）")}");
        Debug.Log("[TrainingSceneWizard] 场景已保存");
    }

    // 训练菜单（Esc 开关）：沿用暂停菜单按钮外观 + 9 行（含"继续"），挂到 Canvas
    private static void BuildTrainingMenu(GameObject canvas, TMP_FontAsset font, TrainingModeController tmc, TrainingBoxOverlay boxOverlay, TrainingFrameDataPanel framePanel)
    {
        var old = canvas.transform.Find("TrainingMenuRoot");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        const int ROWS = 9;

        var menuRoot = new GameObject("TrainingMenuRoot", typeof(RectTransform), typeof(Image));
        menuRoot.transform.SetParent(canvas.transform, false);
        var rect = menuRoot.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(620f, 600f);
        rect.anchoredPosition = Vector2.zero;
        menuRoot.GetComponent<Image>().color = new Color(0.75f, 0.75f, 0.75f, 1f);   // 浅灰不透明

        var title = CreateTmpText("Title", menuRoot.transform, font, new Vector2(0.5f, 0.95f), new Vector2(560f, 50f), TextAlignmentOptions.Center, "训练菜单");
        title.fontSize = 34;

        // 行模板：克隆暂停菜单的按钮（沿用其外观/字体），比自建更像"原菜单"
        Button template = null;
        var pausePanel = FindDeep(canvas.transform, "PausePanel");
        if (pausePanel != null) template = pausePanel.GetComponentInChildren<Button>(true);

        var buttons = new Button[ROWS];
        var labels = new TextMeshProUGUI[ROWS];
        string[] rowTexts = { "继续", "CPU（木桩）", "无敌", "慢动作", "判定框", "帧数据面板", "HUD", "重置位置", "退出训练" };

        for (int i = 0; i < ROWS; i++)
        {
            float y = 0.83f - i * 0.086f;

            GameObject rowGo;
            if (template != null)
            {
                rowGo = Object.Instantiate(template.gameObject, menuRoot.transform);
                rowGo.name = $"Row{i}";
                var so0 = new SerializedObject(rowGo.GetComponent<Button>());
                var oc = so0.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                if (oc != null && oc.isArray) oc.arraySize = 0;
                so0.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                rowGo = new GameObject($"Row{i}", typeof(RectTransform), typeof(Image), typeof(Button));
                rowGo.transform.SetParent(menuRoot.transform, false);
                var fi = rowGo.GetComponent<Image>();
                fi.color = new Color(1f, 1f, 1f, 0.12f);
                rowGo.GetComponent<Button>().targetGraphic = fi;
            }

            var rrt = rowGo.GetComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, y);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(540f, 48f);
            rrt.anchoredPosition = Vector2.zero;

            var rbtn = rowGo.GetComponent<Button>();
            rbtn.onClick.RemoveAllListeners();
            buttons[i] = rbtn;

            var rtxt = rowGo.GetComponentInChildren<TextMeshProUGUI>(true);
            if (rtxt == null)
                rtxt = CreateTmpText($"Label{i}", rowGo.transform, font, new Vector2(0.5f, 0.5f), new Vector2(500f, 42f), TextAlignmentOptions.Left, rowTexts[i]);
            else
                rtxt.text = rowTexts[i];
            rtxt.fontSize = 26;
            rtxt.raycastTarget = false;
            labels[i] = rtxt;
        }

        var panel = menuRoot.AddComponent<TrainingMenuPanel>();
        var so = new SerializedObject(panel);
        so.FindProperty("controller").objectReferenceValue = tmc;
        so.FindProperty("boxOverlay").objectReferenceValue = boxOverlay;
        so.FindProperty("framePanel").objectReferenceValue = framePanel;
        var ab = so.FindProperty("rowButtons"); ab.arraySize = ROWS;
        for (int i = 0; i < ROWS; i++) ab.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
        var al = so.FindProperty("rowLabels"); al.arraySize = ROWS;
        for (int i = 0; i < ROWS; i++) al.GetArrayElementAtIndex(i).objectReferenceValue = labels[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(panel);

        var tso = new SerializedObject(tmc);
        tso.FindProperty("menuPanel").objectReferenceValue = menuRoot;
        tso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(tmc);

        menuRoot.SetActive(false);
        Debug.Log($"[TrainingSceneWizard] TrainingMenuRoot: 已创建（{ROWS} 行，模板={(template != null ? "暂停菜单按钮" : "自建")}）");
    }

    // 主菜单：标签页框架（挂在 TitlePanel 下，替换旧标题按钮）
    private static void BuildMainMenu(GameObject canvas, TMP_FontAsset font, GameObject titlePanel)
    {
        var old = titlePanel.transform.Find("MainMenuRoot");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // 隐藏旧的开始/训练按钮 + 禁用 TitlePanel 逻辑（改由 MainMenuPanel 接管）
        foreach (var t in titlePanel.GetComponentsInChildren<Transform>(true))
            if (t.parent == titlePanel.transform && (t.name == "Start" || t.name == "TrainingBtn"))
                t.gameObject.SetActive(false);
        var tpComp = titlePanel.GetComponent<TitlePanel>();
        if (tpComp != null)
        {
            var soTp = new SerializedObject(tpComp);
            var en = soTp.FindProperty("m_Enabled");
            if (en != null) en.boolValue = false;
            soTp.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tpComp);
        }

        Button template = null;
        var pausePanel = FindDeep(canvas.transform, "PausePanel");
        if (pausePanel != null) template = pausePanel.GetComponentInChildren<Button>(true);

        var menu = new GameObject("MainMenuRoot", typeof(RectTransform), typeof(Image));
        menu.transform.SetParent(titlePanel.transform, false);
        menu.GetComponent<Image>().color = new Color(0.75f, 0.75f, 0.75f, 1f);   // 浅灰不透明（黑字可读）
        menu.GetComponent<Image>().raycastTarget = false;                        // 不吸收点击，只有按钮可点
        var rt = menu.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        string[] tabNames = { "开始游戏", "比赛模式", "训练模式", "冒险模式", "无尽模式", "个人中心", "设置" };
        int n = tabNames.Length;
        var tabButtons = new Button[n];
        var pages = new GameObject[n];

        // 主页：竖直按钮列表（居中）
        const float btnW = 420f, btnH = 56f, btnGap = 14f;
        float totalH = n * btnH + (n - 1) * btnGap;
        for (int i = 0; i < n; i++)
        {
            var b = MakeButton(menu.transform, template, font, $"RootBtn{i}", tabNames[i], new Vector2(0.5f, 0.5f), new Vector2(btnW, btnH));
            b.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, totalH / 2f - btnH / 2f - i * (btnH + btnGap));
            var lt = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (lt != null) lt.color = Color.black;   // 字体黑色
            tabButtons[i] = b;
        }

        // 页面
        for (int i = 0; i < n; i++)
        {
            var page = new GameObject($"Page{i}", typeof(RectTransform), typeof(Image));
            page.transform.SetParent(menu.transform, false);
            page.GetComponent<Image>().color = new Color(0.75f, 0.75f, 0.75f, 1f);   // 浅灰不透明
            page.GetComponent<Image>().raycastTarget = false;
            var prt = page.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(1000f, 640f);
            prt.anchoredPosition = Vector2.zero;
            pages[i] = page;
        }

        // ---- Page0 开始游戏 ----
        CreateTmpText("PlayerTitle", pages[0].transform, font, new Vector2(0.5f, 0.72f), new Vector2(240f, 48f), TextAlignmentOptions.Right, "玩家人数");
        pages[0].transform.Find("PlayerTitle").GetComponent<RectTransform>().anchoredPosition = new Vector2(-330f, 0f);
        var dec1 = MakeButton(pages[0].transform, template, font, "PlayerDec", "-", new Vector2(0.5f, 0.72f), new Vector2(70f, 48f));
        dec1.GetComponent<RectTransform>().anchoredPosition = new Vector2(-130f, 0f);
        var pLabel = CreateTmpText("PlayerCount", pages[0].transform, font, new Vector2(0.5f, 0.72f), new Vector2(90f, 48f), TextAlignmentOptions.Center, "1");
        var inc1 = MakeButton(pages[0].transform, template, font, "PlayerInc", "+", new Vector2(0.5f, 0.72f), new Vector2(70f, 48f));
        inc1.GetComponent<RectTransform>().anchoredPosition = new Vector2(130f, 0f);

        CreateTmpText("AiTitle", pages[0].transform, font, new Vector2(0.5f, 0.55f), new Vector2(240f, 48f), TextAlignmentOptions.Right, "AI 数量");
        pages[0].transform.Find("AiTitle").GetComponent<RectTransform>().anchoredPosition = new Vector2(-330f, 0f);
        var dec2 = MakeButton(pages[0].transform, template, font, "AiDec", "-", new Vector2(0.5f, 0.55f), new Vector2(70f, 48f));
        dec2.GetComponent<RectTransform>().anchoredPosition = new Vector2(-130f, 0f);
        var aLabel = CreateTmpText("AiCount", pages[0].transform, font, new Vector2(0.5f, 0.55f), new Vector2(90f, 48f), TextAlignmentOptions.Center, "1");
        var inc2 = MakeButton(pages[0].transform, template, font, "AiInc", "+", new Vector2(0.5f, 0.55f), new Vector2(70f, 48f));
        inc2.GetComponent<RectTransform>().anchoredPosition = new Vector2(130f, 0f);

        // 设备选择：每个玩家一行（DevRow0..3，显示前 playerCount 行；单人全隐）
        var devRows = new GameObject[4];
        var devLabels = new TMP_Text[4];
        for (int p = 0; p < 4; p++)
        {
            float yy = 0.42f - p * 0.13f;
            var row = new GameObject($"DevRow{p}", typeof(RectTransform));
            row.transform.SetParent(pages[0].transform, false);
            var rrt = row.GetComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, yy);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(900f, 50f);
            rrt.anchoredPosition = Vector2.zero;
            devRows[p] = row;

            var tl = CreateTmpText($"DevTitle{p}", row.transform, font, new Vector2(0.5f, 0.5f), new Vector2(320f, 44f), TextAlignmentOptions.Right, $"P{p + 1} 操作方式");
            tl.rectTransform.anchoredPosition = new Vector2(-340f, 0f);
            var b = MakeButton(row.transform, template, font, $"DevP{p + 1}", p == 1 ? "手柄" : "键盘P1区", new Vector2(0.5f, 0.5f), new Vector2(280f, 48f));
            b.GetComponent<RectTransform>().anchoredPosition = new Vector2(60f, 0f);
            devLabels[p] = b.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        var startBtn = MakeButton(pages[0].transform, template, font, "StartGameBtn", "开始游戏", new Vector2(0.5f, -0.10f), new Vector2(300f, 56f));

        // ---- Page1 比赛模式（占位）----
        CreateTmpText("P1Info", pages[1].transform, font, new Vector2(0.5f, 0.62f), new Vector2(900f, 200f), TextAlignmentOptions.Center,
            "比赛模式（占位）\n个人锦标赛 / 团队赛\n分组 8进4 → 4进2 → 决赛\n（功能待实现）");

        // ---- Page2 训练模式 ----
        var enterTraining = MakeButton(pages[2].transform, template, font, "EnterTrainingBtn", "进入训练模式", new Vector2(0.5f, 0.62f), new Vector2(320f, 56f));
        CreateTmpText("P2Info", pages[2].transform, font, new Vector2(0.5f, 0.45f), new Vector2(900f, 120f), TextAlignmentOptions.Center,
            "训练中按 Esc 打开训练菜单\n（CPU / 无敌 / 慢动作 / 判定框 / 帧数据 / HUD / 重置）");

        // ---- Page3..6 占位 ----
        for (int i = 3; i < n; i++)
            CreateTmpText($"P{i}Info", pages[i].transform, font, new Vector2(0.5f, 0.55f), new Vector2(900f, 200f), TextAlignmentOptions.Center,
                $"{tabNames[i]}（占位）\n功能待实现");

        pages[1].SetActive(false); pages[2].SetActive(false);
        for (int i = 3; i < n; i++) pages[i].SetActive(false);

        // ---- 组件 + 接线 ----
        var panel = menu.AddComponent<MainMenuPanel>();
        var so = new SerializedObject(panel);
        SetObjectArrayProp(so, "rootButtons", tabButtons);
        SetObjectArrayProp(so, "pages", pages);
        so.FindProperty("playerCountLabel").objectReferenceValue = pLabel;
        so.FindProperty("aiCountLabel").objectReferenceValue = aLabel;
        SetObjectArrayProp(so, "deviceRowObjs", devRows);
        SetObjectArrayProp(so, "devLabels", devLabels);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(panel);

        // 字体统一黑色
        foreach (var t in menu.GetComponentsInChildren<TextMeshProUGUI>(true))
            t.color = Color.black;

        Debug.Log($"[TrainingSceneWizard] MainMenuRoot: 已创建（{n} 个模块按钮）");
    }

    private static Button MakeButton(Transform parent, Button template, TMP_FontAsset font, string name, string text, Vector2 anchor, Vector2 size)
    {
        GameObject go;
        if (template != null)
        {
            go = Object.Instantiate(template.gameObject, parent);
            go.name = name;
            var so0 = new SerializedObject(go.GetComponent<Button>());
            var oc = so0.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (oc != null && oc.isArray) oc.arraySize = 0;
            so0.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.12f);
            go.GetComponent<Button>().targetGraphic = img;
        }

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;

        go.SetActive(true);
        var img0 = go.GetComponent<Image>();
        if (img0 != null) img0.raycastTarget = true;   // 保证鼠标可点
        var btn = go.GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.interactable = true;
        if (btn.targetGraphic != null) btn.targetGraphic.raycastTarget = true;

        var t = go.GetComponentInChildren<TextMeshProUGUI>(true);
        if (t == null) t = CreateTmpText("Label", go.transform, font, new Vector2(0.5f, 0.5f), size * 0.9f, TextAlignmentOptions.Center, text);
        else t.text = text;
        t.fontSize = 30;            // 字号调大
        t.raycastTarget = false;

        return btn;
    }

    private static void SetObjectArrayProp(SerializedObject so, string prop, Object[] values)
    {
        var p = so.FindProperty(prop);
        if (p == null) return;
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    // 全局底部按键提示条
    private static void BuildKeyHintBar(GameObject canvas, TMP_FontAsset font)
    {
        var old = canvas.transform.Find("KeyHintBarRoot");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var root = new GameObject("KeyHintBarRoot", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        root.GetComponent<Image>().color = new Color(0.78f, 0.78f, 0.78f, 0.98f);   // 灰色条
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(0f, 44f);
        rt.anchoredPosition = new Vector2(0f, 6f);

        var text = CreateTmpText("HintText", root.transform, font, new Vector2(0.5f, 0.5f), new Vector2(1800f, 40f), TextAlignmentOptions.Center, "");
        text.fontSize = 22;
        text.color = Color.black;   // 黑色字体
        var trt = text.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        var bar = root.AddComponent<KeyHintBar>();
        bar.normalColor = Color.black;
        var so = new SerializedObject(bar);
        so.FindProperty("hintText").objectReferenceValue = text;
        so.FindProperty("background").objectReferenceValue = root.GetComponent<Image>();
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(bar);

        Debug.Log("[TrainingSceneWizard] KeyHintBarRoot: 已创建");
    }

    // 深度查找子物体（含 inactive）—— GameObjects.Find 找不到 inactive 对象
    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static TextMeshProUGUI CreateTmpText(string name, Transform parent, TMP_FontAsset font, Vector2 anchor, Vector2 size, TextAlignmentOptions align, string text)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.font = font;
        tmp.fontSize = 24;
        tmp.color = Color.white;
        tmp.alignment = align;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;

        return tmp;
    }
}