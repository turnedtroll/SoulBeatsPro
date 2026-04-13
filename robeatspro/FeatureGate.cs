using System.Text.Json;

namespace SoulBeatsPro;

/// <summary>
/// Server-controlled feature flags.
///
/// The admin panel can push a set of disabled feature keys, or a full
/// revoke, down to a specific client. The decision is persisted to
/// <c>gate.json</c> inside the hidden admin cache directory so it
/// survives restarts — a kicked user stays kicked even if they kill
/// the process before the command round-trips.
///
/// Feature code should guard itself with <see cref="IsEnabled"/> at
/// the call site. Anything that calls <see cref="CheckRevoked"/> at
/// startup will be blocked outright when <see cref="Revoked"/> is set.
/// </summary>
internal sealed class FeatureGate
{
    public static FeatureGate Instance { get; } = Load();

    public HashSet<string> DisabledFeatures { get; private set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public bool Revoked { get; private set; }
    public string RevokedReason { get; private set; } = "";
    public DateTime UpdatedAt { get; private set; } = DateTime.MinValue;

    // Note: must reference AdminConfig.HiddenDir, which is populated by its
    // own static initializer. AdminConfig has no dependency on FeatureGate,
    // so the order is safe.
    private static readonly string GatePath =
        Path.Combine(AdminConfig.HiddenDir, "gate.json");

    public bool IsEnabled(string feature)
    {
        if (Revoked) return false;
        return !DisabledFeatures.Contains(feature);
    }

    public bool CheckRevoked() => Revoked;

    private sealed class GateFile
    {
        public string[] DisabledFeatures { get; set; } = Array.Empty<string>();
        public bool Revoked { get; set; }
        public string RevokedReason { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
    }

    private static FeatureGate Load()
    {
        try
        {
            if (File.Exists(GatePath))
            {
                var json = File.ReadAllText(GatePath);
                var gf = JsonSerializer.Deserialize<GateFile>(json);
                if (gf != null)
                {
                    var fg = new FeatureGate
                    {
                        DisabledFeatures = new HashSet<string>(
                            gf.DisabledFeatures ?? Array.Empty<string>(),
                            StringComparer.OrdinalIgnoreCase),
                        Revoked = gf.Revoked,
                        RevokedReason = gf.RevokedReason ?? ""
                    };
                    if (DateTime.TryParse(gf.UpdatedAt, out var dt)) fg.UpdatedAt = dt;
                    return fg;
                }
            }
        }
        catch { }
        return new FeatureGate();
    }

    /// <summary>
    /// Applies a gate update from an admin command and persists it.
    /// Safe to call on any thread.
    /// </summary>
    public void ApplyUpdate(IEnumerable<string>? disabled, bool revoked, string reason)
    {
        lock (this)
        {
            DisabledFeatures = new HashSet<string>(
                disabled ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            Revoked = revoked;
            RevokedReason = reason ?? "";
            UpdatedAt = DateTime.UtcNow;
            Save();
        }

        Log.Info($"FeatureGate updated: revoked={revoked}, disabled=[{string.Join(",", DisabledFeatures)}]");
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AdminConfig.HiddenDir);
            var gf = new GateFile
            {
                DisabledFeatures = DisabledFeatures.ToArray(),
                Revoked = Revoked,
                RevokedReason = RevokedReason,
                UpdatedAt = UpdatedAt.ToString("o")
            };
            File.WriteAllText(GatePath, JsonSerializer.Serialize(gf));
        }
        catch (Exception ex)
        {
            Log.Warn($"FeatureGate save failed: {ex.Message}");
        }
    }
}
