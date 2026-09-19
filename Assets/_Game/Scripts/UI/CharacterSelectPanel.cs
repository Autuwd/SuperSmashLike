using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SuperSmashLike.Core;

// ============================================================
// CharacterSelectPanel — 选人面板
// 职责：
//   1. 读 GameSettings.selectableFighters 生成角色卡列表
//   2. 点击角色卡 → 把选择写进 GameSettings（跨场景保持）→ 进入战斗
// 架构位置：UI 层
// 依赖：GameSettings（可选手列表 + 选择结果缓存）、GameManager（切状态）
//
// 【注意】选择结果存在 GameSettings 的私有数组里（SetSelection/GetSelection），
//   靠 ScriptableObject 资产实现跨场景保持 —— 战斗场景 Spawn 时按它取 fighterData。
// ============================================================
namespace SuperSmashLike.UI
{
    public class CharacterSelectPanel : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("UI References")]
        [SerializeField] private Transform rosterRoot;    // 角色卡列表容器（GridLayout）
        [SerializeField] private GameObject cardPrefab;   // 单张角色卡 Prefab（含头像/名字/Button）
        [SerializeField] private Button startButton;      // "开始战斗"按钮（可选：无则点卡即开打）

        #endregion

        #region 2. 运行时状态

        private readonly List<GameObject> _cards = new();

        #endregion

        #region 3. Unity 生命周期

        private void OnEnable()
        {
            BuildRoster();
        }

        // 【做什么】面板关闭时销毁生成的卡，避免下次启用重复堆叠
        private void OnDisable()
        {
            foreach (var c in _cards) if (c) Destroy(c);
            _cards.Clear();
        }

        #endregion

        #region 4. 私有逻辑

        // 【做什么】按 GameSettings 的可选手列表生成角色卡
        private void BuildRoster()
        {
            GameSettings g = GameManager.Instance.gameSettings;
            var fighters = g.GetSelectableFighters();

            for (int i = 0; i < fighters.Count; i++)
            {
                FighterData fd = fighters[i];
                GameObject card = Instantiate(cardPrefab, rosterRoot);
                _cards.Add(card);

                // 填充卡面数据
                int captured = i;   // 闭包捕获：必须存局部变量，否则所有卡都用同一个 i
                card.GetComponentInChildren<Image>().sprite = fd.portraitIcon;
                card.GetComponentInChildren<TextMeshProUGUI>().text = fd.fighterName;

                // 点击卡 → 为该玩家选定角色并进入战斗
                card.GetComponent<Button>().onClick.AddListener(() => OnCardSelected(fd, captured));
            }

            if (startButton) startButton.onClick.AddListener(() => GameManager.Instance.StartMatch());
        }

        // 【做什么】角色卡被点击：记录选择 → 直接开打
        // 【注意】playerIndex 决定"这是几号玩家的选择"，当前所有卡都写同一个下标（单人流程）
        private void OnCardSelected(FighterData fd, int playerIndex)
        {
            if (SmashDebug.IsOn(DebugChannel.UI))
                SmashDebug.Log(DebugChannel.UI, $"P{playerIndex + 1} 选择: {fd.fighterName}");

            // 把选择结果写入 GameSettings（跨场景保持），战斗场景 Spawn 时按此生成
            GameSettings s = GameManager.Instance.gameSettings;
            s.SetSelection(playerIndex, fd);

            GameManager.Instance.StartMatch();
        }

        #endregion
    }
}
