using SuperSmashLike.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperSmashLike.UI
{
    public class CardView : MonoBehaviour
    {
        [SerializeField] private Image portraitImage;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private Image highlightFrame; // 边框高亮（线框）
        [SerializeField] private float highlightScale = 1.08f;
        [SerializeField] private float flashSpeed = 2f; // 确认后闪烁速度

        [Header("Player Highlight Colors")]
        [SerializeField] private Color[] playerColors = new Color[]
        {
            Color.red,      // P1
            Color.blue,     // P2
            Color.yellow,   // P3
            Color.green     // P4
        };

        private FighterData _fd;
        public FighterData Data => _fd;

        private RectTransform _rt;
        private Vector3 _baseScale;

        // 多玩家标记（位掩码，bit0=P1 … bit3=P4）
        private int _highlightMask;   // 正在高亮（未确认）该卡牌的玩家
        private int _confirmMask;     // 已确认该卡牌的玩家
        private int _markMask;        // 所有标记该卡牌的玩家 = highlight | confirm
        private Color _blendColor = Color.white;
        private float _flashT;

        private void Awake()
        {
            _rt = GetComponent<RectTransform>();
            _baseScale = _rt.localScale;

            if (highlightFrame != null)
            {
                highlightFrame.enabled = false;
                var c = highlightFrame.color;
                highlightFrame.color = new Color(c.r, c.g, c.b, 0f);
            }
        }

        private void Update()
        {
            // 仅在"有玩家确认"时闪烁
            if (_confirmMask == 0 || highlightFrame == null || !highlightFrame.enabled)
                return;

            // 确认状态下缓慢闪烁（线框透明度）
            _flashT += Time.unscaledDeltaTime * flashSpeed;
            float a = (Mathf.Sin(_flashT * Mathf.PI * 2f) + 1f) * 0.25f + 0.25f; // 0.25 ~ 0.75
            highlightFrame.color = new Color(_blendColor.r, _blendColor.g, _blendColor.b, a);
        }

        public void SetData(FighterData fd, System.Action onSelect)
        {
            _fd = fd;
            if (portraitImage) portraitImage.sprite = fd.portraitIcon;
            if (nameText) nameText.text = fd.fighterName;

            var btn = GetComponent<Button>();
            if (btn)
            {
                btn.onClick.RemoveAllListeners();
                if (onSelect != null)
                    btn.onClick.AddListener(() => onSelect.Invoke());
            }
        }

        // 【核心】设置该卡牌的玩家标记状态（支持多玩家叠加）
        //   highlightMask：正在高亮（未确认）该卡牌的玩家位掩码
        //   confirmMask  ：已确认该卡牌的玩家位掩码
        // 【重点】多个玩家同时标记同一卡牌时，高亮色 = 各玩家颜色"叠加混合"，
        //         而不是单个玩家的颜色（修复 P2 标记被 P1 覆盖、始终不变的问题）
        public void SetState(int highlightMask, int confirmMask)
        {
            _highlightMask = highlightMask & 0xF;
            _confirmMask = confirmMask & 0xF;
            _markMask = (_highlightMask | _confirmMask) & 0xF;

            if (_markMask == 0)
            {
                ClearState();
                return;
            }

            _blendColor = BlendColors(_markMask);
            _flashT = 0f;

            if (_rt)
                _rt.localScale = _baseScale * highlightScale;

            if (highlightFrame != null)
            {
                highlightFrame.enabled = true;
                // 已确认 → 半透明起步，交给 Update 闪烁；未确认 → 实色边框
                float a = (_confirmMask != 0) ? 0.5f : 1f;
                highlightFrame.color = new Color(_blendColor.r, _blendColor.g, _blendColor.b, a);
            }
        }

        // 清除所有高亮状态
        public void ClearState()
        {
            _highlightMask = 0;
            _confirmMask = 0;
            _markMask = 0;
            _flashT = 0f;

            if (_rt) _rt.localScale = _baseScale;
            if (highlightFrame != null)
            {
                highlightFrame.enabled = false;
                highlightFrame.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        // 把 mask 中所有玩家的颜色相加（clamp 到 1）→ 多人同选时呈现叠加色
        // 例：红(P1)+蓝(P2)=品红；红(P1)+绿(P2 变体)=黄；红+蓝+绿=白
        private Color BlendColors(int mask)
        {
            float r = 0f, g = 0f, b = 0f;
            for (int p = 0; p < 4; p++)
            {
                if ((mask & (1 << p)) == 0) continue;
                Color c = (p < playerColors.Length) ? playerColors[p] : Color.white;
                r += c.r; g += c.g; b += c.b;
            }
            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
        }
    }
}
