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

        private int _highlightedByPlayer = -1; // -1=无，0-3=玩家ID
        private bool _isConfirmed = false;
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
            if (!_isConfirmed || highlightFrame == null || !highlightFrame.enabled)
                return;

            // 确认状态下缓慢闪烁（线框透明度）
            _flashT += Time.unscaledDeltaTime * flashSpeed;
            float a = (Mathf.Sin(_flashT * Mathf.PI * 2f) + 1f) * 0.25f + 0.25f; // 0.25 ~ 0.75

            if (_highlightedByPlayer >= 0 && _highlightedByPlayer < playerColors.Length)
            {
                Color baseCol = playerColors[_highlightedByPlayer];
                highlightFrame.color = new Color(baseCol.r, baseCol.g, baseCol.b, a);
            }
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

        // 设置高亮（未确认状态：实线边框，不闪烁）
        public void SetHighlighted(int playerId)
        {
            _highlightedByPlayer = playerId;
            _isConfirmed = false;
            _flashT = 0f;

            if (_rt)
                _rt.localScale = (playerId >= 0) ? _baseScale * highlightScale : _baseScale;

            if (highlightFrame != null)
            {
                if (playerId < 0)
                {
                    highlightFrame.enabled = false;
                    highlightFrame.color = new Color(1f, 1f, 1f, 0f);
                }
                else
                {
                    highlightFrame.enabled = true;
                    int pid = Mathf.Clamp(playerId, 0, 3);
                    Color col = playerColors[pid];
                    // 未确认：边框完全显现（线框风格）
                    highlightFrame.color = new Color(col.r, col.g, col.b, 1f);
                }
            }
        }

        // 设置确认状态（边框进入闪烁）
        public void SetConfirmed(int playerId, bool confirmed)
        {
            if (playerId < 0 || playerId > 3)
                playerId = 0;

            _highlightedByPlayer = playerId;
            _isConfirmed = confirmed;
            _flashT = 0f;

            if (_rt)
                _rt.localScale = confirmed ? _baseScale * highlightScale : _baseScale;

            if (highlightFrame != null)
            {
                highlightFrame.enabled = true;
                Color col = playerColors[_highlightedByPlayer];
                if (confirmed)
                {
                    // 确认初始半透明，Update 会接管闪烁
                    highlightFrame.color = new Color(col.r, col.g, col.b, 0.5f);
                }
                else
                {
                    highlightFrame.color = new Color(col.r, col.g, col.b, 1f);
                }
            }
        }

        // 清除所有高亮状态
        public void ClearState()
        {
            _highlightedByPlayer = -1;
            _isConfirmed = false;
            _flashT = 0f;

            if (_rt) _rt.localScale = _baseScale;
            if (highlightFrame != null)
            {
                highlightFrame.enabled = false;
                highlightFrame.color = new Color(1f, 1f, 1f, 0f);
            }
        }
    }
}
