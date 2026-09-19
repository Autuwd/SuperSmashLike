using SuperSmashLike.Core;
using UnityEditor;
using UnityEngine;

// ============================================================
// DebugPanelWindow — 调试开关编辑器面板
// 职责：
//   1. 提供 Tools 菜单，在编辑期直接修改 DebugSettings 资产（持久化）
//   2. 提供一键创建 DebugSettings 资产
// 架构位置：Editor 层（不进包体）
// 使用方式：菜单 Tools → SuperSmashLike → Debug 开关面板
//
// 【注意】本窗口改的是【资产】，Play 结束不会还原；
//         游戏内 F1 面板改的是【运行时内存值】，Play 结束会还原。
// ============================================================
namespace SuperSmashLike.EditorTools
{
    public class DebugPanelWindow : EditorWindow
    {
        private DebugSettings _settings;

        [MenuItem("Tools/SuperSmashLike/Debug 开关面板")]
        public static void Open()
        {
            var win = GetWindow<DebugPanelWindow>("SmashDebug 开关");
            win.minSize = new Vector2(320f, 340f);
            win.TryAutoFindSettings();
        }

        [MenuItem("Tools/SuperSmashLike/创建 DebugSettings 资产")]
        public static void CreateAsset()
        {
            // 放在 ScriptableObjects 根目录，与 GameSettings 同级
            const string dir = "Assets/_Game/ScriptableObjects";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets/_Game", "ScriptableObjects");

            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/DS_Debug.asset");
            var asset = CreateInstance<DebugSettings>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(asset);
            Debug.Log($"[DebugPanelWindow] 已创建调试配置资产：{path}（请拖到 GameManager.debugSettings）");
        }

        // 【做什么】自动在工程里找一份 DS_Debug 资产
        private void TryAutoFindSettings()
        {
            if (_settings != null) return;
            var guids = AssetDatabase.FindAssets("t:DebugSettings");
            if (guids.Length > 0)
                _settings = AssetDatabase.LoadAssetAtPath<DebugSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4f);

            // ===== 资产选择 =====
            _settings = (DebugSettings)EditorGUILayout.ObjectField("DebugSettings 资产", _settings, typeof(DebugSettings), false);

            if (_settings == null)
            {
                EditorGUILayout.HelpBox("未找到 DebugSettings 资产。\n点下方按钮创建，然后拖到 GameManager.debugSettings 字段。", MessageType.Warning);
                if (GUILayout.Button("创建 DS_Debug.asset")) CreateAsset();
                return;
            }

            EditorGUILayout.Space(6f);

            // ===== 总开关 =====
            EditorGUILayout.LabelField("总开关", EditorStyles.boldLabel);
            _settings.master = EditorGUILayout.Toggle("  启用调试日志", _settings.master);
            _settings.onlyWarnings = EditorGUILayout.Toggle("  只看警告", _settings.onlyWarnings);

            EditorGUILayout.Space(6f);

            // ===== 分通道 =====
            EditorGUILayout.LabelField("模块通道", EditorStyles.boldLabel);
            _settings.combat   = EditorGUILayout.Toggle("  战斗 Combat（命中/伤害/击飞/盾）", _settings.combat);
            _settings.state    = EditorGUILayout.Toggle("  状态 State（状态机切换）", _settings.state);
            _settings.input    = EditorGUILayout.Toggle("  输入 Input（选招决策）", _settings.input);
            _settings.grab     = EditorGUILayout.Toggle("  抓投 Grab（抓取/挣扎/投掷）", _settings.grab);
            _settings.movement = EditorGUILayout.Toggle("  移动 Movement（跳/落/受身）", _settings.movement);
            _settings.match    = EditorGUILayout.Toggle("  比赛 Match（流程/倒计时/重生）", _settings.match);
            _settings.ui       = EditorGUILayout.Toggle("  界面 UI（HUD/FrameMeter）", _settings.ui);
            _settings.camera   = EditorGUILayout.Toggle("  相机 Camera（跟随/震屏）", _settings.camera);

            EditorGUILayout.Space(6f);

            // ===== 快捷预设 =====
            EditorGUILayout.LabelField("快捷预设", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全开")) SetAll(true);
            if (GUILayout.Button("全关")) SetAll(false);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("战斗排障")) PresetCombat();
            if (GUILayout.Button("流程排障")) PresetFlow();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);

            // ===== 运行时面板 =====
            EditorGUILayout.LabelField("运行时面板", EditorStyles.boldLabel);
            _settings.showOnScreenPanel = EditorGUILayout.Toggle("  启动时显示", _settings.showOnScreenPanel);
            _settings.toggleKey = (KeyCode)EditorGUILayout.EnumPopup("  呼出按键", _settings.toggleKey);

            EditorGUILayout.Space(8f);

            // ===== 保存 =====
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存资产"))
            {
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
                Debug.Log("[DebugPanelWindow] DebugSettings 已保存");
            }
            if (GUILayout.Button("定位资产"))
                EditorGUIUtility.PingObject(_settings);
            EditorGUILayout.EndHorizontal();

            // 值被改动就标脏（防止忘了点保存）
            if (GUI.changed) EditorUtility.SetDirty(_settings);
        }

        // 【做什么】一键把所有通道设为开或关
        private void SetAll(bool on)
        {
            _settings.master = on;
            _settings.combat = _settings.state = _settings.input = _settings.grab = on;
            _settings.movement = _settings.match = _settings.ui = _settings.camera = on;
            EditorUtility.SetDirty(_settings);
        }

        // 【做什么】战斗排障预设：命中/状态/抓投/移动
        private void PresetCombat()
        {
            SetAll(false);
            _settings.master = true;
            _settings.combat = _settings.state = _settings.grab = _settings.movement = true;
            EditorUtility.SetDirty(_settings);
        }

        // 【做什么】流程排障预设：比赛流程 + UI
        private void PresetFlow()
        {
            SetAll(false);
            _settings.master = true;
            _settings.match = _settings.ui = true;
            EditorUtility.SetDirty(_settings);
        }
    }
}
