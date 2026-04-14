namespace SoulBeatsPro;

/// <summary>
/// Profile-agnostic accuracy preset. Each profile stores a MaxJudgmentMs
/// (late edge of last non-miss judgment minus safety margin) and the preset
/// determines the fraction of that window used for the random delay.
/// </summary>
internal enum AccuracyPreset
{
    PerfectOnly = 0,    // MaxAccuracy
    MostlyPerfects = 1, // HighAccuracy
    HumanLike = 2,
    Sloppy = 3
}

internal static class AccuracyPresetTable
{
    // Fraction of MaxJudgmentMs used as the upper bound for uniform-random delay.
    private static readonly double[] Fractions = { 0.0, 0.20, 0.50, 0.85 };

    public static double GetMaxDelaySeconds(AccuracyPreset preset, double maxJudgmentMs)
    {
        if (maxJudgmentMs <= 0.0) return 0.0;
        int idx = (int)preset;
        if (idx < 0 || idx >= Fractions.Length) return 0.0;
        return (Fractions[idx] * maxJudgmentMs) / 1000.0;
    }

    /// <summary>Generic labels suitable for any profile.</summary>
    public static string[] GenericLabels => new[]
    {
        "Max accuracy", "High accuracy", "Human-like", "Sloppy"
    };
}
