using System.Text.Json.Serialization;

namespace SoulBeatsPro;

internal sealed class ColorSignatureEntry
{
    [JsonPropertyName("r")] public int R { get; set; }
    [JsonPropertyName("g")] public int G { get; set; }
    [JsonPropertyName("b")] public int B { get; set; }
    [JsonPropertyName("tolerance")] public int Tolerance { get; set; }
    [JsonPropertyName("learned")] public bool Learned { get; set; } = false;

    public ColorSignatureEntry() { }

    public ColorSignatureEntry(int r, int g, int b, int tolerance, bool learned = false)
    {
        R = r; G = g; B = b; Tolerance = tolerance; Learned = learned;
    }
}

internal sealed class ColorSignature
{
    [JsonPropertyName("entries")]
    public List<ColorSignatureEntry> Entries { get; set; } = new();
}

internal sealed class Profile
{
    [JsonPropertyName("name")] public string Name { get; set; } = "New Profile";
    [JsonPropertyName("isBuiltIn")] public bool IsBuiltIn { get; set; }
    [JsonPropertyName("signatures")]
    public ColorSignature[] Signatures { get; set; } =
        new[] { new ColorSignature(), new ColorSignature(), new ColorSignature(), new ColorSignature() };
    [JsonPropertyName("tap")] public int[][] Tap { get; set; } = Array.Empty<int[]>();
    [JsonPropertyName("hold")] public int[][] Hold { get; set; } = Array.Empty<int[]>();
    [JsonPropertyName("tuning")] public TuningSettings Tuning { get; set; } = new();
    [JsonPropertyName("accuracyPreset")] public AccuracyPreset AccuracyPreset { get; set; } = AccuracyPreset.PerfectOnly;
    [JsonPropertyName("maxJudgmentMs")] public double MaxJudgmentMs { get; set; } = 100.0;
}
