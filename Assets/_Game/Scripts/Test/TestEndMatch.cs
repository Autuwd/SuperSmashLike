using UnityEngine;
using SuperSmashLike.Managers;

// ============================================================
// TestEndMatch — 调试用：开局 2 秒强制结束比赛
// 职责：模拟"P1 获胜"，用于快速验证结算流程
// 架构位置：Test 层（调试脚本，不属于正式功能）
//
// 【重点】这是调试残留脚本，用完必须删除！
//   历史踩坑：曾因本脚本挂在场景里，导致"命数制开局几秒就判 P1 胜"，
//   排查了很久才定位到 Start 里的 Invoke("Fire", 2f)。
// 【排查提示】改代码前先检查 Test/ 目录下有没有测试脚本挂在场景上。
// ============================================================
public class TestEndMatch : MonoBehaviour
{
    private void Start()
    {
        Invoke(nameof(Fire), 2f);
    }

    // 【做什么】强制结束比赛，指定 P1（playerID=0）为胜者
    private void Fire()
    {
        GetComponent<MatchManager>().EndMatch(0);
    }
}
