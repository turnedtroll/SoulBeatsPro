namespace SoulBeatsPro;

/// <summary>
/// Shared enum for the accuracy preset dropdown. Labels displayed in the UI
/// swap per game (Perfect vs Sick) but the enum values are game-agnostic.
/// </summary>
internal enum AccuracyPreset
{
    PerfectOnly = 0,   // 0 ms delay — current behavior
    MostlyPerfects = 1,
    HumanLike = 2,
    Sloppy = 3
}

/// <summary>
/// Per-game max delay (in milliseconds) applied when scheduling a key press
/// after rising-edge detection. Uniform random in [0, maxDelayMs].
///
/// Values derived from research into RoBeats / Funky Friday timing windows
/// (see 2026-04-13-robeats-detection-design.md). Each max leaves at least
/// 20 ms of safety margin inside the Miss boundary.
/// </summary>
internal static class AccuracyPresetTable
{
    // RoBeats: Perfect 60ms (20e/40l), Great ~+80ms late, Okay ~+130-150ms late, Miss beyond ~+170ms.
    private static readonly double[] RoBeatsMaxMs =
    {
        0.0,     // PerfectOnly
        30.0,    // MostlyPerfects
        70.0,    // HumanLike
        120.0    // Sloppy
    };

    // FNF Psych Engine defaults: Sick ±45ms, Good ±90ms, Bad ±135ms, Shit >±166ms.
    private static readonly double[] FunkyFridayMaxMs =
    {
        0.0,     // Sick-only
        30.0,    // Mostly Sicks
        75.0,    // Human-like
        125.0    // Sloppy
    };

    /// <summary>Max delay in seconds for the preset + game combo.</summary>
    public static double GetMaxDelaySeconds(AccuracyPreset preset, bool whiteGrayMode)
    {
        var table = whiteGrayMode ? FunkyFridayMaxMs : RoBeatsMaxMs;
        int idx = (int)preset;
        if (idx < 0 || idx >= table.Length) return 0.0;
        return table[idx] / 1000.0;
    }

    /// <summary>Per-game UI labels for the dropdown.</summary>
    public static string[] GetLabels(bool whiteGrayMode)
    {
        return whiteGrayMode
            ? new[] { "Sick-only", "Mostly Sicks", "Human-like", "Sloppy" }
            : new[] { "Perfect-only", "Mostly Perfects", "Human-like", "Sloppy" };
    }
}
