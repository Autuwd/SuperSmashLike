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
        [SerializeField] private TextMeshProUGUI titleText;    // 角色名
        [SerializeField] private Transform contentRoot;        // ScrollView → Content
        [SerializeField] private GameObject itemPrefab;        // 条目模板
        [SerializeField] private GameObject emptyHint;         // "暂无招式数据"提示
        [SerializeField] private ScrollRect scrollRect;        // 切换 Tab 后滚动回顶（可选）

        [Header("Tabs")]
        [SerializeField] private Button tabJab;
        [SerializeField] private Button tabTilt;
        [SerializeField] private Button tabSmash;
        [SerializeField] private Button tabAerial;
        [SerializeField] private Button tabSpecial;
        [SerializeField] private Button tabGrab;

        private List<FighterData.MoveListEntry> _allEntries;   // Show 时缓存全量
        private MoveCategory _currentTab;                      // 当前 Tab

        private void Start()
        {
            tabJab.onClick.AddListener(() => SwitchTab(MoveCategory.Jab));
            tabTilt.onClick.AddListener(() => SwitchTab(MoveCategory.Tilt));
            tabSmash.onClick.AddListener(() => SwitchTab(MoveCategory.Smash));
            tabAerial.onClick.AddListener(() => SwitchTab(MoveCategory.Aerial));
            tabSpecial.onClick.AddListener(() => SwitchTab(MoveCategory.Special));
            tabGrab.onClick.AddListener(() => SwitchTab(MoveCategory.Grab));
            SwitchTab(MoveCategory.Jab);      // 默认轻击
        }

        public void Show(FighterData data)
        {
            gameObject.SetActive(true);
            if (data == null) { titleText.text = ""; ShowEmpty(); return; }

            titleText.text = data.fighterName;
            _allEntries = data.GetMoveEntries();
            if (_allEntries.Count == 0) { ShowEmpty(); return; }

            SwitchTab(_currentTab);           // 重开时恢复上次 Tab
        }

        public void SwitchTab(MoveCategory cat)
        {
            _currentTab = cat;
            ClearContent();

            // 只实例化当前分类的条目
            foreach (var e in _allEntries)
            {
                if (e.category != cat) continue;
                var go = Instantiate(itemPrefab, contentRoot);
                go.GetComponent<MoveListItem>().Setup(e);
            }

            // 滚动回顶（scrollRect 可选项，没拖引用就跳过）
            if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
        }

        public void Close() => gameObject.SetActive(false);

        private void ClearContent()
        {
            foreach (Transform child in contentRoot) Destroy(child.gameObject);
            emptyHint?.SetActive(false);
        }

        private void ShowEmpty()
        {
            ClearContent();
            if (emptyHint != null) emptyHint.SetActive(true);
        }
    }
}
