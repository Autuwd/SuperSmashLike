using UnityEngine;
using TMPro;
using System.Collections;

public class DamageDisplay : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private TextMeshProUGUI percentText;   // "0%"
    [SerializeField] private RectTransform barFill;         // Image (Filled) 对应 RectTransform
    [SerializeField] private CanvasGroup canvasGroup;       // 淡入淡出用

    [Header("Config")]
    [SerializeField] private float fadeInTime = 0.15f;
    [SerializeField] private float holdTime = 1.0f;
    [SerializeField] private float fadeOutTime = 0.5f;

    private Coroutine _fadeRoutine;
    private int _currentPercent = 0;

    public int FighterId { get; private set; }

    // 外部调用：DamageSystem 触发受伤时调用
    public void SetPercent(int percent, bool animate = true)
    {
        if (percentText == null || barFill == null) return; // 空引用保护
        _currentPercent = Mathf.Clamp(percent, 0, 999);
        percentText.text = $"{_currentPercent}%";

        // 进度条 = 百分比 * 最大宽 anchorMax.x = percent/100
        barFill.anchorMax = new Vector2(_currentPercent / 100f, 1f);

        if (animate) PlayFadeIn();
    }

    // MatchManager 调用
    public void SetFighterId(int id) => FighterId = id;

    private void PlayFadeIn()
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeRoutine());
    }

    private IEnumerator FadeRoutine()
    {
        canvasGroup.alpha = 0f;
        // Fade In
        float t = 0f;
        while (t < fadeInTime)
        {
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, t / fadeInTime);
            t += Time.unscaledDeltaTime; // 不受 timeScale 影响用 unscaled
            yield return null;
        }
        canvasGroup.alpha = 1f;

        // Hold
        yield return new WaitForSecondsRealtime(holdTime);

        // Fade Out
        t = 0f;
        while (t < fadeOutTime)
        {
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, t / fadeOutTime);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        canvasGroup.alpha = 0f;
    }

    // 重置/复活/新回合重置
    public void ResetDisplay()
    {
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (percentText != null) percentText.text = "0%";
        if (barFill != null) barFill.anchorMax = Vector2.zero;
    }
}