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

    /// <summary>Generic labels suitable for any profile.</summary>
    public static string[] GenericLabels => new[]
    {
        "Max accuracy", "High accuracy", "Human-like", "Sloppy"
    };
}
