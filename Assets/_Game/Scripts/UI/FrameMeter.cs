using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SuperSmashLike.Combat;
using SuperSmashLike.Core;

public class FrameMeter : MonoBehaviour
{
    [Header("Config")]
    public int totalCells = 60;
    public float cellWidth = 6f;
    public float barHeight = 16f;
    public Color cStartup = new(1f, 0.85f, 0.2f);    // 起手帧（黄）
    public Color cActive = new(1f, 0.35f, 0.25f);    // 红·判定
    public Color cRecovery = new(0.3f, 0.6f, 1f);    // 蓝色收招
    public Color cEmpty = new(1f, 1f, 1f, 0.08f);    // 空白格

    private Image[,] cells;                  // [player 0/1, frame 0..59]
    private RectTransform[] cursor;
    private TextMeshProUGUI[] infoText;
    private TextMeshProUGUI[,] segLabel;     // 段末标签 [player, 0..2]
    private float[] barY = new float[2];
    private float startX;

    // ==================== 自动构建 UI ====================
    private void Awake()
    {
        var root = new GameObject("FrameMeterCanvas");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        DontDestroyOnLoad(root);

        cells = new Image[2, totalCells];
        cursor = new RectTransform[2];
        infoText = new TextMeshProUGUI[2];
        segLabel = new TextMeshProUGUI[2, 3];

        float barWidth = totalCells * cellWidth;
        startX = (1920f - barWidth) / 2f;    // 屏幕水平居中

        for (int p = 0; p < 2; p++)
        {
            barY[p] = p == 0 ? 220f : 180f;   // P1 在上，P2 在下

            // ---- 60 个格子 ----
            for (int i = 0; i < totalCells; i++)
            {
                var go = new GameObject($"P{p + 1}_Cell{i}");
                go.transform.SetParent(root.transform, false);
                var img = go.AddComponent<Image>();
                img.color = cEmpty;
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(cellWidth - 1f, barHeight);
                rt.anchoredPosition = new Vector2(startX + i * cellWidth + 0.5f, barY[p]);
                cells[p, i] = img;
            }

            // ---- 3 个段末数字标签 ----
            for (int s = 0; s < 3; s++)
            {
                var go = new GameObject($"P{p + 1}_Seg{s}");
                go.transform.SetParent(root.transform, false);
                var txt = go.AddComponent<TextMeshProUGUI>();
                txt.fontSize = 10f;
                txt.color = Color.white;
                txt.alignment = TextAlignmentOptions.Center;
                txt.raycastTarget = false;
                var rt = txt.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(cellWidth * 3f, 12f);
                segLabel[p, s] = txt;
            }

            // ---- 游标（白色竖线）----
            var cg = new GameObject($"P{p + 1}_Cursor");
            cg.transform.SetParent(root.transform, false);
            var curImg = cg.AddComponent<Image>();
            curImg.color = Color.white;
            curImg.raycastTarget = false;
            cursor[p] = curImg.rectTransform;
            cursor[p].sizeDelta = new Vector2(3f, barHeight + 6f);
            cursor[p].anchorMin = cursor[p].anchorMax = new Vector2(0f, 0f);

            // ---- 信息文本（P1 在上，P2 在下）----
            var ig = new GameObject($"P{p + 1}_Info");
            ig.transform.SetParent(root.transform, false);
            var info = ig.AddComponent<TextMeshProUGUI>();
            info.fontSize = 14f;
            info.color = Color.white;
            info.raycastTarget = false;
            infoText[p] = info;
            var igt = info.rectTransform;
            igt.anchorMin = igt.anchorMax = new Vector2(0f, 0f);
            igt.sizeDelta = new Vector2(barWidth, 18f);
            igt.anchoredPosition = new Vector2(startX, p == 0 ? barY[p] + barHeight + 4f : barY[p] - 22f);
        }
    }

    // ==================== 每帧更新 ====================
    private void LateUpdate()
    {
        var players = GameManager.Instance != null ? GameManager.Instance.ActivePlayers : null;
        for (int p = 0; p < 2; p++)
            UpdateMeter(p, players != null && p < players.Count ? players[p] : null);
    }

    private void UpdateMeter(int p, FighterController f)
    {
        // 1. 清空：全部格 + 段末标签清空
        for (int i = 0; i < totalCells; i++)
            cells[p, i].color = cEmpty;
        for (int s = 0; s < 3; s++)
            segLabel[p, s].text = "";

        bool inAttack = f != null
            && f.StateMachine.CurrentState == FighterState.Attack
            && f.attackData != null;

        float cursorX = -100f;   // 非攻击态游标移出屏幕
        if (inAttack)
        {
            var atk = f.attackData;
            int startF = ToF(atk.startupTime);
            int actF = ToF(atk.activeTime);
            int recF = ToF(atk.recoveryTime);
            int totalF = startF + actF + recF;

            // 已过帧 = 总时长 - 剩余倒计时（attackTimer 剩余秒）
            int curFrame = Mathf.FloorToInt((atk.TotalDuration - f.attackTimer) * 60f);
            curFrame = Mathf.Clamp(curFrame, 0, Mathf.Min(totalF, totalCells));

            // 2. 分段颜色 + 段末数字（segIdx 直接传入，不再引用局部变量）
            PaintSeg(p, 0, 0, startF, cStartup, startF);
            PaintSeg(p, 1, startF, actF, cActive, actF);
            PaintSeg(p, 2, startF + actF, recF, cRecovery, recF);

            // 3. 硬直差 = 对方受击硬直 - 我方收招
            float knockSpeed = DamageSystem.CalculateKnockbackVelocity(atk, f.CurrentDamage, f.fighterData.weight);
            int hitstunF = Mathf.RoundToInt(DamageSystem.CalculateHitstun(knockSpeed));
            int adv = hitstunF - recF;
            infoText[p].text = $"起手 {startF}F | 总帧 {totalF}F | 硬直差 {(adv >= 0 ? "+" : "")}{adv}F";

            cursorX = startX + curFrame * cellWidth;
        }
        else
        {
            infoText[p].text = "起手 -- | 总帧 -- | 硬直差 0F";
        }

        cursor[p].anchoredPosition = new Vector2(cursorX, barY[p]);
    }

    // 涂一段格子，段末标出该段的帧数；全部参数传入，不引用外部变量
    private void PaintSeg(int p, int segIdx, int from, int len, Color color, int segFrames)
    {
        int end = Mathf.Min(from + len, totalCells);
        for (int i = from; i < end; i++)
            cells[p, i].color = color;

        if (end > from)
        {
            segLabel[p, segIdx].text = $"{segFrames}";
            float x = startX + (end - 0.5f) * cellWidth;   // 段末格中心
            segLabel[p, segIdx].rectTransform.anchoredPosition = new Vector2(x, barY[p] + 4f);
        }
    }

    private int ToF(float seconds) => Mathf.RoundToInt(seconds * 60f);
}