using System.Text;
using SuperSmashLike.Core;
using TMPro;
using UnityEngine;

// ============================================================
// TrainingFrameDataPanel — 训练模式帧数据面板
// 职责：读取当前招式（FighterController.attackData）的启动/判定/收招时长，
//   按 fps 换算成帧数显示（供练习时观察招式帧结构）
// 数据来源：AttackData.startupTime / activeTime / recoveryTime（秒）
// 架构位置：Training 层（仅训练模式，挂在 TrainingHudRoot 下）
// ============================================================
namespace SuperSmashLike.Training
{
    public class TrainingFrameDataPanel : MonoBehaviour
    {
        [Tooltip("多行帧数据文本")]
        public TextMeshProUGUI frameText;

        [Tooltip("每秒帧数（时长 → 帧数 换算用）")]
        public float fps = 60f;

        private FighterController p1;
        private FighterController p2;
        private readonly StringBuilder _sb = new StringBuilder();

        // 【做什么】每次激活时重新绑定斗士（训练重进后旧实例已销毁）
        // 【为什么用 OnEnable 而不是 Start】Start 只跑一次，重进训练会留下死引用 → 面板恒显示 "-"
        private void OnEnable()
        {
            BindFighters();
        }

        private void OnDisable()
        {
            p1 = null;
            p2 = null;
        }

        private void BindFighters()
        {
            p1 = null;
            p2 = null;
            foreach (var f in FindObjectsOfType<FighterController>())
            {
                if (f.playerID == 0) p1 = f;
                if (f.playerID == 1) p2 = f;
            }
        }

        private void Update()
        {
            if (frameText == null) return;

            // 【自愈】引用失效（斗士被未经根激活切换的重建替换）→ 重新绑定
            if (p1 == null || p2 == null) BindFighters();

            _sb.Clear();
            AppendFighter("P1", p1);
            AppendFighter("P2", p2);
            frameText.text = _sb.ToString();
        }

        private void AppendFighter(string label, FighterController f)
        {
            if (f == null)
            {
                _sb.AppendLine($"{label}: -");
                return;
            }

            var atk = f.attackData;
            if (atk == null || !f.isAttacking)
            {
                _sb.AppendLine($"{label}: 待机   连段 {f.comboStep}   伤害 {f.CurrentDamage:F1}%");
                return;
            }

            int su = Mathf.RoundToInt(atk.startupTime * fps);
            int ac = Mathf.RoundToInt(atk.activeTime * fps);
            int rc = Mathf.RoundToInt(atk.recoveryTime * fps);
            int total = su + ac + rc;

            _sb.AppendLine(
                $"{label}: 启动 {su}F / 判定 {ac}F / 收招 {rc}F (共 {total}F)   " +
                $"连段 {f.comboStep}   伤害 {f.CurrentDamage:F1}%");
        }
    }
}
