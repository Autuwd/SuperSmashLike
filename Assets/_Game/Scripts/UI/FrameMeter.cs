using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SuperSmashLike.Combat;
using SuperSmashLike.Core;

// ============================================================
// FrameMeter — 帧数表（预设体驱动 v4）
// 职责：显示指定玩家的攻击三段（黄/红/蓝）+ 段末帧数 + 游标 + 信息文本
// 架构位置：UI 层
// 依赖：GameManager（按 playerID 找斗士）、DamageSystem（算硬直差）、FighterController（读攻击数据）
//
// 预设体驱动：UI 结构（60 格子 / 段末标签 / 游标 / Info）由预设体提供，不由代码创建。
//   生成方式：菜单 Tools → FrameMeter → Create Prefab
//   → Assets/_Game/Prefabs/UI/FrameMeter.prefab，之后可直接在 Inspector 调整表现。
//
// 预设体子物体命名规约（代码按名字 Find，改名会失效）：
//   Cells/Cell0..Cell59  - 60 个格子（Image）
//   Seg0..Seg2           - 段末数字标签（TextMeshProUGUI）
//   Cursor               - 白色游标竖线（Image）
//   Info                 - 信息文本（TextMeshProUGUI）
//
// 【注意】本类未声明命名空间（历史遗留），与其他脚本不一致。
// ============================================================
public class FrameMeter : MonoBehaviour
{
    #region 1. Inspector 配置

    [Header("Player")]
    public int playerIndex;              // 0 = P1, 1 = P2（按 playerID 匹配，不是列表下标）

    [Header("Frame Rate")]
    public int frameRate = 30;           // 与动画剪辑采样率一致

    [Header("Layout")]
    public int totalCells = 60;          // 格子数量（必须与预设体里的实际格子数一致）

    [Header("Colors")]
    public Color cStartup = new(1f, 0.85f, 0.2f);    // 起手帧（黄）
    public Color cActive = new(1f, 0.35f, 0.25f);    // 判定帧（红）
    public Color cRecovery = new(0.3f, 0.6f, 1f);    // 收招帧（蓝）
    public Color cEmpty = new(1f, 1f, 1f, 0.08f);    // 空白格

    #endregion

    #region 2. 运行时状态

    // ---- 预设体引用（Awake 里 Find 填充）----
    private Image[] cells;
    private RectTransform cursor;
    private TextMeshProUGUI infoText;
    private TextMeshProUGUI[] segLabel = new TextMeshProUGUI[3];

    // ---- 布局参数（Awake 按预设体实际尺寸推算）----
    private float startX;
    private float cellWidth;
    private float barHeight;

    // ---- 攻击计时（自记，不依赖 FighterController.attackTimer）----
    private bool wasAttacking;
    private float attackStartTime;
    private AttackData lastAtk;

    #endregion

    #region 3. Unity 生命周期

    // 【做什么】按命名规约从预设体取引用，并按实际尺寸重排格子
    // 【注意】任何子物体缺失都会把 cells 置 null，后续 Update 直接早退（不刷屏报错）
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

        // 布局参数：从预设体实际布局推算（与生成器保持一致）
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
            crt.sizeDelta = new Vector2(cellWidth - 1f, barHeight);
            crt.anchoredPosition = new Vector2(startX + i * cellWidth + cellWidth * 0.5f, 0f);
        }

        if (SmashDebug.IsOn(DebugChannel.UI))
            SmashDebug.Log(DebugChannel.UI,
                $"[FrameMeter P{playerIndex}] bound: cells={totalCells} cellW={cellWidth:F1} barH={barHeight:F1}");
    }

    // 【做什么】每帧找目标斗士并刷新显示
    private void LateUpdate()
    {
        if (cells == null) return;

        // 按 playerID 精确匹配，不用列表下标！
        // 原因：ActivePlayers 的填充顺序 = FighterController.Start() 的注册顺序，
        //   一旦场景层级顺序变化 / 重生重注册，下标就会错位 → P1 显示 P2 的帧数。
        //   playerID 是角色预制体上的固定值（Fighter_P1=0 / Fighter_P2=1），永远可靠。
        FighterController f = null;
        var players = GameManager.Instance != null ? GameManager.Instance.ActivePlayers : null;
        if (players != null)
        {
            foreach (var p in players)
            {
                if (p != null && p.playerID == playerIndex) { f = p; break; }
            }
        }

        UpdateMeter(f);
    }

    #endregion

    #region 4. 核心刷新逻辑

    // 【做什么】按当前攻击数据刷新格子颜色 / 段末帧数 / 游标 / 信息文本
    private void UpdateMeter(FighterController f)
    {
        bool inAttack = f != null
            && f.StateMachine.CurrentState == FighterState.Attack
            && f.attackData != null;

        // 攻击计时用"边缘检测 + 连段重新计时"自记，不依赖 FighterController 的字段
        if (!wasAttacking && inAttack) attackStartTime = Time.time;
        if (inAttack && f.attackData != lastAtk) attackStartTime = Time.time;   // 连段 → 重新计时
        lastAtk = inAttack ? f.attackData : null;
        wasAttacking = inAttack;

        // ===== 清空上一帧 =====
        for (int i = 0; i < totalCells; i++)
            cells[i].color = cEmpty;

        for (int s = 0; s < 3; s++)
        {
            segLabel[s].text = "";
            // 只藏 x（-1000 移出屏幕），y 保留预设体里摆好的位置，
            // 否则 PaintSeg 会读到被改乱的 y（历史踩坑）
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

            // 当前帧 =（当前时间 - 攻击开始时间）× 帧率
            int curFrame = Mathf.FloorToInt((Time.time - attackStartTime) * frameRate);
            curFrame = Mathf.Clamp(curFrame, 0, Mathf.Min(totalF, totalCells));

            // 三段涂色
            PaintSeg(0, 0, startF, cStartup, startF);
            PaintSeg(1, startF, actF, cActive, actF);
            PaintSeg(2, startF + actF, recF, cRecovery, recF);

            // 硬直差 = 命中硬直帧数 - 收招帧数（正数表示有利）
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

    // 【做什么】涂一段格子并把段末帧数标签摆到该段末尾
    // 【参数】segIdx = 段序号（0起手/1判定/2收招）；from = 起始格；len = 格数；color = 颜色；segFrames = 显示帧数
    private void PaintSeg(int segIdx, int from, int len, Color color, int segFrames)
    {
        int end = Mathf.Min(from + len, totalCells);
        for (int i = from; i < end; i++)
            cells[i].color = color;

        if (end <= from) return;

        segLabel[segIdx].text = $"{segFrames}";
        // x 由代码算（跟着格子走），y 完全用预设体里摆好的位置（可在 Inspector 拖）
        float x = startX + (end - 0.5f) * cellWidth;
        float y = segLabel[segIdx].rectTransform.anchoredPosition.y;
        segLabel[segIdx].rectTransform.anchoredPosition = new Vector2(x, y);

        if (SmashDebug.IsOn(DebugChannel.UI))
            SmashDebug.Log(DebugChannel.UI, $"Seg{segIdx} end={end} x={x:F1} 格子中心应={startX + (end - 0.5f) * cellWidth:F1}");
    }

    #endregion

    #region 5. 私有工具

    // 【做什么】秒 → 帧数（四舍五入）
    private int ToF(float seconds) => Mathf.RoundToInt(seconds * frameRate);

    #endregion
}
