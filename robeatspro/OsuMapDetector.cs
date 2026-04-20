namespace SoulBeatsPro;

internal static class OsuMapDetector
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, OsuBeatmap> _cache = new();

    public static (string artist, string title, string difficulty)? ParseWindowTitle(string windowTitle)
    {
        const string prefix = "osu!";
        if (!windowTitle.StartsWith(prefix)) return null;

        int firstDash = windowTitle.IndexOf(" - ", prefix.Length, StringComparison.Ordinal);
        if (firstDash < 0) return null;

        string afterPrefix = windowTitle[(firstDash + 3)..];

        int bracketOpen = afterPrefix.LastIndexOf('[');
        int bracketClose = afterPrefix.LastIndexOf(']');
        if (bracketOpen < 0 || bracketClose <= bracketOpen) return null;

        string difficulty = afterPrefix[(bracketOpen + 1)..bracketClose].Trim();
        string artistAndTitle = afterPrefix[..bracketOpen].Trim();

        int titleDash = artistAndTitle.IndexOf(" - ", StringComparison.Ordinal);
        if (titleDash < 0) return null;

        string artist = artistAndTitle[..titleDash].Trim();
        string title = artistAndTitle[(titleDash + 3)..].Trim();

        if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(difficulty))
            return null;

        return (artist, title, difficulty);
    }

    public static bool MatchesBeatmap(OsuBeatmap beatmap, string artist, string title, string difficulty)
    {
        return string.Equals(beatmap.Artist, artist, StringComparison.OrdinalIgnoreCase)
            && string.Equals(beatmap.Title, title, StringComparison.OrdinalIgnoreCase)
            && string.Equals(beatmap.Version, difficulty, StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetOsuWindowTitle()
    {
        string? found = null;
        NativeApi.EnumWindows((hWnd, _) =>
        {
            if (!NativeApi.IsWindowVisible(hWnd)) return true;
            int len = NativeApi.GetWindowTextLength(hWnd);
            if (len <= 0) return true;
            var buf = new char[len + 1];
            NativeApi.GetWindowText(hWnd, buf, buf.Length);
            var title = new string(buf, 0, len);
            if (title.StartsWith("osu!", StringComparison.OrdinalIgnoreCase) && title.Contains(" - "))
            {
                found = title;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static (OsuBeatmap? beatmap, string status) Detect(string songsPath, int convertKeyCount = 4)
    {
        var windowTitle = GetOsuWindowTitle();
        if (windowTitle == null)
            return (null, "osu! window not found — is osu! running?");

        var parsed = ParseWindowTitle(windowTitle);
        if (parsed == null)
            return (null, "No beatmap selected in osu! — select a map first");

        var (artist, title, difficulty) = parsed.Value;

        if (!Directory.Exists(songsPath))
            return (null, $"Songs folder not found: {songsPath}");

        // Two passes: first try folders whose name matches artist/title, then everything else
        var allDirs = Directory.GetDirectories(songsPath);
        var matchingDirs = new List<string>();
        var otherDirs = new List<string>();
        foreach (var d in allDirs)
        {
            var dirName = Path.GetFileName(d);
            if (dirName.Contains(artist, StringComparison.OrdinalIgnoreCase)
                || dirName.Contains(title, StringComparison.OrdinalIgnoreCase))
                matchingDirs.Add(d);
            else
                otherDirs.Add(d);
        }

        foreach (var songDir in matchingDirs.Concat(otherDirs))
        {
            foreach (var osuFile in Directory.GetFiles(songDir, "*.osu"))
            {
                string cacheKey = $"{osuFile}|{convertKeyCount}";
                if (_cache.TryGetValue(cacheKey, out var cached))
                {
                    if (MatchesBeatmap(cached, artist, title, difficulty))
                        return (cached, FormatDetectedStatus(cached, artist, title, difficulty));
                    continue;
                }

                var beatmap = OsuBeatmapParser.ParseFile(osuFile, convertKeyCount);
                if (beatmap == null) continue;

                _cache[cacheKey] = beatmap;

                if (MatchesBeatmap(beatmap, artist, title, difficulty))
                    return (beatmap, FormatDetectedStatus(beatmap, artist, title, difficulty));
            }
        }

        return (null, $"Could not find beatmap: {artist} - {title} [{difficulty}]");
    }

    private static string FormatDetectedStatus(OsuBeatmap b, string artist, string title, string difficulty)
    {
        string conv = b.IsConvert ? " [convert]" : "";
        return $"Detected: {artist} - {title} [{difficulty}] ({b.KeyCount}K){conv}";
    }

    public static string DefaultSongsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!", "Songs");

    public static void ClearCache() => _cache.Clear();
}
