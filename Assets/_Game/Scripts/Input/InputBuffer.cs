using System.Collections.Generic;
using UnityEngine;

// ============================================================
// InputBuffer — 帧号输入缓冲（2026-08-19）
// 职责：暂存玩家按键意图，供状态机在"允许消费的时机"读取。
// 为什么用"帧号队列"而不是布尔标志？
//   1. 布尔只有"有没有按"；队列能记住"哪一帧按的" → 可判断过期
//   2. 帧号 = 帧同步联机的重放锚点（今日正课理论落地！）
//      对照 FPS 架构：inputHistory 存 TickInput{tick,...}，这就是本地版
// 用法：
//   inputBuffer.BufferAttack();        // 按键时记一笔（无论能否执行）
//   if (inputBuffer.ConsumeAttack()) … // 状态机窗口内消费
// ============================================================
namespace SuperSmashLike.InputSystem
{
    public class InputBuffer
    {
        private readonly Queue<int> _attackFrames = new();  // 攻击意图的帧号队列
        private readonly int _windowFrames;                 // 有效窗口（帧）超帧过期
        private readonly int _capacity;                     // 队列容量
        private int _lastAttackFrame = -1;                  // 同一帧去重

        public InputBuffer(int windowFrames = 5, int capacity = 10)
        {
            _windowFrames = windowFrames;
            _capacity = capacity;
        }

        /// <summary>记录一次攻击意图（同一帧多次按键只记一次）</summary>
        public void BufferAttack()
        {
            int now = Time.frameCount;
            if (now == _lastAttackFrame) return;   // 同一帧去重
            _lastAttackFrame = now;
            _attackFrames.Enqueue(now);
            Trim();
        }

        /// <summary>消费攻击意图：队首过期则丢弃，第一个窗口内的返回 true</summary>
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

        /// <summary>清空全部缓冲（起手攻击/受击打断时调用）</summary>
        public void Clear()
        {
            _attackFrames.Clear();
        }

        public int Count => _attackFrames.Count;

        private void Trim()
        {
            while (_attackFrames.Count > _capacity)
                _attackFrames.Dequeue();
        }
    }
}