using UnityEngine;
using SuperSmashLike.Core;

// ============================================================
// DamageSystem — 伤害/击飞计算系统（静态工具类）
// 职责：
//   1. 实现大乱斗风格的击飞速度公式
//   2. 计算击飞方向（根据攻击角度）
//   3. 计算受击硬直时间
//   4. 计算护盾伤害
// 架构位置：Combat 层，纯计算无状态（全是 static 方法，不持有任何数据）
// 调用关系：FighterController.ApplyDamage() → DamageSystem 静态方法
//
// 【注意】本类是"纯函数"设计：给定输入必定得到相同输出，方便单独验证数值。
//   改任何公式前先确认是否影响所有角色（这是全局手感参数）。
// ============================================================
namespace SuperSmashLike.Combat
{
    public static class DamageSystem
    {
        #region 击飞计算

        // 【做什么】计算击飞速度标量（越大飞得越远）
        // 【参数】attack = 攻击数据；targetDamage = 目标当前伤害百分比；targetWeight = 目标体重
        // 【返回】击飞速度（已保证非负）
        //
        // 公式来源：大乱斗通用公式逆向工程
        //   knockbackSpeed = (baseKB + bonusKB) × weightFactor × 1.4 + 18
        //     baseKB       = attack.knockbackBase（攻击基础击飞值）
        //     bonusKB      = dmg×0.1 + dmg×knockbackGrowth×0.05（伤害加成，越高飞越远）
        //     weightFactor = 100 / targetWeight（体重越大越小，越难被击飞）
        //     成长率已折进 bonusKB（见下），不再单独做系数
        public static float CalculateKnockbackVelocity(AttackData attack, float targetDamage, float targetWeight)
        {
            float dmg = Mathf.Max(0f, targetDamage);                  // 伤害最低为 0
            float baseKB = attack.knockbackBase;                      // 基础击飞值
            float weightFactor = 100f / Mathf.Max(1f, targetWeight);  // 体重系数（防除零）

            float bonusKB = (dmg * 0.1f) + (dmg * attack.knockbackGrowth * 0.05f);

            float knockbackSpeed = (baseKB + bonusKB) * weightFactor * 1.4f + 18f;

            return Mathf.Max(0f, knockbackSpeed);
        }

        // 【做什么】计算击飞方向向量
        // 【参数】attack = 攻击数据（用其 knockbackAngle）；hitDirection = 命中方向（攻击者指向目标）
        // 【返回】归一化方向向量，垂直分量恒向上
        //
        // 角度语义：
        //   knockbackAngle = 0°  → 水平击飞
        //   knockbackAngle = 90° → 垂直击飞
        //   knockbackAngle = 45° → 斜上方击飞（Sakurai 角度）
        public static Vector2 CalculateKnockbackDirection(AttackData attack, Vector2 hitDirection)
        {
            Vector2 dir = hitDirection.normalized;
            float angleRad = attack.knockbackAngle * Mathf.Deg2Rad;

            return new Vector2(
                dir.x * Mathf.Cos(angleRad),  // 水平分量 × cos(角度)（保留左右方向）
                Mathf.Sin(angleRad)            // 垂直分量 = sin(角度)（总向上）
            ).normalized;
        }

        #endregion

        #region 硬直与护盾

        // 【做什么】按击飞速度换算受击硬直时长
        // 【返回】硬直秒数，范围 [0.15, 0.833]
        // 【注意】maxDur = 0.833s 是对齐 Knockback.anim 的时长（动画播完再转 FreeMove），
        //   换动画时必须同步这个值，否则会出现"硬直结束但动画还在播"或反之
        //
        // 【为什么是双段映射而不是单段 InverseLerp】
        //   数据缩放后（FD_* 的 knockbackBase ×0.28 / knockbackGrowth ×0.10），
        //   kbSpeed 的实际跨度是 19（轻击 0%）~ 163（重击 200%），约 8.6 倍。
        //   单段 InverseLerp(10, 70) 只有 6 倍宽，装不下 → 旧版实测 23 招里 19 招
        //   在 0% 伤害就撞上界，knockbackGrowth 在硬直层面完全失效。
        //   改成两段后：低伤害缓增（碰到谁都不易被控）、高伤害陡增（被击飞后难脱身），
        //   与 Smash 实战的手感分层一致。
        // 【调参依据】BreakSpeed 60 是"轻击系"（idx 30-34，22→72）与
        //   "重击系"（idx 20-22，36→163）的分界；MaxSpeed 165 覆盖 FD_Knight 200% 伤害的最大值。
        //   换角色/改数据后要重算这两值，否则又会退回"全员封顶"。
        public static float CalculateHitstun(float knockbackSpeed)
        {
            const float minDur = 0.15f;      // kb <= 10 → 0.15s（轻击兜底）
            const float midDur = 0.30f;      // kb = BreakSpeed → 0.30s（分段衔接点）
            const float maxDur = 0.833f;     // kb >= MaxSpeed → 0.833s（重击拉满）
            const float lowSpeed = 10f;      // 下界：与旧版保持一致
            const float breakSpeed = 60f;    // 断点
            const float maxSpeed = 165f;     // 上界

            if (knockbackSpeed <= breakSpeed)
            {
                // 第一段：低速区缓增，斜率约 (0.30-0.15)/(60-10) = 0.003 /s 每单位速度
                float tLow = Mathf.InverseLerp(lowSpeed, breakSpeed, knockbackSpeed);
                return Mathf.Lerp(minDur, midDur, tLow);
            }

            // 第二段：高速区陡增，斜率约 (0.833-0.30)/(165-60) = 0.005 /s 每单位速度
            float tHigh = Mathf.InverseLerp(breakSpeed, maxSpeed, knockbackSpeed);
            return Mathf.Lerp(midDur, maxDur, tHigh);
        }

        // 【做什么】计算护盾扣减量（不会把护盾打成负数）
        // 【参数】attack = 攻击数据（用其 shieldDamage）；shieldHP = 护盾当前耐久
        public static float CalculateShieldDamage(AttackData attack, float shieldHP)
        {
            return Mathf.Min(shieldHP, attack.shieldDamage);
        }

        // 【做什么】计算最终伤害值（含全局伤害比例缩放）
        // 【注意】当前无调用方 —— FighterController 里是手写内联的 `attack.damage * damageRatio`。
        //   保留作为统一入口备用；若确认不需要可直接删除。
        public static float CalculateFinalDamage(AttackData attack, float damageRatio)
        {
            return attack.damage * damageRatio;
        }

        #endregion
    }
}
