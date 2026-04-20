namespace SoulBeatsPro;

internal sealed class OsuBeatmap
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Version { get; set; } = "";
    public int KeyCount { get; set; }
    public int Mode { get; set; }
    public double OverallDifficulty { get; set; } = OsuManiaTimingWindows.DefaultOd;
    public List<OsuNote> Notes { get; set; } = new();

    /// <summary>True when the source .osu file was Mode ≠ 3 (e.g. osu!standard) and we
    /// synthesised mania notes via convert. Column placement is approximate — osu!stable's
    /// real convert uses pattern-based column assignment we don't fully replicate.</summary>
    public bool IsConvert { get; set; }
}

internal readonly struct OsuNote
{
    public int Column { get; }
    public int TimeMs { get; }
    public int EndTimeMs { get; }
    public bool IsHold { get; }

    public OsuNote(int column, int timeMs, int endTimeMs, bool isHold)
    {
        Column = column; TimeMs = timeMs; EndTimeMs = endTimeMs; IsHold = isHold;
    }
}

internal static class OsuBeatmapParser
{
    /// <summary>Parse an .osu file as mania. If the file is Mode=0 (osu!standard), synthesise
    /// mania notes via a simple x-position convert using <paramref name="convertKeyCount"/>.</summary>
    public static OsuBeatmap? Parse(string content, int convertKeyCount = 4)
    {
        var beatmap = new OsuBeatmap();
        var pending = new List<StandardHitObject>();
        var lines = content.Split('\n');
        string section = "";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line[1..^1];
                continue;
            }

            switch (section)
            {
                case "General": ParseGeneral(line, beatmap); break;
                case "Metadata": ParseMetadata(line, beatmap); break;
                case "Difficulty": ParseDifficulty(line, beatmap); break;
                case "HitObjects":
                    if (beatmap.Mode == 3) ParseManiaHitObject(line, beatmap);
                    else if (beatmap.Mode == 0) ParseStandardHitObject(line, pending);
                    break;
            }
        }

        if (beatmap.Mode == 0)
        {
            // Convert osu!standard to mania. Circles + sliders become taps at a column
            // chosen from the note's x position. Slider hold duration is skipped (MVP).
            int kc = Math.Clamp(convertKeyCount, 1, 10);
            beatmap.KeyCount = kc;
            beatmap.IsConvert = true;
            foreach (var h in pending)
            {
                int col = Math.Clamp(h.X * kc / 512, 0, kc - 1);
                beatmap.Notes.Add(new OsuNote(col, h.TimeMs, 0, false));
            }
        }
        else if (beatmap.Mode != 3)
        {
            // Taiko / catch — no sensible mania conversion. Reject.
            return null;
        }

        beatmap.Notes.Sort((a, b) =>
        {
            int cmp = a.TimeMs.CompareTo(b.TimeMs);
            return cmp != 0 ? cmp : a.Column.CompareTo(b.Column);
        });

        return beatmap;
    }

    public static OsuBeatmap? ParseFile(string path, int convertKeyCount = 4)
    {
        if (!File.Exists(path)) return null;
        return Parse(File.ReadAllText(path), convertKeyCount);
    }

    private static void ParseGeneral(string line, OsuBeatmap b)
    {
        if (TryGetValue(line, "Mode", out var val) && int.TryParse(val, out int mode))
            b.Mode = mode;
    }

    private static void ParseMetadata(string line, OsuBeatmap b)
    {
        if (TryGetValue(line, "Title", out var title)) b.Title = title;
        else if (TryGetValue(line, "Artist", out var artist)) b.Artist = artist;
        else if (TryGetValue(line, "Version", out var version)) b.Version = version;
    }

    private static void ParseDifficulty(string line, OsuBeatmap b)
    {
        if (TryGetValue(line, "CircleSize", out var csVal)
            && double.TryParse(csVal, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double cs))
        {
            b.KeyCount = (int)Math.Round(cs);
        }
        else if (TryGetValue(line, "OverallDifficulty", out var odVal)
            && double.TryParse(odVal, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double od))
        {
            b.OverallDifficulty = od;
        }
    }

    private static void ParseManiaHitObject(string line, OsuBeatmap b)
    {
        var parts = line.Split(',');
        if (parts.Length < 5) return;
        if (!int.TryParse(parts[0], out int x)) return;
        if (!int.TryParse(parts[2], out int time)) return;
        if (!int.TryParse(parts[3], out int type)) return;

        int keyCount = b.KeyCount > 0 ? b.KeyCount : 4;
        int column = Math.Clamp(x * keyCount / 512, 0, keyCount - 1);
        bool isHold = (type & 128) != 0;

        int endTime = 0;
        if (isHold && parts.Length >= 6)
        {
            var endParts = parts[5].Split(':');
            if (endParts.Length > 0) int.TryParse(endParts[0], out endTime);
        }

        b.Notes.Add(new OsuNote(column, time, endTime, isHold));
    }

    private readonly struct StandardHitObject
    {
        public int X { get; }
        public int TimeMs { get; }
        public int Type { get; }
        public StandardHitObject(int x, int timeMs, int type) { X = x; TimeMs = timeMs; Type = type; }
    }

    private static void ParseStandardHitObject(string line, List<StandardHitObject> pending)
    {
        var parts = line.Split(',');
        if (parts.Length < 5) return;
        if (!int.TryParse(parts[0], out int x)) return;
        if (!int.TryParse(parts[2], out int time)) return;
        if (!int.TryParse(parts[3], out int type)) return;
        // type & 1 = circle, & 2 = slider, & 8 = spinner. Skip spinners (no column sense).
        if ((type & 8) != 0) return;
        pending.Add(new StandardHitObject(x, time, type));
    }

    private static bool TryGetValue(string line, string key, out string value)
    {
        value = "";
        if (!line.StartsWith(key, StringComparison.Ordinal)) return false;
        int colonIdx = line.IndexOf(':');
        if (colonIdx < 0 || colonIdx != key.Length) return false;
        value = line[(colonIdx + 1)..].Trim();
        return true;
    }
}
