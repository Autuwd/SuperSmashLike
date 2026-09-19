using UnityEngine;

// ============================================================
// DebugSettings — 调试开关配置（ScriptableObject 资产）
// 职责：
//   1. 按【模块通道】保存各模块的日志开关，测试时想开哪块开哪块
//   2. 保存运行时面板的显示状态与呼出按键
// 架构位置：Data 层
// 使用方式：
//   右键 → Create → SuperSmashLike → Debug Settings  得到 DS_Debug.asset
//   拖到 GameManager.debugSettings 字段
// 被谁使用：SmashDebug（读开关） / DebugPanel（运行时改开关） / DebugPanelWindow（编辑器改开关）
//
// 【注意】本资产改的是"运行时内存值"，Play 结束会还原；
//         想持久化修改请用菜单 Tools/SuperSmashLike/Debug 开关面板。
// ============================================================
namespace SuperSmashLike.Core
{
    [CreateAssetMenu(menuName = "SuperSmashLike/Debug Settings", fileName = "DS_Debug")]
    public class DebugSettings : ScriptableObject
    {
        [Header("总开关")]
        [Tooltip("一键全开/全关。关掉后所有通道都不输出")]
        public bool master = true;

        [Tooltip("只看警告：勾上后普通 Log 被丢弃，只保留 LogWarn（用于过滤噪音）")]
        public bool onlyWarnings = false;

        [Header("模块通道")]
        [Tooltip("命中 / 伤害 / 击飞 / 护盾 / 盾反 / Hitstop")]
        public bool combat = false;

        [Tooltip("状态机切换与转换被拒")]
        public bool state = false;

        [Tooltip("输入初始化 / 输入决策（选了哪个招、判定窗）")]
        public bool input = false;

        [Tooltip("抓取 / 挣扎 / 投掷")]
        public bool grab = false;

        [Tooltip("移动 / 跳跃 / 落地 / 受身")]
        public bool movement = false;

        [Tooltip("比赛流程 / 倒计时 / 结算 / 重生")]
        public bool match = false;

        [Tooltip("HUD 刷新 / FrameMeter")]
        public bool ui = false;

        [Tooltip("相机跟随 / 震屏 / 闪白")]
        public bool camera = false;

        [Header("运行时面板")]
        [Tooltip("游戏内是否显示调试面板（可在运行时按 toggleKey 切换）")]
        public bool showOnScreenPanel = false;

        [Tooltip("呼出/收起运行时面板的按键")]
        public KeyCode toggleKey = KeyCode.F1;

        [Header("快捷预设")]
        [Tooltip("一键打开常用的战斗排障组合（Combat + State + Grab + Movement）")]
        public bool quickCombatDebug = false;

        // 【做什么】把 quickCombatDebug 的勾选状态同步到各通道
        // 【注意】只在 Inspector 值变化时由 OnValidate 调用，不参与运行时逻辑
        private void OnValidate()
        {
            if (quickCombatDebug)
            {
                master = true;
                combat = true;
                state = true;
                grab = true;
                movement = true;
            }
        }
    }
}
