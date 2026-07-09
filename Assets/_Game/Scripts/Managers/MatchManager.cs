using UnityEngine;
using SuperSmashLike.Core;
using System.Collections;
using System.Collections.Generic;

// ============================================================
// MatchManager — 比赛管理器
// 职责：
//   1. 控制一场比赛的完整流程（倒计时→进行中→结束）
//   2. 执行比赛规则（时间制 vs 命数制）
//   3. 管理玩家出生/重生
// 架构位置：Managers 层，每个场景一个实例
// 依赖：
//   - GameManager.Instance.gameSettings → 读取规则配置
//   - GameManager.Instance.ActivePlayers → 获取所有玩家
//   - CameraManager → 更新摄像机
//   - BlastZone.OnPlayerOutOfBounds → 触发击杀→重生
// ============================================================
namespace SuperSmashLike.Managers
{
    public class MatchManager : MonoBehaviour
    {
        [Header("References")]
        public CameraManager cameraManager;      // 场景中的摄像机管理器
        public Transform[] spawnPoints;          // 玩家出生点（从 Scene 拖入）

        [Header("Settings")]
        public float countdownTime = 3f;         // 开局倒计时秒数
        public float gameOverDelay = 3f;         // 结束后延迟（用于播放胜利动画）

        public float MatchTimeRemaining { get; private set; }  // 比赛剩余时间
        public bool IsMatchActive { get; private set; }        // 比赛是否进行中

        public System.Action<float> OnTimerUpdated;   // 计时更新事件（UI 监听此事件刷新显示）
        public System.Action<int> OnPlayerScored;     // 玩家得分事件
        public System.Action<int> OnGameOver;         // 比赛结束事件（参数=胜者ID）

        private GameSettings settings;
        private Coroutine matchCoroutine;

        private void Start()
        {
            settings = GameManager.Instance.gameSettings;
            MatchTimeRemaining = settings.matchTimeSeconds;
            StartMatch();
        }

        // 开始比赛（启动协程控制比赛流程）
        public void StartMatch()
        {
            IsMatchActive = false;
            matchCoroutine = StartCoroutine(MatchFlow());
        }

        // 比赛流程协程
        // 阶段1：倒计时（3秒）
        // 阶段2：比赛进行（循环检测结束条件）
        // 阶段3：比赛结束
        private IEnumerator MatchFlow()
        {
            // === 阶段1：开局倒计时 ===
            for (int i = (int)countdownTime; i > 0; i--)
            {
                Debug.Log($"Countdown: {i}");
                yield return new WaitForSeconds(1f);
            }

            // === 阶段2：比赛进行 ===
            IsMatchActive = true;
            Debug.Log("Match Started!");

            while (IsMatchActive)
            {
                // 更新时间
                MatchTimeRemaining -= Time.deltaTime;
                OnTimerUpdated?.Invoke(MatchTimeRemaining);

                // 时间制：时间到 → 结束
                if (settings.matchMode == GameSettings.MatchMode.Time && MatchTimeRemaining <= 0f)
                {
                    EndMatch();
                    yield break;
                }

                // 命数制：存活玩家 ≤ 1 → 结束
                if (settings.matchMode == GameSettings.MatchMode.Stock)
                {
                    var alivePlayers = GameManager.Instance.ActivePlayers
                        .FindAll(p => p.StateMachine.CurrentState != FighterState.Dead);

                    if (alivePlayers.Count <= 1)
                    {
                        yield return new WaitForSeconds(gameOverDelay);
                        // 如果还有存活玩家，它的ID就是胜者；否则平局(-1)
                        EndMatch(alivePlayers.Count == 1 ? alivePlayers[0].playerID : -1);
                        yield break;
                    }
                }

                yield return null;
            }
        }

        // 结束比赛
        public void EndMatch(int winnerID = -1)
        {
            IsMatchActive = false;
            OnGameOver?.Invoke(winnerID);
            GameManager.Instance.EndMatch();  // 通知 GameManager 切换到结算状态
        }

        // 重生玩家（由 BlastZone 触发击杀后调用）
        public void RespawnPlayer(FighterController fighter)
        {
            Transform spawn = GetRandomSpawnPoint();
            fighter.Respawn(spawn.position);
        }

        private Transform GetRandomSpawnPoint()
        {
            if (spawnPoints.Length == 0) return transform;
            return spawnPoints[Random.Range(0, spawnPoints.Length)];
        }

        private void OnDestroy()
        {
            if (matchCoroutine != null)
                StopCoroutine(matchCoroutine);
        }
    }
}
