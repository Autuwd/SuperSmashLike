using UnityEngine;

// ============================================================
// DebugPanel — 运行时调试开关面板（IMGUI）
// 职责：
//   1. 游戏内按 toggleKey（默认 F1）呼出/收起一个开关面板
//   2. 面板上可实时勾选各模块的日志开关，改完立即生效
// 架构位置：Core 层，挂在 GameManager 所在的 GameObject 上
// 依赖：DebugSettings（资产） / SmashDebug（门面）
//
// 【注意】刻意使用 IMGUI（OnGUI）而不是 UGUI：
//   调试面板不能依赖被调试的 UI 系统 —— 否则 UI 出问题时面板也一起挂掉。
// 【注意】OnGUI 不受 Time.timeScale 影响，暂停状态下依然可操作。
// ============================================================
namespace SuperSmashLike.Core
{
    public class DebugPanel : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("留空则自动使用 SmashDebug.Settings（GameManager 注入的那份）")]
        [SerializeField] private DebugSettings settings;

        // 面板显示状态（运行时切换，不写回资产）
        private bool _visible;

        // 面板尺寸与位置
        private const float PanelWidth = 300f;
        private const float PanelX = 10f;
        private const float PanelY = 10f;

        // 懒回退：优先用 Inspector 指定的，其次用全局注入的
        private DebugSettings Settings => settings != null ? settings : SmashDebug.Settings;

        private void Start()
        {
            // 初始显示状态跟随资产配置
            if (Settings != null) _visible = Settings.showOnScreenPanel;
        }

        private void Update()
        {
            var s = Settings;
            if (s == null) return;

            // 呼出/收起（用旧版 Input，工程 Active Input Handling = Both）
            if (Input.GetKeyDown(s.toggleKey))
            {
                _visible = !_visible;
                s.showOnScreenPanel = _visible;
            }
        }

        private void OnGUI()
        {
            var s = Settings;
            if (s == null || !_visible) return;

            GUILayout.BeginArea(new Rect(PanelX, PanelY, PanelWidth, 330f), GUI.skin.box);

            // ===== 标题 =====
            GUILayout.Label($"<b>SmashDebug 面板</b>  ({s.toggleKey} 收起)", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Space(4f);

            // ===== 总开关 =====
            s.master = GUILayout.Toggle(s.master, "  总开关 (master)");
            s.onlyWarnings = GUILayout.Toggle(s.onlyWarnings, "  只看警告 (onlyWarnings)");
            GUILayout.Space(4f);

            // ===== 分通道开关（两列）=====
            GUILayout.Label("模块通道：");
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            s.combat   = GUILayout.Toggle(s.combat,   "  战斗 Combat");
            s.state    = GUILayout.Toggle(s.state,    "  状态 State");
            s.input    = GUILayout.Toggle(s.input,    "  输入 Input");
            s.grab     = GUILayout.Toggle(s.grab,     "  抓投 Grab");
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            s.movement = GUILayout.Toggle(s.movement, "  移动 Movement");
            s.match    = GUILayout.Toggle(s.match,    "  比赛 Match");
            s.ui       = GUILayout.Toggle(s.ui,       "  界面 UI");
            s.camera   = GUILayout.Toggle(s.camera,   "  相机 Camera");
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            // ===== 快捷预设 =====
            GUILayout.Label("快捷预设：");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("全开")) SetAll(s, true);
            if (GUILayout.Button("全关")) SetAll(s, false);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("战斗排障")) PresetCombat(s);
            if (GUILayout.Button("流程排障")) PresetFlow(s);
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label("<i>改动即时生效，Play 结束后还原资产值</i>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 10 });

            GUILayout.EndArea();
        }

        // 【做什么】一键把所有通道设为开或关
        private void SetAll(DebugSettings s, bool on)
        {
            s.master = on;
            s.combat = s.state = s.input = s.grab = on;
            s.movement = s.match = s.ui = s.camera = on;
        }

        // 【做什么】战斗排障预设：只看命中/状态/抓投/移动
        private void PresetCombat(DebugSettings s)
        {
            SetAll(s, false);
            s.master = true;
            s.combat = s.state = s.grab = s.movement = true;
        }

        // 【做什么】流程排障预设：只看比赛流程与 UI
        private void PresetFlow(DebugSettings s)
        {
            SetAll(s, false);
            s.master = true;
            s.match = s.ui = true;
        }
    }
}
