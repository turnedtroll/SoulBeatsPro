namespace SoulBeatsPro;

/// <summary>
/// Reads osu!stable's per-user config file (<c>osu!.&lt;Username&gt;.cfg</c>) to lift the
/// user's mania keybinds directly into the macro. osu!stable stores mania bindings as
/// <c>ManiaNKM = VK</c> lines where N is the key count, M is the 1-based column index,
/// and the value is a Win32 virtual-key code.
/// </summary>
internal static class OsuConfigImporter
{
    /// <summary>Best-effort search for <c>osu!.&lt;User&gt;.cfg</c>. Returns null if nothing found.</summary>
    public static string? FindConfigFile(string? songsPath)
    {
        var tried = new List<string>();
        if (!string.IsNullOrWhiteSpace(songsPath))
        {
            try
            {
                var parent = Directory.GetParent(songsPath)?.FullName;
                if (parent != null) tried.Add(parent);
            }
            catch { }
        }
        tried.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!"));
        tried.Add(@"C:\Program Files (x86)\osu!");
        tried.Add(@"C:\Program Files\osu!");

        foreach (var dir in tried)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var files = Directory.GetFiles(dir, "osu!.*.cfg");
                var latest = files.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (latest != null) return latest;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Parse mania bindings for the requested key count from an osu!stable config file.
    /// Accepts <c>ManiaNKM</c>, <c>ManiaNK_M</c>, and <c>ManiaNK-M</c> variants.
    /// Returns an array of length <paramref name="keyCount"/> populated with this macro's
    /// key-name strings, or null if fewer than <paramref name="keyCount"/> columns were found.
    /// </summary>
    public static string[]? ImportManiaBindings(string configPath, int keyCount)
    {
        if (keyCount < 1 || keyCount > 10) return null;
        if (!File.Exists(configPath)) return null;

        string[] lines;
        try { lines = File.ReadAllLines(configPath); }
        catch { return null; }

        var result = new string[keyCount];
        int filled = 0;
        string prefix = $"Mania{keyCount}K";

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            var keyPart = line[..eq].Trim();
            var valPart = line[(eq + 1)..].Trim();

            // Strip prefix, then leading separator (space / underscore / dash) if any
            var tail = keyPart[prefix.Length..].Trim();
            tail = tail.TrimStart('_', '-', ' ');
            if (!int.TryParse(tail, out int col)) continue;
            if (col < 1 || col > keyCount) continue;

            if (!int.TryParse(valPart, out int vk)) continue;

            int idx = col - 1;
            if (result[idx] != null) continue; // first occurrence wins
            result[idx] = NativeApi.NameFromVk(vk);
            filled++;
        }

        return filled == keyCount ? result : null;
    }
}
