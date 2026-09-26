using SuperSmashLike.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperSmashLike.UI
{
    /// 出招表条目 — 固定高卡片：图标 + 名称/指令 + 数据 + 描述
    public class MoveListItem : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Image moveIcon;           // 招式图标（未配置则隐藏）
        [SerializeField] private TextMeshProUGUI nameText; // 招式名 + 输入指令
        [SerializeField] private TextMeshProUGUI dataText; // 伤害/帧数
        [SerializeField] private TextMeshProUGUI descText; // 描述（空则隐藏）

        public void Setup(FighterData.MoveListEntry e)
        {
            if (moveIcon != null)
            {
                bool has = e.icon != null;
                moveIcon.gameObject.SetActive(has);
                if (has) moveIcon.sprite = e.icon;
            }

            if (nameText != null)
                nameText.text = $"<b>{e.name}　{e.input}</b>";

            if (dataText != null)
                dataText.text = $"伤害 {e.damage:0.#}　起手 {e.startupF}F　持续 {e.activeF}F　收招 {e.recoveryF}F";

            if (descText != null)
            {
                bool has = !string.IsNullOrEmpty(e.description);
                descText.gameObject.SetActive(has);
                if (has) descText.text = e.description;
            }
        }
    }
}
