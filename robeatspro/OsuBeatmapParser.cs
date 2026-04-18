namespace SoulBeatsPro;

internal sealed class OsuBeatmap
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Version { get; set; } = "";
    public int KeyCount { get; set; }
    public int Mode { get; set; }
    public List<OsuNote> Notes { get; set; } = new();
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
    public static OsuBeatmap? Parse(string content)
    {
        var beatmap = new OsuBeatmap();
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
                case "HitObjects": ParseHitObject(line, beatmap); break;
            }
        }

        if (beatmap.Mode != 3) return null;

        beatmap.Notes.Sort((a, b) =>
        {
            int cmp = a.TimeMs.CompareTo(b.TimeMs);
            return cmp != 0 ? cmp : a.Column.CompareTo(b.Column);
        });

        return beatmap;
    }

    public static OsuBeatmap? ParseFile(string path)
    {
        if (!File.Exists(path)) return null;
        return Parse(File.ReadAllText(path));
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
        if (TryGetValue(line, "CircleSize", out var val) && int.TryParse(val, out int cs))
            b.KeyCount = cs;
    }

    private static void ParseHitObject(string line, OsuBeatmap b)
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
