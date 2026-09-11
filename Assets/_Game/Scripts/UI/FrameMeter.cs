using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SuperSmashLike.Combat;
using SuperSmashLike.Core;

// ============================================================
// FrameMeter - 帧数表（M1-D2）
// 职责：显示指定玩家的攻击三段（黄/红/蓝）+ 段末帧数 + 游标 + 信息文本
//
// Inspector 可调参数（全部可手动调整）：
//   playerIndex  - 对应 GameManager.ActivePlayers 的索引
//   fontAsset    - TMP 字体资产（空 = 用 TMP Settings 默认字体）
//   fontSize     - 信息文本字号
//   segFontSize  - 段末帧数字号
//   textAboveBar - true 文字在条上方，false 在条下方
//   textOffsetY  - 文字相对条的额外偏移（微调用）
//   infoTemplate - 信息文本格式（{0}=起手帧 {1}=总帧 {2}=硬直差）
// ============================================================
public class FrameMeter : MonoBehaviour
{
    [Header("Player")]
    public int playerIndex;              // 0 = P1, 1 = P2

    [Header("Font")]
    public TMP_FontAsset fontAsset;      // 拖入中文字体资产；空 = TMP 默认字体
    [Range(8, 72)] public float fontSize = 20f;
    [Range(8, 48)] public float segFontSize = 14f;

    [Header("Layout")]
    public bool textAboveBar = true;     // true = 文字在条上方，false = 条下方
    [Range(-100, 100)] public float textOffsetY = 4f;   // 额外垂直偏移（微调用）
    public int totalCells = 60;

    [Header("Colors")]
    public Color cStartup = new(1f, 0.85f, 0.2f);    // 起手帧（黄）
    public Color cActive = new(1f, 0.35f, 0.25f);    // 红·判定
    public Color cRecovery = new(0.3f, 0.6f, 1f);    // 蓝色收招
    public Color cEmpty = new(1f, 1f, 1f, 0.08f);    // 空白格

    [Header("Debug")]
    public bool debugLog;                // 勾上 → Console 打印每帧状态

    // ---- 私有状态 ----
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

    // ==================== 构建 UI ====================
    private void Awake()
    {
        var rt = GetComponent<RectTransform>();
        if (rt == null)
        {
            Debug.LogError("[FrameMeter] 需要 RectTransform，必须挂在 UI Canvas 下！", this);
            return;
        }

        float myW = rt.rect.width;
        float myH = rt.rect.height;
        if (myW < 1f || myH < 1f)
        {
            Debug.LogError($"[FrameMeter] RectTransform 尺寸无效 ({myW}x{myH})，请在 Inspector 设 Width/Height。", this);
            return;
        }

        // --- 从 RectTransform 推算尺寸 ---
        barHeight = myH * 0.55f;
        cellWidth = (myW - totalCells) / totalCells;
        if (cellWidth < 1f) cellWidth = 1f;

        float barWidth = totalCells * cellWidth;
        startX = -barWidth / 2f;

        // 文字垂直位置
        float textY = textAboveBar
            ? (barHeight / 2f + textOffsetY)
            : -(barHeight / 2f + textOffsetY);

        Vector2 anchor = new Vector2(0.5f, 0.5f);

        // ---- 60 个格子 ----
        cells = new Image[totalCells];
        for (int i = 0; i < totalCells; i++)
        {
            var go = new GameObject($"Cell{i}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.color = cEmpty;
            img.raycastTarget = false;
            var crt = img.rectTransform;
            crt.anchorMin = crt.anchorMax = anchor;
            crt.sizeDelta = new Vector2(cellWidth - 1f, barHeight);
            crt.anchoredPosition = new Vector2(startX + i * cellWidth + cellWidth * 0.5f, 0f);
            cells[i] = img;
        }

        // ---- 3 个段末数字标签 ----
        for (int s = 0; s < 3; s++)
        {
            var go = new GameObject($"Seg{s}", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(transform, false);
            var txt = go.GetComponent<TextMeshProUGUI>();
            txt.fontSize = segFontSize;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;
            txt.raycastTarget = false;
            if (fontAsset != null) txt.font = fontAsset;
            var trt = txt.rectTransform;
            trt.anchorMin = trt.anchorMax = anchor;
            trt.sizeDelta = new Vector2(cellWidth * 4f, segFontSize * 1.5f);
            segLabel[s] = txt;
        }

        // ---- 游标（白色竖线）----
        var cg = new GameObject("Cursor", typeof(RectTransform), typeof(Image));
        cg.transform.SetParent(transform, false);
        var curImg = cg.GetComponent<Image>();
        curImg.color = Color.white;
        curImg.raycastTarget = false;
        cursor = curImg.rectTransform;
        cursor.anchorMin = cursor.anchorMax = anchor;
        cursor.sizeDelta = new Vector2(3f, barHeight + 6f);
        cursor.anchoredPosition = new Vector2(-1000f, 0f);

        // ---- 信息文本 ----
        var ig = new GameObject("Info", typeof(RectTransform), typeof(TextMeshProUGUI));
        ig.transform.SetParent(transform, false);
        infoText = ig.GetComponent<TextMeshProUGUI>();
        infoText.fontSize = fontSize;
        infoText.color = Color.white;
        infoText.raycastTarget = false;
        if (fontAsset != null) infoText.font = fontAsset;
        var irt = infoText.rectTransform;
        irt.anchorMin = irt.anchorMax = anchor;
        irt.sizeDelta = new Vector2(barWidth + 60f, fontSize * 2f);
        irt.anchoredPosition = new Vector2(0f, textY);

        if (debugLog)
            Debug.Log($"[FrameMeter P{playerIndex}] built: myW={myW} cellW={cellWidth} barH={barHeight} font={fontSize}");
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
            segLabel[s].rectTransform.anchoredPosition = new Vector2(-1000f, 0f);
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
            infoText.text = $"Startup {startF}F | Total {totalF}F | Adv {(adv >= 0 ? "+" : "")}{adv}F";

            cursorX = startX + curFrame * cellWidth;
        }
        else
        {
            infoText.text = f != null ? "Startup -- | Total -- | Adv 0F" : "--- no player ---";
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
            float x = startX + (end - 0.5f) * cellWidth;
            float textY = textAboveBar ? (barHeight / 2f + textOffsetY) : -(barHeight / 2f + textOffsetY);
            segLabel[segIdx].rectTransform.anchoredPosition = new Vector2(x, textY);
        }
    }

    private int ToF(float seconds) => Mathf.RoundToInt(seconds * 60f);
}