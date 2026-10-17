using System.Text.Json;

namespace Armamentarium.BoxData.Scraper;

/// <summary>When each article number was last searched. A product is searched again after 30 days (ADR 0003 §12).</summary>
public sealed class ScanState
{
    private static readonly TimeSpan Recheck = TimeSpan.FromDays(30);

    private readonly Dictionary<string, DateTimeOffset> _checked;

    public ScanState() : this([])
    {
    }

    private ScanState(Dictionary<string, DateTimeOffset> marks) => _checked = marks;

    public bool IsDue(string code, DateTimeOffset now) => !_checked.TryGetValue(code, out var last) || now - last >= Recheck;

    public void Mark(string code, DateTimeOffset now) => _checked[code] = now;

    public static ScanState Load(string path) =>
        File.Exists(path)
            ? new ScanState(JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(path)) ?? [])
            : new ScanState();

    /// <summary>Writes beside the file and moves it over, so a stopped run never leaves half a state.</summary>
    public void Save(string path)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_checked));
        File.Move(temporary, path, overwrite: true);
    }
}
