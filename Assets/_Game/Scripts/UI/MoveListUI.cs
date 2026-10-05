using SuperSmashLike.Core;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SuperSmashLike.UI
{
    public class MoveListUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI playerText;
        [SerializeField] private Transform contentRoot;
        [SerializeField] private GameObject itemPrefab;
        [SerializeField] private GameObject emptyHint;
        [SerializeField] private ScrollRect scrollRect;

        [Header("Tabs")]
        [SerializeField] private Button tabJab;
        [SerializeField] private Button tabTilt;
        [SerializeField] private Button tabSmash;
        [SerializeField] private Button tabAerial;
        [SerializeField] private Button tabSpecial;
        [SerializeField] private Button tabGrab;

        private List<FighterData.MoveListEntry> _allEntries;
        private MoveCategory _currentTab;

        private void Start()
        {
            if (tabJab) tabJab.onClick.AddListener(() => SwitchTab(MoveCategory.Jab));
            if (tabTilt) tabTilt.onClick.AddListener(() => SwitchTab(MoveCategory.Tilt));
            if (tabSmash) tabSmash.onClick.AddListener(() => SwitchTab(MoveCategory.Smash));
            if (tabAerial) tabAerial.onClick.AddListener(() => SwitchTab(MoveCategory.Aerial));
            if (tabSpecial) tabSpecial.onClick.AddListener(() => SwitchTab(MoveCategory.Special));
            if (tabGrab) tabGrab.onClick.AddListener(() => SwitchTab(MoveCategory.Grab));
            SwitchTab(MoveCategory.Jab);
        }

        public void Show(FighterData data)
        {
            gameObject.SetActive(true);
            Populate(data, -1);
        }

        public void ShowFor(FighterData fd)
        {
            gameObject.SetActive(true);
            Populate(fd, -1);
        }

        public void ShowFor(FighterData fd, int playerId)
        {
            gameObject.SetActive(true);
            Populate(fd, playerId);
        }

        public void ShowCurrent()
        {
            if (GameManager.Instance == null) return;
            var gs = GameManager.Instance.gameSettings;
            if (gs == null) return;

            FighterData fd = gs.GetSelection(0);
            if (fd == null && GameManager.Instance.ActivePlayers.Count > 0)
                fd = GameManager.Instance.ActivePlayers[0].fighterData;

            Show(fd);
        }

        public void Populate(FighterData fd)
        {
            Populate(fd, -1);
        }

        public void Populate(FighterData fd, int playerId)
        {
            if (fd == null)
            {
                if (titleText) titleText.text = "";
                if (playerText) playerText.text = "";
                ShowEmpty();
                return;
            }

            if (titleText) titleText.text = fd.fighterName;
            if (playerText != null)
            {
                if (playerId >= 0 && playerId <= 3)
                    playerText.text = "玩家 " + (playerId + 1) + "（P" + (playerId + 1) + "）";
                else
                    playerText.text = "";
            }

            _allEntries = fd.GetMoveEntries();
            if (_allEntries == null || _allEntries.Count == 0)
            {
                ShowEmpty();
                return;
            }
            SwitchTab(_currentTab);
        }

        public void SwitchTab(MoveCategory cat)
        {
            _currentTab = cat;
            ClearContent();

            if (_allEntries == null) return;

            foreach (var e in _allEntries)
            {
                if (e.category != cat) continue;
                if (itemPrefab == null || contentRoot == null) continue;

                var go = Instantiate(itemPrefab, contentRoot);
                var item = go.GetComponent<MoveListItem>();
                if (item != null) item.Setup(e);
            }

            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;
        }

        public void Close() => gameObject.SetActive(false);

        private void ClearContent()
        {
            if (contentRoot == null) return;
            foreach (Transform child in contentRoot)
                Destroy(child.gameObject);
            if (emptyHint) emptyHint.SetActive(false);
        }

        private void ShowEmpty()
        {
            ClearContent();
            if (emptyHint) emptyHint.SetActive(true);
        }
    }
}
