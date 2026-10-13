using UnityEngine;
using TMPro;
using UnityEngine.UI;

// ============================================================
// DamageDisplay — 玩家信息块（菱形：角色头像 + 击飞百分比 + 命数）
// 职责：
//   1. 击飞百分比数字**常显**（不再攻击时才淡入淡出）
//   2. 菱形中央显示该玩家的角色头像
// 架构位置：UI 层，由 MatchManager 按 playerID 索引调用
// 调用方：MatchManager.OnFighterDamagedHandler → SetPercent()
//        MatchManager.RespawnAfterDelay      → SetPercent(0) + ResetDisplay()
//        MatchManager.BindPortraits          → SetPortrait()
//
// 【注意】本类未声明命名空间（历史遗留），与其他脚本不一致。
// ============================================================
public class DamageDisplay : MonoBehaviour
{
    #region 1. Inspector 配置

    [Header("Refs")]
    [SerializeField] private TextMeshProUGUI percentText;   // "34%"
    [SerializeField] private RectTransform barFill;         // 旧进度条（菱形样式下隐藏，保留兼容）
    [SerializeField] private Image portrait;                // 菱形中央：角色头像
    [SerializeField] private CanvasGroup canvasGroup;       // 常显，alpha 恒为 1

    #endregion

    #region 2. 运行时状态

    private int _currentPercent;

    public int FighterId { get; private set; }   // 由 MatchManager 注入，UI 靠它认领数据

    #endregion

    #region 3. 公开 API

    // 【做什么】启用即常显（prefab 的 CanvasGroup 可能默认 alpha=0）
    private void OnEnable()
    {
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    // 【做什么】设置击飞百分比（常显）
    public void SetPercent(int percent, bool animate = true)
    {
        _currentPercent = Mathf.Clamp(percent, 0, 999);
        if (percentText != null) percentText.text = $"{_currentPercent}%";
        if (barFill != null) barFill.anchorMax = new Vector2(_currentPercent / 100f, 1f);
        if (canvasGroup != null) canvasGroup.alpha = 1f;   // 一直显示
    }

    // 【做什么】注入玩家 ID（由 MatchManager.SpawnAndBindHUD 调用）
    public void SetFighterId(int id) => FighterId = id;

    // 【做什么】设置头像（由 MatchManager 读取 FighterData.portraitIcon 后调用）
    public void SetPortrait(Sprite sprite)
    {
        if (portrait != null && sprite != null) portrait.sprite = sprite;
    }

    // 【做什么】重置显示（重生/新回合）：归零并保持可见
    public void ResetDisplay()
    {
        _currentPercent = 0;
        if (percentText != null) percentText.text = "0%";
        if (barFill != null) barFill.anchorMax = Vector2.zero;
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    #endregion
}
