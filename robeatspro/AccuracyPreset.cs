namespace SoulBeatsPro;

/// <summary>
/// Profile-agnostic accuracy preset. Each preset maps to a (mean, stddev, clampMax)
/// Gaussian in milliseconds, tuned against Funky Friday's judgment windows (Sick ±50,
/// Good ±90, Bad ±135). Delay is always positive (press-after-detect); for symmetric
/// early/late play, the user should calibrate the crosshair slightly above the
/// receptor center so 0 delay ≈ early Sick.
/// </summary>
internal enum AccuracyPreset
{
    PerfectOnly = 0,    // Always 0 delay — always Sick.
    MostlyPerfects = 1, // ~95% Sick.
    HumanLike = 2,      // Mostly Sick, some Good.
    Sloppy = 3          // Sick + Good, rare Bad, effectively no Miss.
}

internal readonly struct PresetTuning
{
    public double MeanMs { get; }
    public double StdDevMs { get; }
    public double ClampMaxMs { get; }

    public PresetTuning(double meanMs, double stdDevMs, double clampMaxMs)
    {
        MeanMs = meanMs; StdDevMs = stdDevMs; ClampMaxMs = clampMaxMs;
    }
}

internal static class AccuracyPresetTable
{
    // (mean, stddev, clampMax) in milliseconds — see class doc for rationale.
    private static readonly PresetTuning[] Tunings =
    {
        new(0,  0,  0),
        new(10, 12, 40),
        new(25, 20, 75),
        new(35, 25, 95)
    };

    // Bidirectional jitter parameters for osu!mania (beatmap-file) mode.
    // StdDevMs is the Gaussian spread; ClampFrac is the fraction of the
    // Marvelous (300g) window the press is allowed to drift from the true note time.
    // At OsuManiaTimingWindows.MarvelousMs = 16.5, a ClampFrac of 1.0 = ±16.5 ms.
    // Every preset stays strictly inside the Marvelous window so hits never drop below 300g.
    private readonly record struct BidirectionalTuning(double StdDevMs, double ClampFrac);

    private static readonly BidirectionalTuning[] Bidirectional =
    {
        new(0.0, 0.00), // PerfectOnly    — no jitter, robotic.
        new(1.5, 0.35), // MostlyPerfects — ~1/3 Marvelous window, barely noticeable drift.
        new(3.5, 0.65), // HumanLike      — ~2/3 Marvelous window, visible human cadence + headroom.
        new(5.0, 0.90), // Sloppy         — near-full Marvelous window, still always 300g.
    };

    public static PresetTuning GetTuning(AccuracyPreset preset)
    {
        int idx = (int)preset;
        if (idx < 0 || idx >= Tunings.Length) return Tunings[0];
        return Tunings[idx];
    }

    /// <summary>
    /// Upper bound on sampled delay in seconds. Used by the engine to skip the
    /// scheduling path entirely when the preset is PerfectOnly, and by UI hints.
    /// </summary>
    public static double GetMaxDelaySeconds(AccuracyPreset preset, double maxJudgmentMs)
    {
        if (maxJudgmentMs <= 0.0) return 0.0;
        var t = GetTuning(preset);
        // Clamp against the profile's judgment window too (safety margin).
        double capMs = Math.Min(t.ClampMaxMs, maxJudgmentMs);
        return capMs / 1000.0;
    }

    /// <summary>
    /// Sample a per-press delay in seconds from the preset's Gaussian,
    /// clamped to [0, ClampMaxMs] and also capped at maxJudgmentMs.
    /// </summary>
    public static double SampleDelaySeconds(AccuracyPreset preset, double maxJudgmentMs, Random rng)
    {
        if (maxJudgmentMs <= 0.0) return 0.0;
        var t = GetTuning(preset);
        if (t.ClampMaxMs <= 0.0) return 0.0;

        // Box-Muller for a standard-normal sample.
        double u1 = 1.0 - rng.NextDouble(); // (0,1]
        double u2 = 1.0 - rng.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

        double ms = t.MeanMs + z * t.StdDevMs;
        double cap = Math.Min(t.ClampMaxMs, maxJudgmentMs);
        if (ms < 0) ms = 0;
        if (ms > cap) ms = cap;
        return ms / 1000.0;
    }

    /// <summary>
    /// Signed jitter in ms sampled from a narrow Gaussian (bidirectional — can be early or late).
    /// Used by osu!mania (beatmap-file) mode where note times are known in advance, so presses
    /// can scatter around the target without ever missing. The bound is derived from the
    /// Marvelous (300g) window (<see cref="OsuManiaTimingWindows.MarvelousMs"/>), so even the
    /// widest preset stays strictly inside 300g.
    /// </summary>
    public static double SampleJitterMsBidirectional(AccuracyPreset preset, double maxJudgmentMs, Random rng)
    {
        int idx = (int)preset;
        if (idx < 0 || idx >= Bidirectional.Length) idx = 0;
        var t = Bidirectional[idx];
        if (t.StdDevMs <= 0.0 || t.ClampFrac <= 0.0) return 0.0;

        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

        double ms = z * t.StdDevMs;

        // Cap is a fraction of the Marvelous (300g) window. Shave 0.5ms safety margin
        // so we never sample exactly at the window edge (rounding/latency could bump over).
        double cap = OsuManiaTimingWindows.MarvelousMs * t.ClampFrac - 0.5;
        if (cap < 0) cap = 0;

        // If the profile sets a tighter judgment window (e.g. another rhythm game using
        // this preset), honor it via a half-window safety limit.
        if (maxJudgmentMs > 0.0) cap = Math.Min(cap, maxJudgmentMs / 2.0);

        if (ms >  cap) ms =  cap;
        if (ms < -cap) ms = -cap;
        return ms;
    }

    /// <summary>Generic labels suitable for any profile.</summary>
    public static string[] GenericLabels => new[]
    {
        "Max accuracy", "High accuracy", "Human-like", "Sloppy"
    };
}
