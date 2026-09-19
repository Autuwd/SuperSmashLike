using System.Collections.Generic;
using UnityEngine;

// ============================================================
// InputBuffer — 帧号输入缓冲（纯 C# 类，非 MonoBehaviour）
// 职责：暂存玩家按键意图，供状态机在"允许消费的时机"读取
// 架构位置：Input 层，被 FighterController 持有（作为字段实例）
//
// 为什么用"帧号队列"而不是布尔标志？
//   1. 布尔只有"有没有按"；队列能记住"哪一帧按的" → 可判断过期
//   2. 帧号 = 帧同步联机的重放锚点（对照 FPS 架构的 inputHistory{TickInput}，
//      这就是本地版实现）
//
// 用法：
//   inputBuffer.BufferAttack();         // 按键时记一笔（无论当前能否执行）
//   if (inputBuffer.ConsumeAttack()) …  // 状态机窗口内消费
// ============================================================
namespace SuperSmashLike.InputSystem
{
    public class InputBuffer
    {
        #region 1. 运行时状态

        private readonly Queue<int> _attackFrames = new();  // 攻击意图的帧号队列
        private readonly int _windowFrames;                 // 有效窗口（帧）：超帧过期
        private readonly int _capacity;                     // 队列容量上限
        private int _lastAttackFrame = -1;                  // 同一帧去重

        #endregion

        #region 2. 构造

        // 【参数】windowFrames = 意图有效期（帧）；capacity = 队列容量上限
        public InputBuffer(int windowFrames = 5, int capacity = 10)
        {
            _windowFrames = windowFrames;
            _capacity = capacity;
        }

        #endregion

        #region 3. 公开 API

        // 【做什么】记录一次攻击意图（同一帧多次按键只记一次）
        public void BufferAttack()
        {
            int now = Time.frameCount;
            if (now == _lastAttackFrame) return;   // 同一帧去重

            _lastAttackFrame = now;
            _attackFrames.Enqueue(now);
            Trim();
        }

        // 【做什么】消费攻击意图：队首过期则丢弃，第一个窗口内的返回 true
        // 【返回】true = 消费成功（调用方可以执行攻击）
        // 【注意】队首必然是最早的输入，所以只要队首没过期，后面的都有效
        public bool ConsumeAttack()
        {
            while (_attackFrames.Count > 0)
            {
                int frame = _attackFrames.Dequeue();
                if (Time.frameCount - frame <= _windowFrames)
                    return true;    // 窗口内 → 消费成功
                // 过期输入 → 丢弃（队首最早，必最先过期）
            }
            return false;
        }

        // 【做什么】清空全部缓冲（起手攻击 / 受击打断时调用）
        public void Clear()
        {
            _attackFrames.Clear();
        }

        // 当前缓冲中的意图数量（调试/扩展用，暂无调用方）
        public int Count => _attackFrames.Count;

        #endregion

        #region 4. 私有逻辑

        // 【做什么】把队列裁到容量上限（丢最老的）
        private void Trim()
        {
            while (_attackFrames.Count > _capacity)
                _attackFrames.Dequeue();
        }

        #endregion
    }
}
