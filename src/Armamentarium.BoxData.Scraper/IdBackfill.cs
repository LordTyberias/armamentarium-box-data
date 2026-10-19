using System.Text.Json.Nodes;

namespace Armamentarium.BoxData.Scraper;

/// <summary>One gap of the table that got ids in this run (ADR 0004 Addendum ARMAM-598).</summary>
public sealed record Backfilled(string Box, string Reading, string Name, RootEntry Ids);

/// <summary>
/// Gives a gap of the table the ids its name now names (ADR 0004 Addendum ARMAM-598): a fixed entry without ids in a box
/// the service wrote, by the same hit that sets ids in a new box. A box whose sources are not all allowlisted shops was
/// written by hand; its gaps are decisions ("name without id", ADR 0118 Addendum ARMAM-588) and stay as they are.
/// </summary>
public static class IdBackfill
{
    /// <summary>A box the service wrote: every source is a shop of the allowlist.</summary>
    public static bool IsServiceBox(JsonObject box, IReadOnlySet<string> hosts) =>
        box["sources"] is JsonArray sources
        && sources.Count > 0
        && sources.All(s => Uri.TryCreate((string?)s, UriKind.Absolute, out var uri) && hosts.Contains(uri.Host));

    /// <summary>Sets the ids of every gap in a service box whose name has a hit; returns what it set.</summary>
    public static IReadOnlyList<Backfilled> Fill(JsonNode table, CatalogueIndex catalogues, IReadOnlySet<string> hosts)
    {
        var filled = new List<Backfilled>();
        foreach (var (box, reading, entry) in Gaps(table, hosts))
        {
            var name = (string)entry["name"]!;
            if (catalogues.Find(reading, name) is not { } ids)
            {
                continue;
            }

            // The committed order: role, ids, name, count.
            var at = entry.IndexOf("name");
            entry.Insert(at, "catalogueId", ids.CatalogueId);
            entry.Insert(at + 1, "entryId", ids.EntryId);
            filled.Add(new Backfilled((string)box["name"]!, reading, name, ids));
        }

        return filled;
    }

    /// <summary>Every fixed entry without ids, with the reading it is read in.</summary>
    public static IEnumerable<(JsonObject Box, string Reading, JsonObject Entry)> Gaps(JsonNode table, IReadOnlySet<string> hosts)
    {
        foreach (var box in table["boxes"]!.AsArray().OfType<JsonObject>().Where(b => IsServiceBox(b, hosts)))
        {
            // A Combat Patrol names only its generation and carries no entries.
            foreach (var block in (box["readings"]?.AsArray() ?? []).OfType<JsonObject>())
            {
                foreach (var entry in block["entries"]!.AsArray().OfType<JsonObject>()
                             .Where(e => e["catalogueId"] is null && e["choice"] is null))
                {
                    yield return (box, (string?)entry["reading"] ?? (string)block["reading"]!, entry);
                }
            }
        }
    }
}
