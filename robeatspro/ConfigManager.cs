using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoulBeatsPro;

// ── Settings POCOs ──────────────────────────────────────────────

internal sealed class KeybindSettings
{
    [JsonPropertyName("lane1")] public string Lane1 { get; set; } = "Z";
    [JsonPropertyName("lane2")] public string Lane2 { get; set; } = "X";
    [JsonPropertyName("lane3")] public string Lane3 { get; set; } = "OEM_COMMA";
    [JsonPropertyName("lane4")] public string Lane4 { get; set; } = "OEM_PERIOD";
    [JsonPropertyName("pause")] public string Pause { get; set; } = "L";
    [JsonPropertyName("debug")] public string Debug { get; set; } = "P";
    [JsonPropertyName("screenshot")] public string Screenshot { get; set; } = "F2";
    [JsonPropertyName("quit")] public string Quit { get; set; } = "ESCAPE";

    [JsonIgnore] public string[] LaneKeys => [Lane1, Lane2, Lane3, Lane4];

    public void SetLane(int index, string key)
    {
        switch (index)
        {
            case 0: Lane1 = key; break;
            case 1: Lane2 = key; break;
            case 2: Lane3 = key; break;
            case 3: Lane4 = key; break;
        }
    }

    public KeybindSettings Clone() => new()
    {
        Lane1 = Lane1, Lane2 = Lane2, Lane3 = Lane3, Lane4 = Lane4,
        Pause = Pause, Debug = Debug, Screenshot = Screenshot, Quit = Quit
    };
}

internal sealed class TuningSettings
{
    [JsonPropertyName("sampleHalf")] public int SampleHalf { get; set; } = 3;
    [JsonPropertyName("minPixels")] public int MinPixels { get; set; } = 3;
    [JsonPropertyName("tapKeyDuration")] public double TapKeyDuration { get; set; } = 0.03;
    [JsonPropertyName("holdReleaseCooldown")] public double HoldReleaseCooldown { get; set; } = 0.06;
    [JsonPropertyName("toggleDelay")] public double ToggleDelay { get; set; } = 0.3;
    [JsonPropertyName("holdArmGrace")] public double HoldArmGrace { get; set; } = 0.20;
    [JsonPropertyName("holdReleaseGrace")] public double HoldReleaseGrace { get; set; } = 0.01;
    [JsonPropertyName("cleanFrames")] public int CleanFrames { get; set; } = 3;
    [JsonPropertyName("minPressDurationMs")] public double MinPressDurationMs { get; set; } = 20.0;

    public void Reset()
    {
        var d = new TuningSettings();
        SampleHalf = d.SampleHalf; MinPixels = d.MinPixels;
        TapKeyDuration = d.TapKeyDuration; HoldReleaseCooldown = d.HoldReleaseCooldown;
        ToggleDelay = d.ToggleDelay; HoldArmGrace = d.HoldArmGrace;
        HoldReleaseGrace = d.HoldReleaseGrace;
        CleanFrames = d.CleanFrames; MinPressDurationMs = d.MinPressDurationMs;
    }
}

internal sealed class ThemeSettings
{
    [JsonPropertyName("windowBg")] public string WindowBg { get; set; } = "#1E1E2E";
    [JsonPropertyName("buttonFace")] public string ButtonFace { get; set; } = "#3A3A50";
    [JsonPropertyName("buttonHighlight")] public string ButtonHighlight { get; set; } = "#4A4A60";
    [JsonPropertyName("buttonShadow")] public string ButtonShadow { get; set; } = "#15152A";
    [JsonPropertyName("buttonText")] public string ButtonText { get; set; } = "#FFFFFF";
    [JsonPropertyName("titleBar")] public string TitleBar { get; set; } = "#151525";
    [JsonPropertyName("titleText")] public string TitleText { get; set; } = "#FFFFFF";
    [JsonPropertyName("accentColor")] public string AccentColor { get; set; } = "#4CAF50";
    [JsonPropertyName("textColor")] public string TextColor { get; set; } = "#E0E0E8";
    [JsonPropertyName("panelBg")] public string PanelBg { get; set; } = "#2D2D40";
    [JsonPropertyName("tabBg")] public string TabBg { get; set; } = "#252535";
    [JsonPropertyName("fontName")] public string FontName { get; set; } = "Segoe UI";
    [JsonPropertyName("fontSize")] public int FontSize { get; set; } = 9;
    [JsonPropertyName("backgroundImage")] public string BackgroundImage { get; set; } = "";
    [JsonPropertyName("formOpacity")] public int FormOpacity { get; set; } = 100;
    [JsonPropertyName("panelAlpha")] public int PanelAlpha { get; set; } = 255;

    public Color GetColor(string hex)
    {
        try { return ColorTranslator.FromHtml(hex); }
        catch { return Color.Gray; }
    }

    public Color GetWindowBg() => GetColor(WindowBg);
    public Color GetButtonFace() => GetColor(ButtonFace);
    public Color GetButtonHighlight() => GetColor(ButtonHighlight);
    public Color GetButtonShadow() => GetColor(ButtonShadow);
    public Color GetButtonText() => GetColor(ButtonText);
    public Color GetTitleBar() => GetColor(TitleBar);
    public Color GetTitleText() => GetColor(TitleText);
    public Color GetAccentColor() => GetColor(AccentColor);
    public Color GetTextColor() => GetColor(TextColor);
    public Color GetPanelBg() => GetColor(PanelBg);
    public Color GetTabBg() => GetColor(TabBg);

    /// Tab background with alpha applied.
    public Color GetTabBgAlpha()
    {
        var c = GetTabBg();
        return Color.FromArgb(Math.Clamp(PanelAlpha, 0, 255), c.R, c.G, c.B);
    }

    public Font GetFont() => new(FontName, FontSize);
    public Font GetFont(float size) => new(FontName, size);
    public Font GetFont(FontStyle style) => new(FontName, FontSize, style);

    public void Reset()
    {
        var d = new ThemeSettings();
        WindowBg = d.WindowBg; ButtonFace = d.ButtonFace; ButtonHighlight = d.ButtonHighlight;
        ButtonShadow = d.ButtonShadow; ButtonText = d.ButtonText; TitleBar = d.TitleBar;
        TitleText = d.TitleText; AccentColor = d.AccentColor; TextColor = d.TextColor;
        PanelBg = d.PanelBg; TabBg = d.TabBg; FontName = d.FontName; FontSize = d.FontSize;
        BackgroundImage = d.BackgroundImage; FormOpacity = d.FormOpacity; PanelAlpha = d.PanelAlpha;
    }
}

internal sealed class GameModeSettings
{
    [JsonPropertyName("activeProfileName")] public string ActiveProfileName { get; set; } = "Funky Friday";
    /// <summary>Legacy: old "activeGame" key. Read-only, used during migration.</summary>
    [JsonPropertyName("activeGame")] public string? LegacyActiveGame { get; set; }

    /// <summary>TEMP shim for Tasks 7–11 consumers. Maps old "funkyFriday"/"robeats" keys
    /// to/from the new ActiveProfileName. Not serialized.</summary>
    [JsonIgnore]
    public string ActiveGame
    {
        get => ActiveProfileName == "RoBeats" ? "robeats" : "funkyFriday";
        set => ActiveProfileName = value == "robeats" ? "RoBeats" : "Funky Friday";
    }
}

internal sealed class AppSettings
{
    [JsonPropertyName("keybinds")] public KeybindSettings Keybinds { get; set; } = new();
    [JsonPropertyName("theme")] public ThemeSettings Theme { get; set; } = new();
    [JsonPropertyName("gameMode")] public GameModeSettings GameMode { get; set; } = new();
    [JsonPropertyName("profiles")] public List<Profile> Profiles { get; set; } = new();

    [JsonPropertyName("detection")] public JsonElement? LegacyDetection { get; set; }
    [JsonPropertyName("tuning")]    public TuningSettings? LegacyTuning { get; set; }
    [JsonPropertyName("profiles_legacy_twoProfile")]
    public JsonElement? LegacyTwoProfile { get; set; }

    public bool Migrate()
    {
        bool changed = false;

        if (string.IsNullOrEmpty(GameMode.ActiveProfileName) || GameMode.ActiveProfileName == "Funky Friday")
        {
            if (!string.IsNullOrEmpty(GameMode.LegacyActiveGame))
            {
                GameMode.ActiveProfileName = GameMode.LegacyActiveGame == "robeats" ? "RoBeats" : "Funky Friday";
                GameMode.LegacyActiveGame = null;
                changed = true;
            }
        }

        if (LegacyTwoProfile.HasValue && Profiles.Count == 0)
        {
            MigrateTwoProfile(LegacyTwoProfile.Value);
            LegacyTwoProfile = null;
            changed = true;
        }

        if ((LegacyDetection.HasValue || LegacyTuning != null) && Profiles.Count == 0)
        {
            SeedBuiltInProfiles();
            ApplyLegacyFlat();
            LegacyDetection = null;
            LegacyTuning = null;
            changed = true;
        }

        if (Profiles.Count == 0)
        {
            SeedBuiltInProfiles();
            changed = true;
        }

        if (GameMode.LegacyActiveGame != null)
        {
            GameMode.LegacyActiveGame = null;
            changed = true;
        }

        return changed;
    }

    private void SeedBuiltInProfiles()
    {
        Profiles.Add(new Profile { Name = "Funky Friday", IsBuiltIn = true, MaxJudgmentMs = 140 });
        Profiles.Add(new Profile { Name = "RoBeats",      IsBuiltIn = true, MaxJudgmentMs = 150 });
        Profiles.Add(new Profile
        {
            Name = "osu!mania",
            IsBuiltIn = true,
            MaxJudgmentMs = 100,
            DetectionMode = DetectionMode.BeatmapFile,
            ManiaKeys = ["D", "F", "J", "K", "S", "D", "F", "SPACE", "J", "K"]
        });
    }

    private void MigrateTwoProfile(JsonElement el)
    {
        if (el.TryGetProperty("funkyFriday", out var ff))
            Profiles.Add(BuildFromLegacyTwoProfile("Funky Friday", ff, whiteGray: true, maxJudgmentMs: 140));
        if (el.TryGetProperty("robeats", out var rb))
            Profiles.Add(BuildFromLegacyTwoProfile("RoBeats", rb, whiteGray: false, maxJudgmentMs: 150));
        GameMode.ActiveProfileName = GameMode.LegacyActiveGame == "robeats" ? "RoBeats" : "Funky Friday";
    }

    private static Profile BuildFromLegacyTwoProfile(string name, JsonElement src, bool whiteGray, double maxJudgmentMs)
    {
        var p = new Profile { Name = name, IsBuiltIn = true, MaxJudgmentMs = maxJudgmentMs };

        var sig = new ColorSignature();
        if (whiteGray && src.TryGetProperty("detection", out var d) && d.TryGetProperty("whiteGray", out var wg))
        {
            int whiteMin = wg.TryGetProperty("whiteMin", out var wm) ? wm.GetInt32() : 240;
            int grayMin  = wg.TryGetProperty("grayMin",  out var gn) ? gn.GetInt32() : 130;
            int grayMax  = wg.TryGetProperty("grayMax",  out var gx) ? gx.GetInt32() : 170;
            int whiteMid = (whiteMin + 255) / 2;
            int grayMid  = (grayMin + grayMax) / 2;
            int grayHalfRange = (grayMax - grayMin) / 2;
            sig.Entries.Add(new ColorSignatureEntry(whiteMid, whiteMid, whiteMid, 255 - whiteMid));
            sig.Entries.Add(new ColorSignatureEntry(grayMid, grayMid, grayMid, Math.Max(grayHalfRange, 8)));
        }
        else if (!whiteGray && src.TryGetProperty("detection", out var d2))
        {
            if (d2.TryGetProperty("noteColor", out var nc))
            {
                int r = nc.TryGetProperty("pickedR", out var pr) && pr.GetInt32() >= 0 ? pr.GetInt32() : 255;
                int g = nc.TryGetProperty("pickedG", out var pg) && pg.GetInt32() >= 0 ? pg.GetInt32() : 215;
                int b = nc.TryGetProperty("pickedB", out var pb) && pb.GetInt32() >= 0 ? pb.GetInt32() : 0;
                sig.Entries.Add(new ColorSignatureEntry(r, g, b, 15));
            }
            if (d2.TryGetProperty("holdColor", out var hc))
            {
                int r = hc.TryGetProperty("pickedR", out var pr) && pr.GetInt32() >= 0 ? pr.GetInt32() : 160;
                int g = hc.TryGetProperty("pickedG", out var pg) && pg.GetInt32() >= 0 ? pg.GetInt32() : 120;
                int b = hc.TryGetProperty("pickedB", out var pb) && pb.GetInt32() >= 0 ? pb.GetInt32() : 40;
                sig.Entries.Add(new ColorSignatureEntry(r, g, b, 25));
            }
        }
        p.Signatures = new[] { Clone(sig), Clone(sig), Clone(sig), Clone(sig) };

        if (src.TryGetProperty("tap", out var tap))   p.Tap  = JsonSerializer.Deserialize<int[][]>(tap.GetRawText()) ?? Array.Empty<int[]>();
        if (src.TryGetProperty("hold", out var hold)) p.Hold = JsonSerializer.Deserialize<int[][]>(hold.GetRawText()) ?? Array.Empty<int[]>();
        if (src.TryGetProperty("tuning", out var t))  p.Tuning = JsonSerializer.Deserialize<TuningSettings>(t.GetRawText()) ?? new();
        if (src.TryGetProperty("accuracyPreset", out var ap)) p.AccuracyPreset = (AccuracyPreset)ap.GetInt32();

        return p;
    }

    private static ColorSignature Clone(ColorSignature s)
    {
        var c = new ColorSignature();
        foreach (var e in s.Entries) c.Entries.Add(new ColorSignatureEntry(e.R, e.G, e.B, e.Tolerance));
        return c;
    }

    private void ApplyLegacyFlat()
    {
        var target = Profiles.Find(p => p.Name == GameMode.ActiveProfileName) ?? Profiles[0];
        if (LegacyTuning != null) target.Tuning = LegacyTuning;
    }
}

// ── ConfigManager (singleton) ───────────────────────────────────

internal sealed class ConfigManager
{
    // ── Shared background image cache ──────────────────────────────
    private static Image? _cachedBgImage;
    public static Image? CachedBackgroundImage => _cachedBgImage;

    public static void ReloadBackgroundImage()
    {
        _cachedBgImage?.Dispose();
        _cachedBgImage = null;

        var path = Instance.Theme.BackgroundImage;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { _cachedBgImage = Image.FromFile(path); }
            catch { _cachedBgImage = null; }
        }
    }

    public static void DisposeBackgroundImage()
    {
        _cachedBgImage?.Dispose();
        _cachedBgImage = null;
    }
    public static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "bigbart");

    public static string SettingsPath => Path.Combine(ConfigDir, "settings.json");
    public static string CoordsPath => Path.Combine(ConfigDir, "coords.json");
    public static string ScreenshotsDir => Path.Combine(ConfigDir, "screenshots");
    public static string BackgroundsDir => Path.Combine(ConfigDir, "backgrounds");

    public static ConfigManager Instance { get; } = new();

    public KeybindSettings Keybinds => _settings.Keybinds;
    public ThemeSettings Theme => _settings.Theme;
    public GameModeSettings GameMode => _settings.GameMode;
    public List<Profile> Profiles => _settings.Profiles;

    public Profile ActiveProfile
    {
        get
        {
            var p = _settings.Profiles.Find(x => x.Name == _settings.GameMode.ActiveProfileName);
            if (p != null) return p;
            if (_settings.Profiles.Count == 0)
            {
                _settings.Profiles.Add(new Profile { Name = "Default", IsBuiltIn = false });
            }
            return _settings.Profiles[0];
        }
    }

    public TuningSettings Tuning => ActiveProfile.Tuning;

    /// <summary>
    /// Raised whenever a signature mutation has been persisted. ColorsTab and any
    /// other live consumers should re-render in response. SaveSettings() does NOT
    /// raise this — only the explicit signature-mutating call sites should.
    /// </summary>
    public event Action? ProfileSignaturesChanged;

    public void NotifySignaturesChanged() => ProfileSignaturesChanged?.Invoke();

    private AppSettings _settings = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private ConfigManager()
    {
        EnsureDirectories();
        LoadSettings();
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(ScreenshotsDir);
        Directory.CreateDirectory(BackgroundsDir);

        // Hide the config folder from casual browsing
        try
        {
            var di = new DirectoryInfo(ConfigDir);
            di.Attributes |= FileAttributes.Hidden | FileAttributes.System;
        }
        catch { }
    }

    // ── Settings ────────────────────────────────────────────────

    public void LoadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            _settings = new AppSettings();
            _settings.Migrate();
            SaveSettings();
            return;
        }
        try
        {
            var json = File.ReadAllText(SettingsPath);
            _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            var changed = _settings.Migrate();
            if (File.Exists(CoordsPath))
            {
                try { File.Delete(CoordsPath); } catch { }
                changed = true;
            }
            if (changed) SaveSettings();
        }
        catch { _settings = new AppSettings(); _settings.Migrate(); SaveSettings(); }
    }

    public void SaveSettings()
    {
        var json = JsonSerializer.Serialize(_settings, JsonOpts);
        var tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, SettingsPath, overwrite: true);
    }

    /// <summary>
    /// Serializes the current in-memory settings as JSON. Used by the admin
    /// "request_config" command to pull a live snapshot of a client's config
    /// without touching the on-disk copy.
    /// </summary>
    public string GetSettingsSnapshot()
        => JsonSerializer.Serialize(_settings, JsonOpts);

    // ── Coords ──────────────────────────────────────────────────

    // Default coordinates are computed relative to screen resolution
    // so they work on any monitor size, not just 1920x1080.
    // Proportions based on original 1920x1080 reference layout:
    private static readonly double[] TapXRatios = [0.3750, 0.4588, 0.5411, 0.6250];
    private static readonly double TapYRatio = 0.8796;
    private static readonly double[] HoldXRatios = [0.3750, 0.4573, 0.5417, 0.6234];
    private static readonly double HoldYRatio = 0.7630;

    public static (Point[] tap, Point[] hold) GetDefaultCoords()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        int w = bounds.Width, h = bounds.Height;

        var tap = new Point[4];
        var hold = new Point[4];
        int tapY = (int)(h * TapYRatio);
        int holdY = (int)(h * HoldYRatio);

        for (int i = 0; i < 4; i++)
        {
            tap[i] = new Point((int)(w * TapXRatios[i]), tapY);
            hold[i] = new Point((int)(w * HoldXRatios[i]), holdY);
        }

        return (tap, hold);
    }

    public (Point[] tap, Point[] hold) LoadCoords()
    {
        var p = ActiveProfile;
        if (p.Tap.Length != 4 || p.Hold.Length != 4)
            return GetDefaultCoords();

        try
        {
            var tap  = new Point[4];
            var hold = new Point[4];
            for (int i = 0; i < 4; i++)
            {
                tap[i]  = new Point(p.Tap[i][0],  p.Tap[i][1]);
                hold[i] = new Point(p.Hold[i][0], p.Hold[i][1]);
            }
            return (tap, hold);
        }
        catch { return GetDefaultCoords(); }
    }

    public void SaveCoords(Point[] tap, Point[] hold)
    {
        var p = ActiveProfile;
        p.Tap  = tap.Select(pt => new[] { pt.X, pt.Y }).ToArray();
        p.Hold = hold.Select(pt => new[] { pt.X, pt.Y }).ToArray();
        SaveSettings();
    }

    // ── Screenshots ─────────────────────────────────────────────

    public string SaveScreenshot(Bitmap bmp)
    {
        var path = Path.Combine(ScreenshotsDir,
            $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    // ── Profile management ──────────────────────────────────────

    public Profile AddProfile(string name)
    {
        if (Profiles.Any(p => p.Name == name))
            throw new InvalidOperationException($"Profile '{name}' already exists");
        var p = new Profile { Name = name, IsBuiltIn = false };
        Profiles.Add(p);
        SaveSettings();
        return p;
    }

    public void DeleteProfile(string name)
    {
        var p = Profiles.FirstOrDefault(x => x.Name == name);
        if (p == null) return;
        if (p.IsBuiltIn) throw new InvalidOperationException("Cannot delete a built-in profile");
        Profiles.Remove(p);
        if (_settings.GameMode.ActiveProfileName == name && Profiles.Count > 0)
            _settings.GameMode.ActiveProfileName = Profiles[0].Name;
        SaveSettings();
    }

    public Profile DuplicateProfile(string name, string newName)
    {
        var src = Profiles.First(p => p.Name == name);
        var json = JsonSerializer.Serialize(src);
        var copy = JsonSerializer.Deserialize<Profile>(json)!;
        copy.Name = newName;
        copy.IsBuiltIn = false;
        Profiles.Add(copy);
        SaveSettings();
        return copy;
    }

    public void SetActiveProfile(string name)
    {
        if (!Profiles.Any(p => p.Name == name)) return;
        _settings.GameMode.ActiveProfileName = name;
        SaveSettings();
    }
}

