using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SuperSmashLike.Combat;
using SuperSmashLike.Core;

// ============================================================
// FrameMeter - 帧数表（M1-D2 预设体驱动 v4）
// 职责：显示指定玩家的攻击三段（黄/红/蓝）+ 段末帧数 + 游标 + 信息文本
//
// ★ 预设体驱动：UI 结构（60 格子/段末标签/游标/Info）由预设体提供，
//   不再由代码创建。生成方式：菜单 Tools → FrameMeter → Create Prefab
//   → Assets/_Game/Prefabs/UI/FrameMeter.prefab，然后可直接在 Inspector
//   调整每个元素的表现（颜色/字号/位置/大小）。
//
// 预设体子物体命名规约（代码按名字 Find，改不了）：
//   Cell0..Cell59  - 60 个格子（Image）
//   Seg0..Seg2     - 段末数字标签（TextMeshProUGUI，游标上方）
//   Cursor         - 白色游标竖线（Image）
//   Info           - 信息文本（TextMeshProUGUI，条上方）
//
// Inspector 可调参数：
//   playerIndex  - 对应 GameManager.ActivePlayers 的索引
//   totalCells   - 格子数量（须与预设体里的实际格子数一致！）
//   cStartup/cActive/cRecovery/cEmpty - 颜色
//   debugLog     - 打印每帧状态
// ============================================================
public class FrameMeter : MonoBehaviour
{
    [Header("Player")]
    public int playerIndex;              // 0 = P1, 1 = P2

    [Header("Layout")]
    public int totalCells = 60;

    [Header("Colors")]
    public Color cStartup = new(1f, 0.85f, 0.2f);    // 起手帧（黄）
    public Color cActive = new(1f, 0.35f, 0.25f);    // 红·判定
    public Color cRecovery = new(0.3f, 0.6f, 1f);    // 蓝色收招
    public Color cEmpty = new(1f, 1f, 1f, 0.08f);    // 空白格

    [Header("Debug")]
    public bool debugLog;                // 勾上 → Console 打印每帧状态

    // ---- 预设体引用（Awake 里 Find 填充）----
    private Image[] cells;
    private RectTransform cursor;
    private TextMeshProUGUI infoText;
    private TextMeshProUGUI[] segLabel = new TextMeshProUGUI[3];
    private float startX;
    private float cellWidth;
    private float barHeight;

    // ---- 攻击计时（自记，不依赖 attackTimer）----
    private bool wasAttacking;
    private float attackStartTime;

    // ==================== 构建 UI（预设体驱动）====================
    private void Awake()
    {
        if (GetComponent<RectTransform>() == null)
        {
            Debug.LogError("[FrameMeter] 需要 RectTransform，必须挂在 UI Canvas 下！", this);
            return;
        }

        // 从预设体按命名规约找引用（不再代码创建 UI）
        cells = new Image[totalCells];
        for (int i = 0; i < totalCells; i++)
        {
            var t = transform.Find($"Cells/Cell{i}");
            if (t == null)
            {
                Debug.LogError($"[FrameMeter P{playerIndex}] 找不到预设体子物体 Cell{i}！请用" +
                               $"菜单 Tools → FrameMeter → Create Prefab 生成预设体。", this);
                cells = null;
                return;
            }
            cells[i] = t.GetComponent<Image>();
        }

        cursor = transform.Find("Cursor")?.GetComponent<RectTransform>();
        infoText = transform.Find("Info")?.GetComponent<TextMeshProUGUI>();
        for (int s = 0; s < 3; s++)
            segLabel[s] = transform.Find($"Seg{s}")?.GetComponent<TextMeshProUGUI>();

        if (cursor == null || infoText == null || segLabel[0] == null || segLabel[1] == null || segLabel[2] == null)
        {
            Debug.LogError("[FrameMeter] 预设体结构不完整（需要 Cursor/Info/Seg0-2）。请重新生成预设体。", this);
            cells = null;
            return;
        }

        // 布局参数：从预设体实际布局推算（与生成器一致）
        var rt = GetComponent<RectTransform>();
        float myW = rt.rect.width;
        float myH = rt.rect.height;
        barHeight = myH * 0.55f;
        cellWidth = (myW - totalCells) / totalCells;
        if (cellWidth < 1f) cellWidth = 1f;
        startX = -totalCells * cellWidth / 2f;

        for (int i = 0; i < totalCells; i++)
        {
            var crt = cells[i].rectTransform;
            crt.sizeDelta = new Vector2(cellWidth - 1f, barHeight);              // 宽高跟着新参数走
            crt.anchoredPosition = new Vector2(
                startX + i * cellWidth + cellWidth * 0.5f, 0f);                  // 位置按新宽度重排
        }

        if (debugLog)
            Debug.Log($"[FrameMeter P{playerIndex}] bound: cells={totalCells} cellW={cellWidth:F1} barH={barHeight:F1}");
    }

    // ==================== 每帧更新 ====================
    private void LateUpdate()
    {
        if (cells == null) return;

        var players = GameManager.Instance != null ? GameManager.Instance.ActivePlayers : null;
        var f = players != null && playerIndex < players.Count ? players[playerIndex] : null;
        UpdateMeter(f);
    }

    private void UpdateMeter(FighterController f)
    {
        bool inAttack = f != null
            && f.StateMachine.CurrentState == FighterState.Attack
            && f.attackData != null;

        // 记录攻击开始时间（边缘检测）
        if (!wasAttacking && inAttack)
            attackStartTime = Time.time;
        wasAttacking = inAttack;

        if (debugLog && inAttack)
            Debug.Log($"[FrameMeter P{playerIndex}] inAttack=true atk={f.attackData.attackName}");

        // 清空
        for (int i = 0; i < totalCells; i++)
            cells[i].color = cEmpty;
        for (int s = 0; s < 3; s++)
        {
            segLabel[s].text = "";
            // 只藏 x（-1000 移出屏幕），y 保留预设体里摆好的位置，不然 PaintSeg 会读到被改乱的 y
            float keepY = segLabel[s].rectTransform.anchoredPosition.y;
            segLabel[s].rectTransform.anchoredPosition = new Vector2(-1000f, keepY);
        }

        float cursorX = -1000f;

        if (inAttack)
        {
            var atk = f.attackData;
            int startF = ToF(atk.startupTime);
            int actF = ToF(atk.activeTime);
            int recF = ToF(atk.recoveryTime);
            int totalF = startF + actF + recF;

            // 当前帧 = (当前时间 - 攻击开始时间) × 60
            int curFrame = Mathf.FloorToInt((Time.time - attackStartTime) * 60f);
            curFrame = Mathf.Clamp(curFrame, 0, Mathf.Min(totalF, totalCells));

            // 涂色
            PaintSeg(0, 0, startF, cStartup, startF);
            PaintSeg(1, startF, actF, cActive, actF);
            PaintSeg(2, startF + actF, recF, cRecovery, recF);

            // 硬直差
            float knockSpeed = DamageSystem.CalculateKnockbackVelocity(atk, f.CurrentDamage, f.fighterData.weight);
            int hitstunF = Mathf.RoundToInt(DamageSystem.CalculateHitstun(knockSpeed));
            int adv = hitstunF - recF;
            infoText.text = $"发动 {startF}F | 总计 {totalF}F | 帧数差 {(adv >= 0 ? "+" : "")}{adv}F";

            cursorX = startX + curFrame * cellWidth;
        }
        else
        {
            infoText.text = f != null ? "发动 -- | 总计 -- | 帧数差 0F" : "--- no player ---";
        }

        cursor.anchoredPosition = new Vector2(cursorX, 0f);
    }

    private void PaintSeg(int segIdx, int from, int len, Color color, int segFrames)
    {
        int end = Mathf.Min(from + len, totalCells);
        for (int i = from; i < end; i++)
            cells[i].color = color;

        if (end > from)
        {
            segLabel[segIdx].text = $"{segFrames}";
            // x 由代码算（跟着格子走），y 完全用预设体里摆好的位置（可在 Inspector 拖）
            float x = startX + (end - 0.5f) * cellWidth;
            float y = segLabel[segIdx].rectTransform.anchoredPosition.y;
            segLabel[segIdx].rectTransform.anchoredPosition = new Vector2(x, y);

            Debug.Log($"Seg{segIdx} end={end} x={x:F1} 格子中心应= {startX + (end - 0.5f) * cellWidth:F1}");
        }

    }

    private int ToF(float seconds) => Mathf.RoundToInt(seconds * 60f);
}