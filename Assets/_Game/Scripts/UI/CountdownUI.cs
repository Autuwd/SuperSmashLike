using TMPro;
using UnityEngine;

public class CountdownUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private float bigScale = 3.5f;     // 数字起始放大倍数
    [SerializeField] private float normalScale = 1f;

    private void Awake()
    {
        if (label == null) label = GetComponent<TMP_Text>();
        if (label == null) label = GetComponentInChildren<TMP_Text>(true);   // 文本在子物体也能找到
        if (label == null) Debug.LogError("[CountdownUI] 找不到 label（TMP_Text）");
    }

    // 【做什么】立即隐藏倒计时（离开对局 / 被中断时调用）
    public void Hide()
    {
        if (label == null) return;
        label.text = "";
        label.transform.localScale = Vector3.one;
        label.rectTransform.anchoredPosition = Vector2.zero;
        label.color = Color.white;
    }

    // 【做什么】播放 from→1 的数字 + FIGHT!
    public System.Collections.IEnumerator Play(int from, float stepTime, float fightHold)
    {
        for (int n = from; n >= 1; n--)
            yield return PlayNumber(n.ToString(), stepTime);

        yield return PlayFight(fightHold);

        label.text = "";
        label.transform.localScale = Vector3.one;
        label.rectTransform.anchoredPosition = Vector2.zero;
    }

    // 数字：由大到小收缩，进入瞬间抖动（衰减）
    private System.Collections.IEnumerator PlayNumber(string text, float dur)
    {
        label.text = text;
        label.color = Color.white;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);

            float scale = Mathf.Lerp(bigScale, normalScale, Mathf.SmoothStep(0f, 1f, k)); // 大→小
            float shake = (1f - k) * 0.12f;                                                // 抖动衰减
            Vector2 off = new Vector2(Mathf.Sin(Time.time * 55f), Mathf.Cos(Time.time * 63f)) * shake;

            label.transform.localScale = Vector3.one * scale;
            label.rectTransform.anchoredPosition = off;
            yield return null;
        }
    }

    // FIGHT!：小→大→回落 pop + 强震动（打击感）
    private System.Collections.IEnumerator PlayFight(float dur)
    {
        label.text = "FIGHT!";
        label.color = new Color(1f, 0.85f, 0.2f);   // 亮黄
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);

            float pop = 1f + 0.5f * Mathf.Sin(k * Mathf.PI);   // 冲到 1.5 再回落
            float shake = (1f - k) * 0.3f;                     // 比数字更猛
            Vector2 off = new Vector2(Mathf.Sin(Time.time * 70f), Mathf.Cos(Time.time * 80f)) * shake;

            label.transform.localScale = Vector3.one * pop;
            label.rectTransform.anchoredPosition = off;
            yield return null;
        }
        label.color = Color.white;
    }
}