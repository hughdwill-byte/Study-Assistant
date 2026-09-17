using System.IO;
using System.Runtime.InteropServices;

namespace StudyHud.Overlay;

/// <summary>
/// Plays short distinct chimes for Focus-timer phase changes, so the user hears when a phase starts or
/// ends even when not looking at the HUD. Tones are synthesised into an in-memory WAV and played
/// asynchronously via winmm's PlaySound — no audio files to ship, no extra dependencies. Every call is
/// guarded: a machine with no audio device must never crash or block the timer.
/// </summary>
internal static class PomodoroSounds
{
    private const int SampleRate = 44100;

    // PlaySound with SND_ASYNC reads the buffer as it plays, so it must outlive the call — keep the
    // most recent one alive in a static field (the tones are short, so at most one is in flight).
    private static byte[]? _pinned;
    private static readonly object _gate = new();

    /// <summary>Plays the chime for the phase just entered. Work rises; breaks fall; long break falls further.</summary>
    public static void PlayForPhase(PomodoroPhase phase)
    {
        (double freq, int ms)[] tones = phase switch
        {
            PomodoroPhase.Work => new[] { (587.0, 110), (880.0, 160) },              // ascending: focus begins
            PomodoroPhase.ShortBreak => new[] { (880.0, 140), (587.0, 230) },        // falling: focus ended
            PomodoroPhase.LongBreak => new[] { (880.0, 140), (660.0, 140), (494.0, 280) }, // longer fall
            _ => Array.Empty<(double, int)>()
        };
        if (tones.Length == 0) return;
        Play(tones);
    }

    private static void Play((double freq, int ms)[] tones)
    {
        try
        {
            var wav = BuildWav(tones);
            lock (_gate) _pinned = wav; // keep alive for the async read
            // SND_MEMORY | SND_ASYNC | SND_NODEFAULT
            PlaySound(wav, IntPtr.Zero, 0x0004 | 0x0001 | 0x0002);
        }
        catch
        {
            // No audio device / winmm unavailable — a chime is a nicety, never a failure.
        }
    }

    private static byte[] BuildWav((double freq, int ms)[] tones)
    {
        // Render 16-bit mono PCM samples with a short fade on each tone to avoid clicks.
        var samples = new List<short>();
        foreach (var (freq, ms) in tones)
        {
            int count = SampleRate * ms / 1000;
            int fade = Math.Min(count / 2, SampleRate * 6 / 1000); // ~6 ms fade in/out
            for (int i = 0; i < count; i++)
            {
                double env = 1.0;
                if (i < fade) env = (double)i / fade;
                else if (i > count - fade) env = (double)(count - i) / fade;
                double sample = Math.Sin(2 * Math.PI * freq * i / SampleRate) * env * 0.35;
                samples.Add((short)(sample * short.MaxValue));
            }
        }

        int dataBytes = samples.Count * 2;
        using var ms2 = new MemoryStream();
        using var w = new BinaryWriter(ms2);
        // RIFF header
        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + dataBytes);
        w.Write(new[] { 'W', 'A', 'V', 'E' });
        // fmt chunk
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);                    // PCM chunk size
        w.Write((short)1);              // PCM
        w.Write((short)1);              // mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2);        // byte rate (mono, 16-bit)
        w.Write((short)2);              // block align
        w.Write((short)16);             // bits per sample
        // data chunk
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(dataBytes);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return ms2.ToArray();
    }

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(byte[] data, IntPtr hmod, uint flags);
}
