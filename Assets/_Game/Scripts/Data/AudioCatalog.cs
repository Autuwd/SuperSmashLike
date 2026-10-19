using UnityEngine;

// ============================================================
// AudioCatalog — 音频资源目录（ScriptableObject 资产）
// 职责：
//   1. 以枚举 ID（SfxId / MusicId）索引全部音效与 BGM 片段
//   2. 为 AudioManager 提供统一的 clip 查询入口（越界返回 null，不抛异常）
// 架构位置：Data 层（纯数据，无行为）
// 使用方式：
//   右键 → Create → SuperSmashLike → Audio Catalog
//   资产位于 Assets/_Game/ScriptableObjects/Audio/AudioCatalog.asset（由 AudioSetupWizard 生成）
// 【为什么用枚举而不是字符串】：编译期可检查、调用点零 GC、
//   wizard 按枚举顺序填数组即可，避免字符串拼写错误导致的静默失配。
// ============================================================
namespace SuperSmashLike.Core
{
    /// <summary>SFX 标识。数组下标与枚举值一一对应，Count 为数组长度。</summary>
    public enum SfxId
    {
        HitLight = 0, HitMedium = 1, HitHeavy = 2, Swing = 3,
        ShieldHit = 4, ShieldBreak = 5, Parry = 6, KO = 7,
        Jump = 8, Land = 9, Dash = 10,
        UiMove = 11, UiConfirm = 12, UiCancel = 13, UiStart = 14, Countdown = 15,
        Count = 16
    }

    /// <summary>BGM 标识。数组下标与枚举值一一对应，Count 为数组长度。</summary>
    public enum MusicId { Title = 0, Select = 1, Battle = 2, Result = 3, Count = 4 }

    [CreateAssetMenu(menuName = "SuperSmashLike/Audio Catalog", fileName = "AudioCatalog")]
    public class AudioCatalog : ScriptableObject
    {
        #region 1. Inspector 配置

        [Header("SFX（按 SfxId 顺序，长度 = SfxId.Count）")]
        public AudioClip[] sfx = new AudioClip[(int)SfxId.Count];

        [Header("BGM（按 MusicId 顺序，长度 = MusicId.Count）")]
        public AudioClip[] music = new AudioClip[(int)MusicId.Count];

        #endregion

        #region 2. 查询 API

        // 【为什么越界返回 null 而不是抛异常】：
        //   AudioManager 收到 null clip 时静默 no-op——
        //   资源缺失不该让整场战斗报错刷屏，只影响这一次播放。
        public AudioClip GetSfx(SfxId id)
        {
            int i = (int)id;
            if (i < 0 || i >= sfx.Length) return null;   // 防御：手改数组长度后仍安全
            return sfx[i];
        }

        public AudioClip GetMusic(MusicId id)
        {
            int i = (int)id;
            if (i < 0 || i >= music.Length) return null;
            return music[i];
        }

        #endregion
    }
}
