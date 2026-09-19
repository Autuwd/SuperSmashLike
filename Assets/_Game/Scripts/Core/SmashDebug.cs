using UnityEngine;

// ============================================================
// SmashDebug — 调试日志统一门面（静态类）
// 职责：
//   1. 提供按【模块通道】分组的日志接口，替代裸 Debug.Log
//   2. 通过 DebugSettings 资产统一控制开关，关闭时零成本
//   3. 统一日志前缀格式，方便 Console 过滤
// 架构位置：Core 层，被所有模块调用
// 依赖：DebugSettings（ScriptableObject，由 GameManager 在 Awake 注入）
// 被谁使用：FighterController / MatchManager / InputManager / Hitbox / 各 UI 等
//
// 【重点】正确用法 —— 必须把判断放在 if 里，让字符串插值不执行：
//     if (SmashDebug.IsOn(DebugChannel.Combat))
//         SmashDebug.Log(DebugChannel.Combat, $"伤害={dmg}");
//
// 【重点】错误用法 —— 插值先算完才发现不用打，热路径持续产生 GC：
//     SmashDebug.Log(DebugChannel.Combat, $"伤害={dmg}");
//
// 【注意】LogError 不受开关控制（错误必须永远可见），
//         需要"永远可见的错误"请直接用 Debug.LogError，不要走本类。
// ============================================================
namespace SuperSmashLike.Core
{
    // 调试日志通道（按模块划分，不要按文件划分，否则开关太多）
    public enum DebugChannel
    {
        Combat,    // 命中 / 伤害 / 击飞 / 护盾 / 盾反 / Hitstop
        State,     // 状态机切换与转换被拒
        Input,     // 输入初始化 / 输入决策（选了哪个招）
        Grab,      // 抓取 / 挣扎 / 投掷
        Movement,  // 移动 / 跳跃 / 落地 / 受身
        Match,     // 比赛流程 / 倒计时 / 结算 / 重生
        UI,        // HUD 刷新 / FrameMeter
        Camera,    // 相机跟随 / 震屏 / 闪白
    }

    public static class SmashDebug
    {
        // 全局唯一的配置资产引用，由 GameManager.Awake 注入
        // 为 null 时所有通道视为关闭（保证未初始化时不刷屏）
        public static DebugSettings Settings;

        // 【做什么】判断某个通道当前是否开启
        // 【注意】Settings 为空时返回 false —— 未初始化就不打印，避免污染 Console
        public static bool IsOn(DebugChannel channel)
        {
            if (Settings == null || !Settings.master) return false;

            return channel switch
            {
                DebugChannel.Combat   => Settings.combat,
                DebugChannel.State    => Settings.state,
                DebugChannel.Input    => Settings.input,
                DebugChannel.Grab     => Settings.grab,
                DebugChannel.Movement => Settings.movement,
                DebugChannel.Match    => Settings.match,
                DebugChannel.UI       => Settings.ui,
                DebugChannel.Camera   => Settings.camera,
                _ => false,
            };
        }

        // 【做什么】输出普通日志（受开关控制）
        // 【注意】调用方必须先用 IsOn 包住，否则字符串插值仍会执行
        public static void Log(DebugChannel channel, string message)
        {
            if (!IsOn(channel)) return;
            if (Settings.onlyWarnings) return;   // "只看警告"模式下丢弃普通日志
            Debug.Log($"[{channel}] {message}");
        }

        // 【做什么】输出警告日志（受开关控制，不受 onlyWarnings 影响）
        public static void LogWarn(DebugChannel channel, string message)
        {
            if (!IsOn(channel)) return;
            Debug.LogWarning($"[{channel}] {message}");
        }
    }
}
