using UnityEngine;
using TMPro;

public class StockDisplay : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private TextMeshProUGUI stockText;     // "×3"
    [SerializeField] private TextMeshProUGUI timerText;     // "02:30"
    [SerializeField] private RectTransform[] stockIcons;    // 可选：小图标组（每命一个）

    [Header("Config")]
    [SerializeField] private bool showTimer = false;
    [SerializeField] private Color warningColor = Color.red; // 最后 30s 变色
    [SerializeField] private float warningThreshold = 30f;

    private int _currentStock = 3;
    private float _remainingTime = 180f; // 3分钟
    private bool _timerRunning = false;

    public int FighterId { get; private set; }
    public void SetFighterId(int id) => FighterId = id;

    private void Update()
    {
        if (!_timerRunning) return;
        _remainingTime -= Time.deltaTime;
        if (_remainingTime <= 0f) { _remainingTime = 0f; _timerRunning = false; }
        UpdateTimerText();
    }

    public void Init(int startingStock, float matchDuration, bool showTimer = false)
    {
        _currentStock = startingStock;
        _remainingTime = matchDuration;
        _timerRunning = true;
        this.showTimer = showTimer; // 记录
        if (timerText != null) timerText.gameObject.SetActive(showTimer); // 显隐
        UpdateStockVisual();
        UpdateTimerText();
    }

    public void SetTimerRunning(bool running) => _timerRunning = running;

    public void AddTime(float seconds) { _remainingTime += seconds; UpdateTimerText(); }

    private void UpdateStockVisual()
    {
        stockText.text = $"×{_currentStock}";
        if (stockIcons != null)
        {
            for (int i = 0; i < stockIcons.Length; i++)
                stockIcons[i].gameObject.SetActive(i < _currentStock);
        }
    }

    //MatchManager 要求时同步剩余时间
    public void SyncTime(float serverRemaining)
    {
        _remainingTime = serverRemaining;
        if (showTimer) UpdateTimerText();
    }

    //更新 timerText 显示
    private void UpdateTimerText()
    {
        if (timerText == null || !showTimer) return; // 不显示则返回
        int minutes = Mathf.FloorToInt(_remainingTime / 60f);
        int seconds = Mathf.FloorToInt(_remainingTime % 60f);
        timerText.text = $"{minutes:00}:{seconds:00}";

        if (_remainingTime <= warningThreshold && _remainingTime > 0)
        {
            timerText.color = warningColor;
            timerText.fontSize = Mathf.Lerp(timerText.fontSize, timerText.fontSize * 1.2f, Time.deltaTime * 5f);
        }
        else
        {
            timerText.color = Color.white;
        }
    }

    public void SetStock(int stock)
    {
        _currentStock = Mathf.Max(0, stock);
        UpdateStockVisual();
    }

    // 重置/新回合重置
    public void ResetDisplay() { _timerRunning = false; timerText.color = Color.white; }
}