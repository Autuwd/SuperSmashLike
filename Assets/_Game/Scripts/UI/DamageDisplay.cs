using UnityEngine;
using TMPro;
using System.Collections;

// ============================================================
// DamageDisplay — 伤害百分比显示（单玩家）
// 职责：
//   1. 显示伤害百分比数字 + 进度条
//   2. 受伤时播放"淡入 → 保持 → 淡出"的提示动画
// 架构位置：UI 层，由 MatchManager 按 playerID 索引调用
// 依赖：无（纯表现）
//
// 调用方：MatchManager.OnFighterDamagedHandler → SetPercent()
//        MatchManager.RespawnAfterDelay   → SetPercent(0) + ResetDisplay()
//
// 【注意】本类未声明命名空间（历史遗留），与其他脚本不一致。
// 【注意】所有时间相关计算都用 unscaledDeltaTime / WaitForSecondsRealtime，
//   因为 Hitstop 会把 timeScale 冻结，用普通时间动画会卡住。
// ============================================================
public class DamageDisplay : MonoBehaviour
{
    #region 1. Inspector 配置

    [Header("Refs")]
    [SerializeField] private TextMeshProUGUI percentText;   // "0%"
    [SerializeField] private RectTransform barFill;         // 进度条（Filled Image 的 RectTransform）
    [SerializeField] private CanvasGroup canvasGroup;       // 淡入淡出用

    [Header("Config")]
    [SerializeField] private float fadeInTime = 0.15f;      // 淡入时长
    [SerializeField] private float holdTime = 1.0f;         // 保持时长
    [SerializeField] private float fadeOutTime = 0.5f;      // 淡出时长

    #endregion

    #region 2. 运行时状态

    private Coroutine _fadeRoutine;
    private int _currentPercent;

    public int FighterId { get; private set; }   // 由 MatchManager 注入，UI 靠它认领数据

    #endregion

    #region 3. 公开 API

    // 【做什么】设置伤害百分比（由 MatchManager 调用）
    // 【参数】percent = 伤害值（自动夹到 0~999）；animate = 是否播放淡入动画
    // 【注意】barFill 用 anchorMax.x 当进度，不是 fillAmount
    public void SetPercent(int percent, bool animate = true)
    {
        if (percentText == null || barFill == null) return;   // 空引用保护

        _currentPercent = Mathf.Clamp(percent, 0, 999);
        percentText.text = $"{_currentPercent}%";

        // 进度条 = 百分比 → anchorMax.x = percent / 100
        barFill.anchorMax = new Vector2(_currentPercent / 100f, 1f);

        if (animate) PlayFadeIn();
    }

    // 【做什么】注入玩家 ID（由 MatchManager.SpawnAndBindHUD 调用）
    public void SetFighterId(int id) => FighterId = id;

    // 【做什么】重置显示（重生/新回合）：停动画 + 归零 + 隐藏
    public void ResetDisplay()
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (percentText != null) percentText.text = "0%";
        if (barFill != null) barFill.anchorMax = Vector2.zero;
    }

    #endregion

    #region 4. 私有逻辑

    // 【做什么】重启动画（先停旧的，防止连续受伤时动画叠加）
    private void PlayFadeIn()
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeRoutine());
    }

    // 【做什么】淡入 → 保持 → 淡出
    private IEnumerator FadeRoutine()
    {
        if (canvasGroup == null) yield break;

        // 淡入
        canvasGroup.alpha = 0f;
        float t = 0f;
        while (t < fadeInTime)
        {
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, t / fadeInTime);
            t += Time.unscaledDeltaTime;   // unscaled：Hitstop 时也能播完
            yield return null;
        }
        canvasGroup.alpha = 1f;

        // 保持
        yield return new WaitForSecondsRealtime(holdTime);

        // 淡出
        t = 0f;
        while (t < fadeOutTime)
        {
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, t / fadeOutTime);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        canvasGroup.alpha = 0f;
    }

    #endregion
}
