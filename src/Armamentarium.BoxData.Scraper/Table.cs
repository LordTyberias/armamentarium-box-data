using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// Reads and writes <c>box-contents.json</c> in the form it is committed: two-space indent, LF, no escaped non-ASCII
/// (the round trip of the committed table is byte-identical, see TableTests).
/// </summary>
public static class Table
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonNode Read(string text) => JsonNode.Parse(text) ?? throw new InvalidDataException("The table is empty.");

    public static string Write(JsonNode table) => table.ToJsonString(Options) + "\n";

    public static IReadOnlySet<string> Codes(JsonNode table) =>
        Boxes(table).SelectMany(b => b["productCodes"]!.AsArray()).Select(c => (string)c!).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A box: one block per reading, entries with name and count, and ids where <paramref name="idsOf"/> names the one
    /// root entry of that reading (ADR 0004); without it, no ids (ADR 0003 §10).
    /// </summary>
    public static JsonObject Box(
        string name,
        IReadOnlyList<string> codes,
        IReadOnlyList<string> sources,
        IReadOnlyList<string> readings,
        IReadOnlyList<ProposedEntry> entries,
        Func<string, string, RootEntry?>? idsOf = null) =>
        new()
        {
            ["name"] = name,
            ["productCodes"] = Array(codes),
            ["sources"] = Array(sources),
            ["readings"] = new JsonArray(readings.Select(r => (JsonNode)new JsonObject
            {
                ["reading"] = r,
                ["entries"] = new JsonArray(entries.Select(e => Entry(e, r, idsOf?.Invoke(r, e.Name))).ToArray()),
            }).ToArray()),
        };

    /// <summary>A Combat Patrol box names only its generation; its contents come from the Combat Patrol catalogue.</summary>
    public static JsonObject CombatPatrolBox(
        string name, IReadOnlyList<string> codes, IReadOnlyList<string> sources, string generation) =>
        new()
        {
            ["name"] = name,
            ["productCodes"] = Array(codes),
            ["sources"] = Array(sources),
            ["generation"] = generation,
        };

    public static void AddBox(JsonNode table, JsonObject box) => table["boxes"]!.AsArray().Add(box);

    /// <summary>Adds the codes to the box that holds <paramref name="knownCode"/>; returns that box's name.</summary>
    public static string AddCodes(JsonNode table, string knownCode, IReadOnlyList<string> codes)
    {
        var box = Boxes(table).First(b => b["productCodes"]!.AsArray().Any(c => (string)c! == knownCode));
        var held = box["productCodes"]!.AsArray();
        foreach (var code in codes.Where(c => held.All(h => (string)h! != c)))
        {
            held.Add(code);
        }

        return (string)box["name"]!;
    }

    /// <summary>The schema rules a draft can break before a person sees it, or null.</summary>
    public static string? Validate(JsonObject box)
    {
        var sources = box["sources"]!.AsArray().Select(s => (string)s!).ToList();
        if (sources.Distinct().Count() < 2)
        {
            return "fewer than two sources";
        }

        if (sources.Any(s => !s.StartsWith("https://", StringComparison.Ordinal)))
        {
            return "a source is not https";
        }

        var codes = box["productCodes"]!.AsArray().Select(c => (string)c!).ToList();
        if (codes.Count == 0 || codes.Distinct().Count() != codes.Count || codes.Any(c => !ProductCatalogue.ArticleNumber().IsMatch(c)))
        {
            return "article numbers missing, repeated or not six to eleven digits";
        }

        return null;
    }

    private static IEnumerable<JsonNode> Boxes(JsonNode table) => table["boxes"]!.AsArray().OfType<JsonNode>();

    private static JsonArray Array(IEnumerable<string> values) => new(values.Select(v => (JsonNode)v).ToArray());

    // A non-player model is an `npo` only in Kill Team; in any other reading it is an `extra` (README, Entry).
    private static JsonNode Entry(ProposedEntry entry, string reading, RootEntry? ids)
    {
        var node = new JsonObject
        {
            ["role"] = !entry.NonPlayer ? "team" : reading == "kt" ? "npo" : "extra",
        };
        if (ids is not null)
        {
            node["catalogueId"] = ids.CatalogueId;
            node["entryId"] = ids.EntryId;
        }

        node["name"] = entry.Name;
        if (entry.Counts.Count == 1)
        {
            node["count"] = entry.Counts[0];
        }
        else
        {
            node["countCandidates"] = new JsonArray(entry.Counts.Select(c => (JsonNode)c).ToArray());
        }

        return node;
    }
}
