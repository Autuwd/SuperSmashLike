using UnityEngine;
using SuperSmashLike.Core;
using System.Collections.Generic;

// ============================================================
// CameraManager — 摄像机管理器
// 职责：
//   1. 大乱斗风格的多目标跟踪摄像机
//   2. 自动计算所有存活玩家的中心点
//   3. 根据玩家分散程度自动缩放（距离远→拉远，距离近→推近）
//   4. 限制摄像机移动范围（不超出舞台边界）
// 架构位置：Managers 层，场景中挂载在 Camera 物体上
// 关键技术：
//   - SmoothDamp：平滑跟随，避免摄像机抖动
//   - InverseLerp：将玩家分散距离映射到缩放系数
//   - LateUpdate：在所有 Update 执行完后更新，确保玩家位置已更新
// ============================================================
namespace SuperSmashLike.Managers
{
    public class CameraManager : MonoBehaviour
    {
        [Header("Camera Settings")]
        public Camera targetCamera;              // 要控制的摄像机（默认 main camera）
        public Vector3 cameraOffset = new(0f, 6f, -12f);  // 摄像机偏移（Z轴-10：俯视平面）

        [Header("Zoom")]
        public float minZoom = 8f;               // 最近缩放（单人/贴在一起时）
        public float maxZoom = 20f;               // 最远缩放（4人分散时）
        public float zoomSmoothTime = 0.3f;       // 缩放平滑时间

        [Header("Movement")]
        public float moveSmoothTime = 0.15f;             // 移动平滑时间
        public Vector2 cameraBoundsMin = new(-20f, -10f); // 摄像机移动范围最小值（左下）
        public Vector2 cameraBoundsMax = new(20f, 15f);   // 摄像机移动范围最大值（右上）

        private Vector3 moveVelocity;  // SmoothDamp 使用的速度引用
        private float zoomVelocity;    // zoom SmoothDamp 速度引用
        private float currentZoom;     // 当前缩放值

        // ===== Screen Shake =====
        private float shakeIntensity;    // 当前震动强度
        private float shakeDuration;     // 剩余震动时间
        private float shakeElapsed;      // 已震动时间
        private Vector3 shakeOffset;     // 当前帧偏移量

        private void Start()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            cameraOffset = new Vector3(0f, 6f, -12f);   // 强制覆盖 Inspector 旧值
            currentZoom = targetCamera.orthographicSize;
        }

        private void LateUpdate()
        {
            List<FighterController> activePlayers = GameManager.Instance.ActivePlayers;
            if (activePlayers == null || activePlayers.Count == 0)
                return;

            // 计算所有存活玩家的中心位置
            Vector3 center = CalculateCenter(activePlayers);

            // 计算玩家分散程度（最远两名玩家的距离）
            float spread = CalculateSpread(activePlayers);

            // 将玩家分散距离映射到缩放值
            // 0距离→minZoom, 30距离→maxZoom
            float targetZoom = Mathf.Lerp(minZoom, maxZoom,
                Mathf.InverseLerp(0f, 30f, spread));

            // 平滑缩放
            currentZoom = Mathf.SmoothDamp(currentZoom, targetZoom,
                ref zoomVelocity, zoomSmoothTime);
            targetCamera.orthographicSize = currentZoom;

            // 计算最终位置（中心+偏移）并限制在边界内
            Vector3 targetPosition = center + cameraOffset;
            targetPosition.x = Mathf.Clamp(targetPosition.x, cameraBoundsMin.x, cameraBoundsMax.x);
            targetPosition.y = Mathf.Clamp(targetPosition.y, cameraBoundsMin.y, cameraBoundsMax.y);

            // 平滑移动
            Vector3 finalPosition = Vector3.SmoothDamp(transform.position,
                targetPosition, ref moveVelocity, moveSmoothTime);
            transform.position = finalPosition + CalculateShakeOffset();

            // 让相机看向角色中心（旋转后的画面中心 = 视线落点 = 角色位置）
            if (targetCamera != null)
                transform.LookAt(new Vector3(center.x, center.y + 0.5f, center.z));
        }

        // 计算所有存活玩家的平均位置（摄像机跟踪中心）
        // 死亡角色不参与计算
        private Vector3 CalculateCenter(List<FighterController> players)
        {
            if (players.Count == 0) return Vector3.zero;
            if (players.Count == 1) return players[0].transform.position;

            Vector3 sum = Vector3.zero;
            int aliveCount = 0;
            foreach (var p in players)
            {
                if (p.StateMachine.CurrentState != FighterState.Dead)
                {
                    sum += p.transform.position;
                    aliveCount++;
                }
            }
            return aliveCount > 0 ? sum / aliveCount : Vector3.zero;
        }

        // 计算存活玩家之间的最大距离
        // 用于决定摄像机缩放比例
        private float CalculateSpread(List<FighterController> players)
        {
            if (players.Count <= 1) return 0f;

            float maxDist = 0f;
            // 遍历所有玩家对，找出最远距离
            for (int i = 0; i < players.Count; i++)
            {
                for (int j = i + 1; j < players.Count; j++)
                {
                    if (players[i].StateMachine.CurrentState == FighterState.Dead
                        || players[j].StateMachine.CurrentState == FighterState.Dead)
                        continue;

                    float dist = Vector3.Distance(players[i].transform.position,
                        players[j].transform.position);
                    if (dist > maxDist) maxDist = dist;
                }
            }
            return maxDist;
        }

        // Scene 视图显示摄像机边界
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 center = (cameraBoundsMin + cameraBoundsMax) / 2f;
            Vector3 size = cameraBoundsMax - cameraBoundsMin;
            Gizmos.DrawWireCube(center, size);
        }

        // 触发震动（命中/重击时调用）
        public void Shake(float intensity, float duration)
        {
            shakeIntensity = Mathf.Max(shakeIntensity, intensity);  // 叠加取最大
            shakeDuration = Mathf.Max(shakeDuration, duration);
            shakeElapsed = 0f;
        }

        // 计算当前帧震动偏移（在 LateUpdate 里调用）
        private Vector3 CalculateShakeOffset()
        {
            if (shakeDuration <= 0f) return Vector3.zero;

            shakeElapsed += Time.unscaledDeltaTime;   // 注意：用真实时间，Hitstop 冻结时震动不停
            float t = shakeElapsed / shakeDuration;   // 0→1 衰减曲线
            float strength = shakeIntensity * (1f - t);  // 线性衰减

            // 随机抖动（每帧重新随机，产生不规则震动感）
            Vector3 offset = new Vector3(
                (Random.value * 2f - 1f) * strength,
                (Random.value * 2f - 1f) * strength,
                0f);

            if (t >= 1f)
            {
                shakeDuration = 0f;  // 震动结束，归零
                shakeIntensity = 0f;
            }
            return offset;
        }
    }
}
