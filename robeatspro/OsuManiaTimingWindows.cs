namespace SoulBeatsPro;

/// <summary>osu!mania judgment tiers, widest (best) to narrowest (miss).</summary>
internal enum OsuManiaJudgment
{
    Marvelous, // 300g — "MAX" / Rainbow 300
    Perfect,   // 300
    Great,     // 200
    Good,      // 100
    OK,        // 50
    Miss
}

/// <summary>
/// Hit-window lookups for osu!mania (stable / ScoreV1). Values are the
/// half-widths in milliseconds around the note's exact time, sourced from the
/// osu! wiki. The Marvelous (300g) window is a constant 16.5 ms; every other
/// judgment is a linear function of OverallDifficulty (OD).
///
/// At OD 8 (a common osu!mania OD) the windows are roughly:
///   Marvelous 16.5ms, Perfect 40ms, Great 73ms, Good 103ms, OK 127ms, Miss 164ms.
/// </summary>
internal static class OsuManiaTimingWindows
{
    /// <summary>Marvelous (300g / MAX) half-window in ms — constant across all ODs.</summary>
    public const double MarvelousMs = 16.5;

    /// <summary>Default OD used when a beatmap's OverallDifficulty is unknown.</summary>
    public const double DefaultOd = 8.0;

    /// <summary>± ms half-window for the given judgment at the given OverallDifficulty.</summary>
    public static double GetWindowMs(OsuManiaJudgment j, double od)
    {
        // Clamp OD to osu!'s 0–10 range — anything outside is malformed.
        if (od < 0) od = 0;
        if (od > 10) od = 10;

        return j switch
        {
            OsuManiaJudgment.Marvelous => MarvelousMs,
            OsuManiaJudgment.Perfect   =>  64.0 - 3.0 * od,
            OsuManiaJudgment.Great     =>  97.0 - 3.0 * od,
            OsuManiaJudgment.Good      => 127.0 - 3.0 * od,
            OsuManiaJudgment.OK        => 151.0 - 3.0 * od,
            OsuManiaJudgment.Miss      => 188.0 - 3.0 * od,
            _ => 0.0
        };
    }
}
