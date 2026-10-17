using System.Text.Json;

namespace Armamentarium.BoxData.Scraper;

/// <summary>When each article number was last searched. A product is searched again after 30 days (ADR 0003 §12).</summary>
public sealed class ScanState
{
    private static readonly TimeSpan Recheck = TimeSpan.FromDays(30);

    // A product whose shops did not all answer, or whose proposal failed, comes back after three days, not thirty.
    private static readonly TimeSpan RetryAfter = TimeSpan.FromDays(3);

    private readonly Dictionary<string, DateTimeOffset> _checked;

    public ScanState() : this([])
    {
    }

    private ScanState(Dictionary<string, DateTimeOffset> marks) => _checked = marks;

    public bool IsDue(string code, DateTimeOffset now) => !_checked.TryGetValue(code, out var last) || now - last >= Recheck;

    public void Mark(string code, DateTimeOffset now) => _checked[code] = now;

    public void Retry(string code, DateTimeOffset now) => _checked[code] = now - (Recheck - RetryAfter);

    public static ScanState Load(string path)
    {
        if (!File.Exists(path))
        {
            return new ScanState();
        }

        try
        {
            return new ScanState(JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(path)) ?? []);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"{path} is not a valid scan state; deleting it resets the 30-day marks and the next run searches everything.", ex);
        }
    }

    /// <summary>Writes beside the file and moves it over, so a stopped run never leaves half a state.</summary>
    public void Save(string path)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_checked));
        File.Move(temporary, path, overwrite: true);
    }
}
