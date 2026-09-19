using UnityEngine;
using TMPro;

// ============================================================
// StockDisplay — 命数 + 计时器显示（单玩家）
// 职责：
//   1. 显示剩余命数（"×3" 文本 + 可选图标组）
//   2. 显示比赛倒计时（最后 30 秒变红并放大）
// 架构位置：UI 层，由 MatchManager 按 playerID 索引调用
// 依赖：无（纯表现）
//
// 调用方：MatchManager.SpawnAndBindHUD        → Init()
//        MatchManager.OnTimerUpdatedHandler  → SyncTime()
//        MatchManager.OnFighterKilledHandler → SetStock()
//        MatchManager.RespawnAfterDelay      → SetStock()
//
// 【注意】本类未声明命名空间（历史遗留），与其他脚本不一致。
// ============================================================
public class StockDisplay : MonoBehaviour
{
    #region 1. Inspector 配置

    [Header("Refs")]
    [SerializeField] private TextMeshProUGUI stockText;     // "×3"
    [SerializeField] private TextMeshProUGUI timerText;     // "02:30"
    [SerializeField] private RectTransform[] stockIcons;    // 可选：小图标组（每命一个）

    [Header("Config")]
    [SerializeField] private bool showTimer;                // 是否显示计时器（由 Init 覆盖）
    [SerializeField] private Color warningColor = Color.red;// 最后 warningThreshold 秒的颜色
    [SerializeField] private float warningThreshold = 30f;

    #endregion

    #region 2. 运行时状态

    private int _currentStock = 3;
    private float _remainingTime = 180f;
    private bool _timerRunning;

    public int FighterId { get; private set; }   // 由 MatchManager 注入

    #endregion

    #region 3. Unity 生命周期

    // 【做什么】本地倒计时（比赛时间由 MatchManager 权威计算，这里只是显示层自走）
    private void Update()
    {
        if (!_timerRunning) return;

        _remainingTime -= Time.deltaTime;
        if (_remainingTime <= 0f)
        {
            _remainingTime = 0f;
            _timerRunning = false;
        }
        UpdateTimerText();
    }

    #endregion

    #region 4. 公开 API

    // 【做什么】初始化（由 MatchManager 在生成 HUD 后调用）
    // 【参数】startingStock = 起始命数；matchDuration = 比赛总时长；showTimer = 是否显示计时器
    public void Init(int startingStock, float matchDuration, bool showTimer = false)
    {
        _currentStock = startingStock;
        _remainingTime = matchDuration;
        _timerRunning = true;
        this.showTimer = showTimer;

        if (timerText != null) timerText.gameObject.SetActive(showTimer);
        UpdateStockVisual();
        UpdateTimerText();
    }

    // 【做什么】注入玩家 ID（由 MatchManager 调用）
    public void SetFighterId(int id) => FighterId = id;

    // 【做什么】控制本地倒计时是否继续走（比赛结束时停表）
    public void SetTimerRunning(bool running) => _timerRunning = running;

    // 【做什么】加时（当前无调用方，保留供道具/规则扩展）
    public void AddTime(float seconds)
    {
        _remainingTime += seconds;
        UpdateTimerText();
    }

    // 【做什么】用 MatchManager 的权威时间覆盖本地时间
    public void SyncTime(float serverRemaining)
    {
        _remainingTime = serverRemaining;
        if (showTimer) UpdateTimerText();
    }

    // 【做什么】更新命数（扣命 / 重生后同步）
    public void SetStock(int stock)
    {
        _currentStock = Mathf.Max(0, stock);
        UpdateStockVisual();
    }

    // 【做什么】重置显示（重生/新回合）
    public void ResetDisplay()
    {
        _timerRunning = false;
        if (timerText != null) timerText.color = Color.white;
    }

    #endregion

    #region 5. 私有逻辑

    // 【做什么】刷新命数文本与图标显隐
    private void UpdateStockVisual()
    {
        if (stockText != null) stockText.text = $"×{_currentStock}";

        if (stockIcons == null) return;
        for (int i = 0; i < stockIcons.Length; i++)
            stockIcons[i].gameObject.SetActive(i < _currentStock);
    }

    // 【做什么】刷新计时器文本；进入警戒时间时变色
    // 【注意】fontSize 的 Lerp 写法有 bug：每帧用"当前值"当目标，会无限放大。
    //   当前 showTimer 多为 false 所以没暴露，启用计时器时需要修。
    private void UpdateTimerText()
    {
        if (timerText == null || !showTimer) return;

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

    #endregion
}
