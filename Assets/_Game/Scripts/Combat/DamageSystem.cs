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
        //     growthFactor = knockbackGrowth / 100（成长率越高，高伤害时优势越明显）
        public static float CalculateKnockbackVelocity(AttackData attack, float targetDamage, float targetWeight)
        {
            float dmg = Mathf.Max(0f, targetDamage);                  // 伤害最低为 0
            float baseKB = attack.knockbackBase;                      // 基础击飞值
            float weightFactor = 100f / Mathf.Max(1f, targetWeight);  // 体重系数（防除零）
            float growthFactor = attack.knockbackGrowth / 100f;       // 成长系数

            float bonusKB = (dmg * 0.1f) + (dmg * attack.knockbackGrowth * 0.05f);

            float knockbackSpeed = (baseKB + bonusKB) * weightFactor * 1.4f + 18f;
            knockbackSpeed *= growthFactor;

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
        public static float CalculateHitstun(float knockbackSpeed)
        {
            float minDur = 0.15f;      // 速度 10  → 0.15s（轻击）
            float maxDur = 0.833f;     // 速度 70+ → 0.833s（重击拉满）
            float t = Mathf.InverseLerp(10f, 70f, knockbackSpeed);
            return Mathf.Lerp(minDur, maxDur, t);
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
