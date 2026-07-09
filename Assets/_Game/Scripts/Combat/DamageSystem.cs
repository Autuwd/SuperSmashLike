using UnityEngine;
using SuperSmashLike.Core;

// ============================================================
// DamageSystem — 伤害/击飞计算系统（静态工具类）
// 职责：
//   1. 实现大乱斗风格的击飞速度公式
//   2. 计算击飞方向（根据攻击角度）
//   3. 计算受击硬直时间
//   4. 计算护盾伤害
// 架构位置：Combat 层，纯计算无状态
// 调用关系：
//   FighterController.ApplyDamage() → DamageSystem 静态方法
// ============================================================
namespace SuperSmashLike.Combat
{
    public static class DamageSystem
    {
        // ================================================================
        // 击飞速度计算公式（基于大乱斗通用公式逆向工程）
        //
        // 核心逻辑：
        //   knockbackSpeed = (baseKB + bonusKB) × weightFactor × 1.4 + 18
        //   其中：
        //     baseKB   = 攻击的基础击飞值（AttackData.knockbackBase）
        //     bonusKB  = 伤害百分比相关的加成分
        //                = damage × 0.1 + damage × knockbackGrowth × 0.05
        //     weightFactor = 100 / targetWeight（越重越小，越难被击飞）
        //     growthFactor = knockbackGrowth / 100
        //
        // 公式特点：
        //   - 伤害越高，击飞越远（bonusKB 随 damage 线性增长）
        //   - 体重越大，击飞越近（weightFactor < 1）
        //   - 击飞成长率越高，高伤害时优势越明显
        // ================================================================
        public static float CalculateKnockbackVelocity(AttackData attack, float targetDamage, float targetWeight)
        {
            float dmg = Mathf.Max(0f, targetDamage);  // 伤害最低为0

            float baseKB = attack.knockbackBase;       // 基础击飞值
            float weightFactor = 100f / Mathf.Max(1f, targetWeight);  // 体重系数
            float growthFactor = attack.knockbackGrowth / 100f;       // 成长系数

            // 伤害加成部分（伤害越高击飞越远）
            float bonusKB = (dmg * 0.1f) + (dmg * attack.knockbackGrowth * 0.05f);

            // 最终速度
            float knockbackSpeed = (baseKB + bonusKB) * weightFactor * 1.4f + 18f;
            knockbackSpeed *= growthFactor;

            return Mathf.Max(0f, knockbackSpeed);
        }

        // ================================================================
        // 计算击飞方向向量
        // 根据攻击的 knockbackAngle 和命中方向计算实际击飞方向
        // 例如：
        //   knockbackAngle = 0°  → 水平击飞
        //   knockbackAngle = 90° → 垂直击飞
        //   knockbackAngle = 45° → 斜上方击飞（Sakurai 角度）
        // ================================================================
        public static Vector2 CalculateKnockbackDirection(AttackData attack, Vector2 hitDirection)
        {
            Vector2 dir = hitDirection.normalized;
            float angleRad = attack.knockbackAngle * Mathf.Deg2Rad;

            return new Vector2(
                dir.x * Mathf.Cos(angleRad),  // 水平分量 × cos(角度)
                Mathf.Sin(angleRad)            // 垂直分量 = sin(角度)（总向上）
            ).normalized;
        }

        // ================================================================
        // 计算受击硬直时间（Hitstun）
        // 击飞速度越快 → 硬直越长
        // 公式：硬直 = speed × 0.01 + 0.1（最少 0.05 秒）
        // ================================================================
        public static float CalculateHitstun(float knockbackSpeed)
        {
            return Mathf.Max(0.05f, knockbackSpeed * 0.01f + 0.1f);
        }

        // 计算护盾扣减伤害（不超过护盾当前值）
        public static float CalculateShieldDamage(AttackData attack, float shieldHP)
        {
            return Mathf.Min(shieldHP, attack.shieldDamage);
        }

        // 计算最终伤害值（含全局伤害比例缩放）
        // damageRatio 来自 GameSettings，用于调整整体游戏难度
        public static float CalculateFinalDamage(AttackData attack, float damageRatio)
        {
            return attack.damage * damageRatio;
        }
    }
}
