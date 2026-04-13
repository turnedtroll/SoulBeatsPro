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

internal sealed class NoteColorSettings
{
    [JsonPropertyName("minR")] public int MinR { get; set; } = 200;
    [JsonPropertyName("minG")] public int MinG { get; set; } = 180;
    [JsonPropertyName("maxB")] public int MaxB { get; set; } = 80;
    // Actual picked color (for swatch display; -1 = not set)
    [JsonPropertyName("pickedR")] public int PickedR { get; set; } = -1;
    [JsonPropertyName("pickedG")] public int PickedG { get; set; } = -1;
    [JsonPropertyName("pickedB")] public int PickedB { get; set; } = -1;
}

internal sealed class HoldColorSettings
{
    [JsonPropertyName("minR")] public int MinR { get; set; } = 120;
    [JsonPropertyName("maxR")] public int MaxR { get; set; } = 200;
    [JsonPropertyName("minG")] public int MinG { get; set; } = 100;
    [JsonPropertyName("maxG")] public int MaxG { get; set; } = 180;
    [JsonPropertyName("maxB")] public int MaxB { get; set; } = 80;
    [JsonPropertyName("minRG")] public int MinRG { get; set; } = 230;
    // Actual picked color (for swatch display; -1 = not set)
    [JsonPropertyName("pickedR")] public int PickedR { get; set; } = -1;
    [JsonPropertyName("pickedG")] public int PickedG { get; set; } = -1;
    [JsonPropertyName("pickedB")] public int PickedB { get; set; } = -1;
}

internal sealed class WhiteGraySettings
{
    [JsonPropertyName("whiteMin")] public int WhiteMin { get; set; } = 240;
    [JsonPropertyName("grayMin")] public int GrayMin { get; set; } = 130;
    [JsonPropertyName("grayMax")] public int GrayMax { get; set; } = 170;
}

internal sealed class DetectionSettings
{
    [JsonPropertyName("noteColor")] public NoteColorSettings NoteColor { get; set; } = new();
    [JsonPropertyName("holdColor")] public HoldColorSettings HoldColor { get; set; } = new();
    [JsonPropertyName("whiteGray")] public WhiteGraySettings WhiteGray { get; set; } = new();
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

    public void Reset()
    {
        var d = new TuningSettings();
        SampleHalf = d.SampleHalf; MinPixels = d.MinPixels;
        TapKeyDuration = d.TapKeyDuration; HoldReleaseCooldown = d.HoldReleaseCooldown;
        ToggleDelay = d.ToggleDelay; HoldArmGrace = d.HoldArmGrace;
        HoldReleaseGrace = d.HoldReleaseGrace;
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
    [JsonPropertyName("activeGame")] public string ActiveGame { get; set; } = "funkyFriday";
}

internal sealed class GameProfile
{
    [JsonPropertyName("detection")] public DetectionSettings Detection { get; set; } = new();
    [JsonPropertyName("tuning")] public TuningSettings Tuning { get; set; } = new();
    [JsonPropertyName("tap")] public int[][] Tap { get; set; } = Array.Empty<int[]>();
    [JsonPropertyName("hold")] public int[][] Hold { get; set; } = Array.Empty<int[]>();
    [JsonPropertyName("accuracyPreset")] public AccuracyPreset AccuracyPreset { get; set; } = AccuracyPreset.PerfectOnly;
}

internal sealed class ProfilesSettings
{
    [JsonPropertyName("funkyFriday")] public GameProfile FunkyFriday { get; set; } = new();
    [JsonPropertyName("robeats")] public GameProfile RoBeats { get; set; } = new();
}

internal sealed class AppSettings
{
    [JsonPropertyName("keybinds")] public KeybindSettings Keybinds { get; set; } = new();
    [JsonPropertyName("theme")] public ThemeSettings Theme { get; set; } = new();
    [JsonPropertyName("gameMode")] public GameModeSettings GameMode { get; set; } = new();
    [JsonPropertyName("profiles")] public ProfilesSettings Profiles { get; set; } = new();

    // Legacy fields kept for one-shot migration read
    [JsonPropertyName("detection")] public DetectionSettings? LegacyDetection { get; set; }
    [JsonPropertyName("tuning")] public TuningSettings? LegacyTuning { get; set; }
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
    public ProfilesSettings Profiles => _settings.Profiles;

    /// <summary>
    /// Returns the profile for the currently active game. The reference is
    /// resolved each call — callers must NOT cache it across a game switch,
    /// since switching games changes which profile this resolves to.
    /// MacroEngine snapshots Detection/Tuning values in Start() for the run.
    /// </summary>
    public GameProfile ActiveProfile =>
        GameMode.ActiveGame == "funkyFriday" ? Profiles.FunkyFriday : Profiles.RoBeats;

    public DetectionSettings Detection => ActiveProfile.Detection;
    public TuningSettings Tuning => ActiveProfile.Tuning;

    public bool IsWhiteGrayMode => GameMode.ActiveGame == "funkyFriday";

    private AppSettings _settings = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

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
        if (!File.Exists(SettingsPath)) return;
        try
        {
            var json = File.ReadAllText(SettingsPath);
            _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            MigrateLegacyIfNeeded();
        }
        catch { _settings = new AppSettings(); }
    }

    private void MigrateLegacyIfNeeded()
    {
        bool hasLegacyDetection = _settings.LegacyDetection != null;
        bool hasLegacyTuning    = _settings.LegacyTuning != null;
        bool hasLegacyCoords    = File.Exists(CoordsPath);

        if (!hasLegacyDetection && !hasLegacyTuning && !hasLegacyCoords) return;

        var activeGame = _settings.GameMode.ActiveGame;
        var target = activeGame == "funkyFriday"
            ? _settings.Profiles.FunkyFriday
            : _settings.Profiles.RoBeats;

        if (hasLegacyDetection) target.Detection = _settings.LegacyDetection!;
        if (hasLegacyTuning)    target.Tuning    = _settings.LegacyTuning!;

        bool coordsMigrated = false;
        if (hasLegacyCoords)
        {
            try
            {
                var json = File.ReadAllText(CoordsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var tapArr  = root.GetProperty("tap");
                var holdArr = root.GetProperty("hold");
                target.Tap  = new int[4][];
                target.Hold = new int[4][];
                for (int i = 0; i < 4; i++)
                {
                    target.Tap[i]  = new[] { tapArr[i][0].GetInt32(),  tapArr[i][1].GetInt32() };
                    target.Hold[i] = new[] { holdArr[i][0].GetInt32(), holdArr[i][1].GetInt32() };
                }
                coordsMigrated = true;
            }
            catch { /* coords.json corrupt — fall through to defaults */ }
        }

        bool migrated = hasLegacyDetection || hasLegacyTuning || coordsMigrated;

        if (migrated)
        {
            _settings.LegacyDetection = null;
            _settings.LegacyTuning    = null;
            SaveSettings();
        }

        if (hasLegacyCoords)
        {
            try { File.Delete(CoordsPath); } catch { }
        }

        if (migrated)
        {
            Log.Info($"Migrated legacy settings to profile: {activeGame}");
        }
        else if (hasLegacyCoords)
        {
            Log.Info("Discarded corrupt legacy coords.json — using profile defaults");
        }
    }

    public void SaveSettings()
    {
        var json = JsonSerializer.Serialize(_settings, JsonOpts);
        File.WriteAllText(SettingsPath, json);
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
}
