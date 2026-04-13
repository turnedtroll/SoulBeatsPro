using System.Text.Json;

namespace larpLOLv4;

internal static class CoordsManager
{
    // Default coordinates (same as the Python version)
    public static readonly Point[] DefaultTap =
    [
        new(720, 950), new(881, 950), new(1039, 950), new(1200, 950)
    ];

    public static readonly Point[] DefaultHold =
    [
        new(720, 824), new(878, 824), new(1040, 824), new(1197, 824)
    ];

    private static string CoordsPath
    {
        get
        {
            // Use the exe's actual directory, not the temp extraction dir
            var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
            var dir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            return Path.Combine(dir, "coords.txt");
        }
    }

    public static (Point[] tap, Point[] hold) Load()
    {
        var path = CoordsPath;
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tap = new Point[4];
                var hold = new Point[4];

                var tapArr = root.GetProperty("tap");
                var holdArr = root.GetProperty("hold");

                for (int i = 0; i < 4; i++)
                {
                    var t = tapArr[i];
                    tap[i] = new Point(t[0].GetInt32(), t[1].GetInt32());
                    var h = holdArr[i];
                    hold[i] = new Point(h[0].GetInt32(), h[1].GetInt32());
                }

                return (tap, hold);
            }
            catch
            {
                // fall through to defaults
            }
        }

        return (
            (Point[])DefaultTap.Clone(),
            (Point[])DefaultHold.Clone()
        );
    }

    public static void Save(Point[] tap, Point[] hold)
    {
        var obj = new
        {
            tap = tap.Select(p => new[] { p.X, p.Y }).ToArray(),
            hold = hold.Select(p => new[] { p.X, p.Y }).ToArray()
        };
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(CoordsPath, json);
    }
}
