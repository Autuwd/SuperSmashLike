using UnityEngine;

// ============================================================
// GraphicsSettingsApplier — 画面设置的即时应用层（静态类）
// 职责：
//   1. 提供画面设置的候选表（分辨率 / 帧率 / 显示模式 / 画质）与对应显示文本
//   2. 把 GraphicsSettingsData 里的四个索引即时应用到 Screen / QualitySettings
// 架构位置：Core 层，被 SettingsStore.Load 与 SettingsPanel 共同调用
// 【重点】本类只做"应用"，不做"保存"——保存归 SettingsStore.NotifyGraphicsChanged，
//   职责分离后任何一处改动都不会引起另一处的意外副作用
// ============================================================
namespace SuperSmashLike.Core
{
    public static class GraphicsSettingsApplier
    {
        // ---------- 候选表（只读，顺序即索引） ----------

        // 可选分辨率表：与 ResolutionLabels 一一对应
        public static readonly Vector2Int[] Resolutions =
        {
            new Vector2Int(1920, 1080),
            new Vector2Int(1600, 900),
            new Vector2Int(1280, 720),
        };

        // 帧率上限表：-1 = 不限（Application.targetFrameRate = -1 即跟随系统）
        public static readonly int[] FpsLimits = { 60, 120, -1 };

        // ---------- 下拉框显示文本 ----------

        public static readonly string[] ResolutionLabels =
        {
            "1920 x 1080",
            "1600 x 900",
            "1280 x 720",
        };

        // 显示模式：0=独占全屏 1=无边框窗口（全屏窗口） 2=窗口
        public static readonly string[] FullScreenLabels = { "全屏", "无边框窗口", "窗口" };

        public static readonly string[] FpsLabels = { "60 FPS", "120 FPS", "不限" };

        // 画质档位：映射到 QualitySettings 现有档位的 低/中/高 三档
        public static readonly string[] QualityLabels = { "低", "中", "高" };

        // ---------- 核心 API ----------

        // 【做什么】把整份画面设置一次性应用到引擎（Load 后与面板改动时都会调）
        // 【重点】顺序：先画质，再分辨率，最后帧率——
        //   切画质可能触发渲染路径变化，先定画质可避免分辨率/帧率被引擎二次调整
        // 【行为】d 为 null 时静默返回；各索引越界由对应 Apply 方法内部钳制，绝不抛异常
        public static void ApplyAll(GraphicsSettingsData d)
        {
            if (d == null) return;

            ApplyQuality(d.qualityIndex);
            ApplyResolution(d.resolutionIndex);
            ApplyFullScreenMode(d.fullScreenMode);
            ApplyFpsLimit(d.fpsLimitIndex);
        }

        // 【做什么】按索引设置游戏分辨率，并保留当前显示模式
        // 【重点】必须把 Screen.fullScreenMode 传回 SetResolution——
        //   SetResolution 的第三参数若省略会退回默认模式，可能把玩家的无边框设置冲掉
        public static void ApplyResolution(int index)
        {
            int clamped = Mathf.Clamp(index, 0, Resolutions.Length - 1);
            Vector2Int res = Resolutions[clamped];

            try
            {
                Screen.SetResolution(res.x, res.y, Screen.fullScreenMode);
            }
            catch (System.Exception e)
            {
                // 编辑器 Play 模式对分辨率变更支持有限，失败不致命
                Debug.LogWarning($"[GraphicsSettingsApplier] 设置分辨率失败：{e.Message}");
            }
        }

        // 【做什么】按索引切换显示模式，切换后重设一次当前分辨率让模式真正生效
        // 【重点】部分平台仅设置 Screen.fullScreenMode 不会立刻重建窗口，
        //   紧跟一次 SetResolution(当前宽高, 新模式) 才能保证生效
        public static void ApplyFullScreenMode(int index)
        {
            int clamped = Mathf.Clamp(index, 0, FullScreenLabels.Length - 1);

            FullScreenMode mode = clamped switch
            {
                0 => FullScreenMode.ExclusiveFullScreen,   // 独占全屏（可降低输入延迟）
                1 => FullScreenMode.FullScreenWindow,      // 无边框窗口（Alt-Tab 友好）
                _ => FullScreenMode.Windowed,              // 窗口
            };

            try
            {
                Screen.fullScreenMode = mode;

                // 用当前窗口宽高 + 新模式再设一次，强制窗口按新模式重建
                Screen.SetResolution(Screen.width, Screen.height, mode);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GraphicsSettingsApplier] 设置显示模式失败：{e.Message}");
            }
        }

        // 【做什么】按索引设置帧率上限
        // 【重点】必须把 vSyncCount 归零——垂直同步开启时 targetFrameRate 会被忽略，
        //   两个上限机制只能留一个由我们控制，否则"120 FPS"选项实际仍是显示器刷新率
        public static void ApplyFpsLimit(int index)
        {
            int clamped = Mathf.Clamp(index, 0, FpsLimits.Length - 1);

            Application.targetFrameRate = FpsLimits[clamped];
            QualitySettings.vSyncCount = 0;
        }

        // 【做什么】按索引设置画质：把 低/中/高 三档映射到项目实际的 QualitySettings 档位
        // 【重点】QualitySettings.names 的档数随项目设置而变（可能 3 档也可能 6 档），
        //   所以用 取头 / 取中 / 取尾 的方式映射，而不是硬编码 0/1/2
        public static void ApplyQuality(int index)
        {
            string[] names = QualitySettings.names;

            // 守卫：空档位表（极端配置）直接跳过，SetQualityLevel(0) 会抛异常
            if (names == null || names.Length == 0) return;

            int clamped = Mathf.Clamp(index, 0, QualityLabels.Length - 1);

            int mapped = clamped switch
            {
                0 => 0,                        // 低 -> 最低档
                1 => (names.Length - 1) / 2,   // 中 -> 中间档（档数为偶数时偏下，符合省性能直觉）
                _ => names.Length - 1,         // 高 -> 最高档
            };

            QualitySettings.SetQualityLevel(mapped, true);
        }
    }
}
