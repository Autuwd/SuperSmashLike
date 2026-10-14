using SuperSmashLike.Core;
using TMPro;
using UnityEngine;

namespace SuperSmashLike.Training
{
    public class TrainingHud : MonoBehaviour
    {
        [Header("UI")]
        public TextMeshProUGUI p1DamageText;
        public TextMeshProUGUI p2DamageText;
        public TextMeshProUGUI comboText;
        public TextMeshProUGUI hintText;

        private FighterController p1;
        private FighterController p2;

        [Header("Damage Popup")]
        [Tooltip("伤害飘字颜色")]
        public Color popupColor = new Color(1f, 0.85f, 0.2f, 1f);

        private Canvas _canvas;
        private TMP_FontAsset _popupFont;

        private void Start()
        {
            var fighters = FindObjectsOfType<FighterController>();
            foreach (var f in fighters)
            {
                if (f.playerID == 0) p1 = f;
                if (f.playerID == 1) p2 = f;
            }

            if (p1 != null)
                p1.OnDamaged += OnP1Damaged;
            if (p2 != null)
                p2.OnDamaged += OnP2Damaged;

            _canvas = GetComponentInParent<Canvas>();
            _popupFont = hintText != null ? hintText.font : null;

            if (comboText != null)
                comboText.text = "Combo: 0";
            if (hintText != null)
                hintText.text = "Esc 菜单   R 重置   T CPU   I 无敌   H HUD（帧数据/慢动作见菜单）";
        }

        private void OnDestroy()
        {
            if (p1 != null)
                p1.OnDamaged -= OnP1Damaged;
            if (p2 != null)
                p2.OnDamaged -= OnP2Damaged;
        }

        private void OnP1Damaged(float dmg, FighterController attacker)
        {
            if (p1 != null && p1DamageText != null)
                p1DamageText.text = $"P1: {p1.CurrentDamage:F1}%";
            SpawnPopup(p1, dmg, attacker);
            UpdateCombo();
        }

        private void OnP2Damaged(float dmg, FighterController attacker)
        {
            if (p2 != null && p2DamageText != null)
                p2DamageText.text = $"P2: {p2.CurrentDamage:F1}%";
            SpawnPopup(p2, dmg, attacker);
            UpdateCombo();
        }

        // 【做什么】在受击者头顶生成伤害飘字
        // 【注意】仅真实命中才飘字：attacker 非空（排除 ResetToSpawn 时 attacker=null 的自伤广播）
        private void SpawnPopup(FighterController victim, float dmg, FighterController attacker)
        {
            if (attacker == null || dmg <= 0f) return;
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null) return;
            DamagePopup.Spawn(_canvas, victim, dmg, _popupFont, popupColor);
        }

        private void UpdateCombo()
        {
            if (comboText == null) return;
            int c = 0;
            if (p1 != null) c = Mathf.Max(c, p1.comboStep);
            if (p2 != null) c = Mathf.Max(c, p2.comboStep);
            comboText.text = $"Combo: {c}";
        }

        private void Update()
        {
            if (p1 != null && p1DamageText != null)
                p1DamageText.text = $"P1: {p1.CurrentDamage:F1}%";
            if (p2 != null && p2DamageText != null)
                p2DamageText.text = $"P2: {p2.CurrentDamage:F1}%";
            UpdateCombo();
        }
    }
}