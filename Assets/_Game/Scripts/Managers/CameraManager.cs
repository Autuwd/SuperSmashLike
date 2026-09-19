using UnityEngine;
using SuperSmashLike.Core;
using System.Collections.Generic;
using UnityEngine.UI;

// ============================================================
// CameraManager — 摄像机管理器
// 职责：
//   1. 大乱斗风格的多目标跟踪摄像机
//   2. 自动计算所有存活玩家的中心点
//   3. 根据玩家分散程度自动缩放（距离远→拉远，距离近→推近）
//   4. 限制摄像机移动范围（不超出舞台边界）
//   5. 提供打击感反馈：Shake（震屏）、FlashWhite（全屏闪白）
// 架构位置：Managers 层，场景中挂载在 Camera 物体上
// 依赖：GameManager（读 ActivePlayers）
// 被谁使用：Hitbox（震屏）、FighterController（盾反/受身闪白）
//
// 关键技术：
//   - SmoothDamp：平滑跟随，避免摄像机抖动
//   - InverseLerp：把玩家分散距离映射到缩放系数
//   - LateUpdate：在所有 Update 执行完后更新，确保玩家位置已更新
// 【注意】必须在 LateUpdate 算相机 —— 放 Update 会因时序问题导致抖动
// ============================================================
namespace SuperSmashLike.Managers
{
    public class CameraManager : MonoBehaviour
    {
        #region 1. Inspector 配置

        [Header("Camera Settings")]
        public Camera targetCamera;                        // 要控制的摄像机（默认 main camera）
        public Vector3 cameraOffset = new(0f, 6f, -12f);   // 摄像机偏移（俯视 2.5D 视角）

        [Header("Zoom")]
        public float minZoom = 8f;               // 最近缩放（单人/贴在一起时）
        public float maxZoom = 20f;              // 最远缩放（4人分散时）
        public float zoomSmoothTime = 0.3f;      // 缩放平滑时间

        [Header("Movement")]
        public float moveSmoothTime = 0.15f;                // 移动平滑时间
        public Vector2 cameraBoundsMin = new(-20f, -10f);   // 摄像机移动范围最小值（左下）
        public Vector2 cameraBoundsMax = new(20f, 15f);     // 摄像机移动范围最大值（右上）

        #endregion

        #region 2. 运行时状态

        private Vector3 moveVelocity;  // SmoothDamp 速度引用（移动）
        private float zoomVelocity;    // SmoothDamp 速度引用（缩放）
        private float currentZoom;     // 当前正交尺寸

        // 震屏
        private float shakeIntensity;    // 当前震动强度
        private float shakeDuration;     // 总震动时长
        private float shakeElapsed;      // 已震动时间

        // 全屏闪白（代码自动创建，零场景操作）
        private Image whiteFlashImage;

        #endregion

        #region 3. Unity 生命周期

        // 【做什么】代码自动创建全屏白块（ScreenSpace Overlay Canvas + 拉伸 Image）
        // 【注意】跨场景常驻（选人 → 战斗通用），所以 DontDestroyOnLoad
        private void Awake()
        {
            var canvasGO = new GameObject("ParryFlashCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var imgGO = new GameObject("WhiteFlash");
            imgGO.transform.SetParent(canvasGO.transform, false);

            var img = imgGO.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);    // 默认全透明
            img.raycastTarget = false;                 // 不挡 UI 点击

            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;                // 全屏四角拉伸
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            whiteFlashImage = img;
            DontDestroyOnLoad(canvasGO);
        }

        private void Start()
        {
            if (targetCamera == null) targetCamera = Camera.main;

            // 强制覆盖 Inspector 旧值（历史坑：序列化旧值会盖掉代码默认值）
            cameraOffset = new Vector3(0f, 6f, -12f);
            currentZoom = targetCamera.orthographicSize;
        }

        // 【做什么】每帧跟随：算中心 → 算分散 → 定缩放 → 定位置 → 看向中心
        // 【注意】必须是 LateUpdate（等所有角色移动完成后再算）
        private void LateUpdate()
        {
            List<FighterController> activePlayers = GameManager.Instance.ActivePlayers;
            if (activePlayers == null || activePlayers.Count == 0) return;

            // 1. 所有存活玩家的中心位置
            Vector3 center = CalculateCenter(activePlayers);

            // 2. 玩家分散程度（最远两名玩家的距离）
            float spread = CalculateSpread(activePlayers);

            // 3. 分散距离映射到缩放：0 距离 → minZoom，30 距离 → maxZoom
            float targetZoom = Mathf.Lerp(minZoom, maxZoom, Mathf.InverseLerp(0f, 30f, spread));

            // 4. 平滑缩放
            currentZoom = Mathf.SmoothDamp(currentZoom, targetZoom, ref zoomVelocity, zoomSmoothTime);
            targetCamera.orthographicSize = currentZoom;

            // 5. 目标位置（中心 + 偏移）并夹在边界内
            Vector3 targetPosition = center + cameraOffset;
            targetPosition.x = Mathf.Clamp(targetPosition.x, cameraBoundsMin.x, cameraBoundsMax.x);
            targetPosition.y = Mathf.Clamp(targetPosition.y, cameraBoundsMin.y, cameraBoundsMax.y);

            // 6. 平滑移动 + 叠加震动偏移
            Vector3 finalPosition = Vector3.SmoothDamp(transform.position, targetPosition, ref moveVelocity, moveSmoothTime);
            transform.position = finalPosition + CalculateShakeOffset();

            // 7. 让相机看向角色中心（旋转后的画面中心 = 视线落点）
            transform.LookAt(new Vector3(center.x, center.y + 0.5f, center.z));
        }

        #endregion

        #region 4. 公开 API

        // 【做什么】触发震屏（命中/重击时调用）
        // 【参数】intensity = 强度；duration = 时长
        // 【注意】多次调用取最大值而不是叠加，避免连续命中震到失控
        public void Shake(float intensity, float duration)
        {
            shakeIntensity = Mathf.Max(shakeIntensity, intensity);
            shakeDuration = Mathf.Max(shakeDuration, duration);
            shakeElapsed = 0f;
        }

        // 【做什么】全屏闪白（盾反 / 受身反馈）
        // 【注意】先 StopAllCoroutines 防连触发叠加
        public void FlashWhite(float duration)
        {
            if (whiteFlashImage == null) return;

            StopAllCoroutines();
            StartCoroutine(FlashRoutine(duration));
        }

        #endregion

        #region 5. 私有逻辑

        // 【做什么】计算所有存活玩家的平均位置（摄像机跟踪中心）
        // 【注意】死亡角色不参与计算，否则死亡后镜头会被拖住
        private Vector3 CalculateCenter(List<FighterController> players)
        {
            if (players.Count == 0) return Vector3.zero;
            if (players.Count == 1) return players[0].transform.position;

            Vector3 sum = Vector3.zero;
            int aliveCount = 0;

            foreach (var p in players)
            {
                if (p.StateMachine.CurrentState == FighterState.Dead) continue;
                sum += p.transform.position;
                aliveCount++;
            }

            return aliveCount > 0 ? sum / aliveCount : Vector3.zero;
        }

        // 【做什么】计算存活玩家之间的最大距离（决定缩放）
        // 【注意】O(n²) 遍历所有玩家对；当前最多 4 人，性能无压力
        private float CalculateSpread(List<FighterController> players)
        {
            if (players.Count <= 1) return 0f;

            float maxDist = 0f;
            for (int i = 0; i < players.Count; i++)
            {
                for (int j = i + 1; j < players.Count; j++)
                {
                    if (players[i].StateMachine.CurrentState == FighterState.Dead
                        || players[j].StateMachine.CurrentState == FighterState.Dead)
                        continue;

                    float dist = Vector3.Distance(players[i].transform.position, players[j].transform.position);
                    if (dist > maxDist) maxDist = dist;
                }
            }
            return maxDist;
        }

        // 【做什么】计算当前帧震动偏移
        // 【注意】必须用 unscaledDeltaTime —— Hitstop 把 timeScale 冻结时，震动仍要能播完
        private Vector3 CalculateShakeOffset()
        {
            if (shakeDuration <= 0f) return Vector3.zero;

            shakeElapsed += Time.unscaledDeltaTime;
            float t = shakeElapsed / shakeDuration;        // 0→1 进度
            float strength = shakeIntensity * (1f - t);    // 线性衰减

            // 每帧重新随机，产生不规则震动感
            Vector3 offset = new Vector3(
                (Random.value * 2f - 1f) * strength,
                (Random.value * 2f - 1f) * strength,
                0f);

            if (t >= 1f)
            {
                shakeDuration = 0f;
                shakeIntensity = 0f;
            }
            return offset;
        }

        // 【做什么】闪白协程：亮起 30% 时间 → 保持峰值 → 衰减归零
        // 【注意】全程用 unscaledDeltaTime，保证 Hitstop 期间也能闪完
        private System.Collections.IEnumerator FlashRoutine(float duration)
        {
            var c = whiteFlashImage.color;
            float t = 0f;
            float rise = duration * 0.3f;

            // 上升段
            while (t < rise)
            {
                t += Time.unscaledDeltaTime;
                c.a = Mathf.Clamp01(t / rise);
                whiteFlashImage.color = c;
                yield return null;
            }

            // 衰减段
            c.a = 1f;
            whiteFlashImage.color = c;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                c.a = Mathf.Clamp01(1f - (t - rise) / (duration - rise));
                whiteFlashImage.color = c;
                yield return null;
            }

            c.a = 0f;
            whiteFlashImage.color = c;
        }

        #endregion

        #region 6. 调试可视化

        // 【做什么】在 Scene 视图画出摄像机移动边界
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 center = (cameraBoundsMin + cameraBoundsMax) / 2f;
            Vector3 size = cameraBoundsMax - cameraBoundsMin;
            Gizmos.DrawWireCube(center, size);
        }

        #endregion
    }
}
