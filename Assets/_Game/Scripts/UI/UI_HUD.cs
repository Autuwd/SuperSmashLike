using SuperSmashLike.Core;
using SuperSmashLike.Managers;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UI_HUD : MonoBehaviour
{
    public TextMeshProUGUI timerText;      // 倒计时文本（右上角）
    public TextMeshProUGUI p1DamageText;   // P1 伤害%（左下角）
    public TextMeshProUGUI p2DamageText;   // P2 伤害%（右下角）

    private MatchManager match;            // 场景里的 MatchManager

    // Start is called before the first frame update
    void Start()
    {
        // MatchManager 没有 Instance 单例 → 自动在场景里找（每场景只有一个）
        if (match == null) match = FindObjectOfType<MatchManager>();
    }

    // Update is called once per frame
    void Update()
    {
        // 倒计时（右上角）
        if (match != null && timerText != null)
            timerText.text = Mathf.CeilToInt(match.MatchTimeRemaining).ToString();

        // 伤害百分比（大乱斗规则：被打越多击飞越远）
        if (GameManager.Instance == null) return;
        var players = GameManager.Instance.ActivePlayers;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p.playerID == 0 && p1DamageText != null)
                p1DamageText.text = Mathf.CeilToInt(p.CurrentDamage) + "%";
            else if (p.playerID == 1 && p2DamageText != null)
                p2DamageText.text = Mathf.CeilToInt(p.CurrentDamage) + "%";
        }
    }
}
