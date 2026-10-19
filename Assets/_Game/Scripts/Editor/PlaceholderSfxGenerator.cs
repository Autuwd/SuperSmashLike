using System;
using System.IO;
using UnityEngine;

// ============================================================
// PlaceholderSfxGenerator — 生成占位音效 WAV（离线工具，可重复运行）
// 用途：M8 尚无美术音效资源，先用代码合成可辨识的短音，保证音效链路可验证
// 使用：Unity 菜单 SuperSmashLike/40a. Generate Placeholder SFX
//      或 -executeMethod PlaceholderSfxGenerator.Run
// 说明：
//   1. 44.1kHz / 16bit / 单声道 PCM，体积小、Unity 直接导入
//   2. 幂等：已存在的文件默认跳过（force=true 才覆盖）
//   3. 每种音效有独立合成配方，便于耳朵区分（不是同一段噪声复制 16 份）
// ============================================================
public static class PlaceholderSfxGenerator
{
    private const int SampleRate = 44100;
    private const string OutDir = "Assets/_Game/Art/Audio/SFX";

    public static void Run() => Generate(false);

    public static void Generate(bool force)
    {
        _force = force;
        Directory.CreateDirectory(OutDir);

        // ---- 命中：轻 / 中 / 重（噪声 + 低频冲击，力度递增）----
        Write("hit_light", 0.07f, NoiseBurst(0.07f, 900f, 0.55f, 0.30f));
        Write("hit_medium", 0.11f, NoiseBurst(0.11f, 520f, 0.75f, 0.55f));
        Write("hit_heavy", 0.20f, Mix(
            NoiseBurst(0.20f, 260f, 0.95f, 0.75f),
            Tone(0.20f, 70f, 0.55f, decay: 7f)));

        // ---- 挥空：高频"嗖"（扫频噪声）----
        Write("swing", 0.12f, SweepNoise(0.12f, 1800f, 420f, 0.32f));

        // ---- 护盾：金属短鸣（两个不协和正弦）----
        Write("shield_hit", 0.13f, Mix(
            Tone(0.13f, 880f, 0.35f, decay: 22f),
            Tone(0.13f, 1245f, 0.22f, decay: 26f)));

        // ---- 破盾：下滑噪声 + 低频 ----
        Write("shield_break", 0.38f, Mix(
            SweepNoise(0.38f, 1600f, 180f, 0.60f),
            Tone(0.38f, 110f, 0.35f, decay: 9f)));

        // ---- 盾反：上扬三音（C-E-G）----
        Write("parry", 0.30f, Mix(
            Tone(0.10f, 784f, 0.42f, decay: 30f),
            Delayed(Tone(0.10f, 988f, 0.42f, decay: 30f), 0.06f),
            Delayed(Tone(0.12f, 1319f, 0.46f, decay: 24f), 0.12f)));

        // ---- KO：低频爆炸（噪声 + 下扫低频）----
        Write("ko", 0.70f, Mix(
            NoiseBurst(0.70f, 180f, 1.0f, 1.0f),
            SweepTone(0.70f, 220f, 45f, 0.75f, decay: 4.5f)));

        // ---- 跳跃：短促上扫 ----
        Write("jump", 0.10f, SweepTone(0.10f, 320f, 720f, 0.30f, decay: 16f));

        // ---- 落地：低频闷响 ----
        Write("land", 0.09f, Mix(
            NoiseBurst(0.09f, 220f, 0.45f, 0.35f),
            Tone(0.09f, 90f, 0.30f, decay: 26f)));

        // ---- 冲刺：短噪声 ----
        Write("dash", 0.09f, SweepNoise(0.09f, 900f, 1600f, 0.24f));

        // ---- UI：移动 / 确认 / 取消 / 开始 / 倒计时 ----
        Write("ui_move", 0.045f, Tone(0.045f, 1180f, 0.24f, decay: 40f));
        Write("ui_confirm", 0.12f, Mix(
            Tone(0.06f, 880f, 0.34f, decay: 30f),
            Delayed(Tone(0.06f, 1320f, 0.34f, decay: 30f), 0.05f)));
        Write("ui_cancel", 0.12f, Mix(
            Tone(0.06f, 660f, 0.32f, decay: 30f),
            Delayed(Tone(0.06f, 440f, 0.32f, decay: 30f), 0.05f)));
        Write("ui_start", 0.34f, Mix(
            Tone(0.10f, 523f, 0.34f, decay: 22f),
            Delayed(Tone(0.10f, 659f, 0.34f, decay: 22f), 0.09f),
            Delayed(Tone(0.14f, 784f, 0.38f, decay: 18f), 0.18f)));
        Write("countdown", 0.09f, Tone(0.09f, 1046f, 0.30f, decay: 26f));

        Debug.Log($"[PlaceholderSFX] 完成：{OutDir}（force={force}）");
        UnityEditor.AssetDatabase.Refresh();
    }

    private static void Write(string name, float seconds, float[] data)
    {
        var path = $"{OutDir}/{name}.wav";
        if (File.Exists(path) && !_force) return;
        WriteWav(path, data, SampleRate);
    }

    private static bool _force;

    // ---------- 合成基元 ----------

    private static float[] Tone(float seconds, float freq, float amp, float decay)
    {
        int n = Mathf.CeilToInt(seconds * SampleRate);
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            float env = Mathf.Exp(-decay * t);
            d[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * amp * env;
        }
        return d;
    }

    private static float[] SweepTone(float seconds, float f0, float f1, float amp, float decay)
    {
        int n = Mathf.CeilToInt(seconds * SampleRate);
        var d = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float f = Mathf.Lerp(f0, f1, t);
            phase += 2f * Mathf.PI * f / SampleRate;
            float env = Mathf.Exp(-decay * t * seconds);
            d[i] = Mathf.Sin(phase) * amp * env;
        }
        return d;
    }

    private static float[] NoiseBurst(float seconds, float lowpassHz, float amp, float decay)
    {
        int n = Mathf.CeilToInt(seconds * SampleRate);
        var d = new float[n];
        var rng = new System.Random(12345);
        float a = Mathf.Clamp01(2f * Mathf.PI * lowpassHz / SampleRate);
        float y = 0f;
        for (int i = 0; i < n; i++)
        {
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            y += a * (x - y);                       // 一阶低通
            float t = (float)i / SampleRate;
            float env = Mathf.Exp(-decay * t / Mathf.Max(0.01f, seconds) * 4f);
            d[i] = y * amp * env;
        }
        return d;
    }

    private static float[] SweepNoise(float seconds, float f0, float f1, float amp)
    {
        int n = Mathf.CeilToInt(seconds * SampleRate);
        var d = new float[n];
        var rng = new System.Random(777);
        float y = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float cutoff = Mathf.Lerp(f0, f1, t);
            float a = Mathf.Clamp01(2f * Mathf.PI * cutoff / SampleRate);
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            y += a * (x - y);
            float env = Mathf.Sin(Mathf.PI * t);    // 起落平滑
            d[i] = y * amp * env;
        }
        return d;
    }

    private static float[] Delayed(float[] src, float seconds)
    {
        int off = Mathf.CeilToInt(seconds * SampleRate);
        var d = new float[off + src.Length];
        Array.Copy(src, 0, d, off, src.Length);
        return d;
    }

    private static float[] Mix(params float[][] parts)
    {
        int len = 0;
        foreach (var p in parts) len = Mathf.Max(len, p.Length);
        var d = new float[len];
        foreach (var p in parts)
            for (int i = 0; i < p.Length; i++) d[i] += p[i];
        // 防削波
        float peak = 0.0001f;
        foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
        if (peak > 0.95f)
        {
            float k = 0.95f / peak;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }
        return d;
    }

    private static void WriteWav(string path, float[] samples, int rate)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        int dataLen = samples.Length * 2;

        bw.Write(new[] { 'R', 'I', 'F', 'F' });
        bw.Write(36 + dataLen);
        bw.Write(new[] { 'W', 'A', 'V', 'E' });
        bw.Write(new[] { 'f', 'm', 't', ' ' });
        bw.Write(16);
        bw.Write((short)1);            // PCM
        bw.Write((short)1);            // mono
        bw.Write(rate);
        bw.Write(rate * 2);            // byte rate
        bw.Write((short)2);            // block align
        bw.Write((short)16);           // bits
        bw.Write(new[] { 'd', 'a', 't', 'a' });
        bw.Write(dataLen);

        foreach (var s in samples)
        {
            short v = (short)Mathf.Clamp(Mathf.RoundToInt(s * short.MaxValue), short.MinValue, short.MaxValue);
            bw.Write(v);
        }
    }
}
