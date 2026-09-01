using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SuperSmashLike.Core;

namespace SuperSmashLike.UI
{
    /// <summary>
    /// 选人面板：展示所有可选手（读 GameSettings/FighterData 列表），点击选定后进入战斗。
    /// </summary>
    public class CharacterSelectPanel : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Transform rosterRoot;        // 角色卡列表容器（GridLayout）
        [SerializeField] private GameObject cardPrefab;       // 单张角色卡 Prefab（含头像/名字/Button）
        [SerializeField] private Button startButton;          // "开始战斗"按钮（可选：无则点卡即开打）

        private readonly List<GameObject> _cards = new();

        private void OnEnable()
        {
            BuildRoster();
        }

        private void OnDisable()
        {
            foreach (var c in _cards) if (c) Destroy(c);
            _cards.Clear();
        }

        private void BuildRoster()
        {
            // 从 GameSettings 读取可选手列表（若没有独立角色列表，也可手动从 Inspector 配置）
            GameSettings g = GameManager.Instance.gameSettings;
            var fighters = g.GetSelectableFighters(); // ★ 见下方 GameSettings 补充

            for (int i = 0; i < fighters.Count; i++)
            {
                FighterData fd = fighters[i];
                GameObject card = Instantiate(cardPrefab, rosterRoot);
                _cards.Add(card);

                // 填充卡面数据
                int captured = i; // 闭包捕获
                card.GetComponentInChildren<Image>().sprite = fd.portraitIcon;
                card.GetComponentInChildren<TextMeshProUGUI>().text = fd.fighterName;

                // 点击卡 → 为该玩家选定角色并进入战斗
                card.GetComponent<Button>().onClick.AddListener(() => OnCardSelected(fd, captured));
            }

            if (startButton) startButton.onClick.AddListener(() => GameManager.Instance.StartMatch());
        }

        private void OnCardSelected(FighterData fd, int playerIndex)
        {
            Debug.Log($"[CharacterSelect] P{playerIndex + 1} 选择: {fd.fighterName}");
            // ★ 实战点：把选择结果写入 GameSettings（`SelectedFighters` 列表），
            //   战斗场景 Spawn 时按此列表生成 Fighter prefab 并赋 fighterData。
            GameSettings s = GameManager.Instance.gameSettings;
            s.SetSelection(playerIndex, fd); // ★ 见下方 GameSettings 补充

            GameManager.Instance.StartMatch();
        }
    }
}