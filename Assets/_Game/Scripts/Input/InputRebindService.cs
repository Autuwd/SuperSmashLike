using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SuperSmashLike.Core;

// ============================================================
// InputRebindService — 运行时按键重绑定服务（M8-D3）
// 职责：
//   1. 持有 master 输入资产克隆（与运行时资产隔离，专门用于重绑定与覆盖持久化）
//   2. 交互式重绑定（PerformInteractiveRebinding）+ 同方案冲突检测
//   3. 覆盖数据（overridePath）序列化到 SettingsStore，跨场景/跨重启保持
// 架构位置：Input 层
// 依赖：SettingsStore（唯一持久化入口）、UnityEngine.InputSystem
//
// 【重点】数据流：
//   SettingsStore.Data.controls.overridesJson（磁盘 JSON）
//        --ApplyTo--> 运行时资产（InputManager.runtimeActions 等）
//        <--SaveBindingOverridesAsJson-- master 克隆（重绑定只发生在 master 上）
//   重绑定成功 -> 写回 SettingsStore -> NotifyControlsChanged -> OnOverridesChanged
//        -> InputManager 重新 ApplyTo + 重启 Gameplay 地图（强制重新解析绑定）
//
// 【重点】为什么用独立 master 克隆：
//   InputManager 各自持有带 bindingMask 的运行时克隆，直接在它们上面重绑定
//   会受掩码/启用状态影响；master 不启用、不设掩码，是覆盖数据的唯一事实来源。
// ============================================================
namespace SuperSmashLike.InputSystem
{
    #region 重绑定行数据

    // 供 UI（SettingsPanel 按键页）渲染一行"动作 + 当前按键"
    public class RebindEntry
    {
        public string scheme;          // "KeyboardP1" / "KeyboardP2" / "Gamepad"
        public string action;          // 动作名（英文，如 "Jump"）
        public string displayName;     // 中文显示名（如 "跳跃"）
        public string bindingDisplay;  // 当前按键显示（如 "Space"，已省略设备前缀）
        public int bindingIndex;       // 该动作在本方案下的绑定下标
    }

    #endregion

    public static class InputRebindService
    {
        #region 1. 常量与静态状态

        public const string SchemeKeyboardP1 = "KeyboardP1";
        public const string SchemeKeyboardP2 = "KeyboardP2";
        public const string SchemeGamepad = "Gamepad";

        public static readonly string[] Schemes = { SchemeKeyboardP1, SchemeKeyboardP2, SchemeGamepad };

        // master 克隆：重绑定与覆盖序列化的唯一事实来源；null 表示尚未 Initialize
        private static InputActionAsset _master;
        public static bool Initialized => _master != null;

        // 覆盖数据变化事件（重绑定成功 / 恢复默认后触发）
        // InputManager 订阅后重新 ApplyTo 并重启 Gameplay 地图
        public static event Action OnOverridesChanged;

        // 动作名 -> 中文显示名（冲突提示与 UI 行标题共用）
        private static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
        {
            { "Jump",   "跳跃" },
            { "Attack", "攻击" },
            { "Special","必杀" },
            { "Shield", "防御" },
            { "Grab",   "抓取" },
            { "Taunt",  "嘲讽" },
            { "Pause",  "暂停" },
            { "Move",   "移动" },
        };

        #endregion

        #region 2. 初始化与覆盖应用

        // 【做什么】用源资产建 master 克隆，并应用已存覆盖
        // 【重点】幂等：_master 已存在则直接返回（首个调用者的克隆生效）；
        //         源资产为空只告警不抛出（外部引用可能尚未就绪）
        public static void Initialize(InputActionAsset source)
        {
            if (_master != null) return;

            if (source == null)
            {
                Debug.LogWarning("[InputRebindService] Initialize 收到空的源资产，跳过初始化。");
                return;
            }

            // JSON 克隆（与 InputManager.Awake 同一模式）：与原资产完全隔离
            _master = ScriptableObject.CreateInstance<InputActionAsset>();
            _master.LoadFromJson(source.ToJson());

            // 让 master 与磁盘状态一致（否则首次重绑会基于默认键做增量，丢掉历史覆盖）
            ApplyTo(_master);

            if (SmashDebug.IsOn(DebugChannel.Input))
                SmashDebug.Log(DebugChannel.Input, "[InputRebindService] master 资产已初始化并应用已存覆盖");
        }

        // 【做什么】把 SettingsStore 里持久化的覆盖 JSON 应用到任意运行时资产
        // 【重点】无需 Initialize 也能用（直接读 SettingsStore）；
        //         坏 JSON 只 LogWarning 不抛出 —— 覆盖数据损坏时游戏仍要能正常操作
        public static void ApplyTo(InputActionAsset runtimeAsset)
        {
            if (runtimeAsset == null) return;

            string json = SettingsStore.Data != null && SettingsStore.Data.controls != null
                ? SettingsStore.Data.controls.overridesJson
                : string.Empty;
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                // 【修正】契约写的 ApplyBindingOverridesAsJson 在 Input System 1.14.2 不存在，
                // 实际 API 为 LoadBindingOverridesFromJson（removeExisting 默认 true = 完全覆盖为 JSON 状态）
                runtimeAsset.LoadBindingOverridesFromJson(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[InputRebindService] 应用按键覆盖失败，已忽略: {e.Message}");
            }
        }

        // 【做什么】当前覆盖 JSON：已初始化则从 master 派生（始终反映最新结果），否则退回存储值
        public static string OverridesJson
        {
            get
            {
                if (_master != null) return _master.SaveBindingOverridesAsJson();
                return SettingsStore.Data != null && SettingsStore.Data.controls != null
                    ? SettingsStore.Data.controls.overridesJson
                    : string.Empty;
            }
        }

        // 【做什么】把 master 的覆盖写回 SettingsStore 并广播变更
        // 【重点】NotifyControlsChanged 负责落盘；OnOverridesChanged 负责通知运行时重新应用
        private static void SaveOverrides()
        {
            if (_master == null) return;

            string json = _master.SaveBindingOverridesAsJson();
            if (SettingsStore.Data != null && SettingsStore.Data.controls != null)
            {
                SettingsStore.Data.controls.overridesJson = json;
                SettingsStore.NotifyControlsChanged();
            }

            OnOverridesChanged?.Invoke();
        }

        #endregion

        #region 3. 绑定查询（GetEntries / HasConflict）

        // 【做什么】列出某方案下全部可重绑行（每个动作取该方案第一条单键绑定）
        // 【重点】Move 是 2DVector 复合方向键（含手柄摇杆绑定），不是"单键"，显式跳过 ——
        //         重绑定行只出 7 个按钮动作：跳跃/攻击/必杀/防御/抓取/嘲讽/暂停
        public static List<RebindEntry> GetEntries(string scheme)
        {
            var entries = new List<RebindEntry>();
            if (_master == null) return entries;   // 空安全：未初始化返回空表而不是抛异常

            var map = _master.FindActionMap("Gameplay");
            if (map == null) return entries;

            foreach (var action in map.actions)
            {
                if (action.name == "Move") continue;   // 复合方向键不可重绑

                int idx = FindSchemeBindingIndex(action, scheme);
                if (idx < 0) continue;                 // 该方案下无绑定 -> 跳过

                var binding = action.bindings[idx];
                entries.Add(new RebindEntry
                {
                    scheme = scheme,
                    action = action.name,
                    displayName = GetDisplayName(action.name),
                    bindingIndex = idx,
                    bindingDisplay = string.IsNullOrEmpty(binding.effectivePath)
                        ? string.Empty
                        : InputControlPath.ToHumanReadableString(
                            binding.effectivePath,
                            InputControlPath.HumanReadableStringOptions.OmitDevice)
                });
            }

            return entries;
        }

        // 【做什么】冲突检测：同方案下其它动作是否已占用 controlPath
        // 【重点】扫描包含复合子键（如 Move 的 W/A/S/D）在内的一切同组绑定，
        //         这样把跳跃改到 W 会被"移动"拦下；路径比较大小写不敏感
        public static bool HasConflict(string scheme, string actionName, string controlPath, out string conflictAction)
        {
            conflictAction = null;
            if (_master == null || string.IsNullOrEmpty(controlPath)) return false;

            var map = _master.FindActionMap("Gameplay");
            if (map == null) return false;

            foreach (var action in map.actions)
            {
                // 正在重绑的动作自己不算冲突
                if (string.Equals(action.name, actionName, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var b in action.bindings)
                {
                    if (!GroupsContain(b.groups, scheme)) continue;
                    if (string.Equals(b.effectivePath, controlPath, StringComparison.OrdinalIgnoreCase))
                    {
                        conflictAction = GetDisplayName(action.name);
                        return true;
                    }
                }
            }

            return false;
        }

        // 【做什么】找动作在某方案下的第一条可用绑定下标；找不到返回 -1
        // 【规则】groups 含目标方案，且既不是复合条目本身、也不是复合子键
        private static int FindSchemeBindingIndex(InputAction action, string scheme)
        {
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite || b.isPartOfComposite) continue;
                if (GroupsContain(b.groups, scheme)) return i;
            }
            return -1;
        }

        // 【做什么】groups 形如 ";KeyboardP1" / "KeyboardP1;Gamepad"，按分号精确匹配方案名
        private static bool GroupsContain(string groups, string scheme)
        {
            if (string.IsNullOrEmpty(groups) || string.IsNullOrEmpty(scheme)) return false;

            int start = 0;
            while (start <= groups.Length)
            {
                int sep = groups.IndexOf(';', start);
                int end = sep < 0 ? groups.Length : sep;
                string slice = groups.Substring(start, end - start).Trim();
                if (string.Equals(slice, scheme, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (sep < 0) break;
                start = sep + 1;
            }
            return false;
        }

        // 【做什么】动作名 -> 中文显示名（字典未收录时回退原名）
        private static string GetDisplayName(string actionName)
        {
            return !string.IsNullOrEmpty(actionName) && DisplayNames.TryGetValue(actionName, out string name)
                ? name
                : actionName;
        }

        #endregion

        #region 4. 交互式重绑定

        // 【做什么】发起一次交互式重绑定，完成后经 onDone(success, message) 回调
        // 【重点】RebindingOperation 必须在完成/取消后 Dispose，否则按键监听泄漏，
        //         连续多次重绑会不断累积事件订阅
        public static void BeginRebind(string scheme, string actionName, Action<bool, string> onDone)
        {
            // 空安全：未初始化 / 参数缺失 -> 以"未找到该绑定"回调而不是抛异常
            if (_master == null || string.IsNullOrEmpty(scheme) || string.IsNullOrEmpty(actionName))
            {
                onDone?.Invoke(false, "未找到该绑定");
                return;
            }

            var map = _master.FindActionMap("Gameplay");
            var action = map != null ? map.FindAction(actionName, false) : null;
            int bindingIndex = action != null ? FindSchemeBindingIndex(action, scheme) : -1;
            if (action == null || bindingIndex < 0)
            {
                onDone?.Invoke(false, "未找到该绑定");
                return;
            }

            action.PerformInteractiveRebinding(bindingIndex)
                // 只接受对应设备类型的控件（键盘方案含鼠标以外的 Keyboard，手柄只匹配 <Gamepad>）
                .WithControlsHavingToMatchPath(scheme == SchemeGamepad ? "<Gamepad>" : "<Keyboard>")
                .WithCancelingThrough("<Keyboard>/escape")
                .WithTimeout(5f)
                .OnComplete(op =>
                {
                    try
                    {
                        if (op.canceled)
                        {
                            onDone?.Invoke(false, "已取消");
                            return;
                        }

                        // 新路径：重绑成功后 Input System 已把结果写入 overridePath
                        var binding = op.action.bindings[bindingIndex];
                        string newPath = string.IsNullOrEmpty(binding.overridePath)
                            ? binding.effectivePath
                            : binding.overridePath;

                        // 冲突：撤销本次覆盖（保持原键），UI 显示占用者
                        if (HasConflict(scheme, actionName, newPath, out string conflictAction))
                        {
                            op.action.RemoveBindingOverride(bindingIndex);
                            onDone?.Invoke(false, "该按键已被「" + conflictAction + "」使用");
                            return;
                        }

                        // 成功：写回 SettingsStore + 广播（InputManager 重新 ApplyTo + 重启地图）
                        SaveOverrides();
                        if (SmashDebug.IsOn(DebugChannel.Input))
                            SmashDebug.Log(DebugChannel.Input,
                                $"[InputRebindService] 重绑定成功: {scheme}/{actionName} -> {newPath}");
                        onDone?.Invoke(true, null);
                    }
                    finally
                    {
                        op.Dispose();
                    }
                })
                .OnCancel(op =>
                {
                    try
                    {
                        onDone?.Invoke(false, "已取消");
                    }
                    finally
                    {
                        op.Dispose();
                    }
                })
                .Start();
        }

        #endregion

        #region 5. 恢复默认

        // 【做什么】恢复某方案下全部绑定为默认（移除该方案绑定的覆盖，其它方案不受影响）
        public static void ResetScheme(string scheme)
        {
            if (_master == null) return;   // 空安全：没有 master 就没有可恢复的覆盖

            var map = _master.FindActionMap("Gameplay");
            if (map == null) return;

            foreach (var action in map.actions)
            {
                int idx = FindSchemeBindingIndex(action, scheme);
                if (idx >= 0) action.RemoveBindingOverride(idx);
            }

            SaveOverrides();
        }

        // 【做什么】恢复全部绑定为默认（清 master 覆盖 + 清持久化 JSON）
        public static void ResetAll()
        {
            if (_master != null)
                _master.RemoveAllBindingOverrides();

            // 即使 master 未初始化也要清掉磁盘残留，保证"恢复默认"总是彻底
            if (SettingsStore.Data != null && SettingsStore.Data.controls != null)
                SettingsStore.Data.controls.overridesJson = string.Empty;

            SettingsStore.NotifyControlsChanged();
            OnOverridesChanged?.Invoke();
        }

        #endregion
    }
}
