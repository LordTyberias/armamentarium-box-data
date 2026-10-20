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

    /// <summary>An entry without ids: a name and a count only (a choice always carries its catalogue id).</summary>
    public static bool IsGap(JsonObject entry) => entry["catalogueId"] is null;

    /// <summary>
    /// Sets the ids of every gap in a service box whose name has a hit; returns what it set. A hit the reading of the box
    /// already lists as a fixed entry stays a gap: no root entry is listed twice in a reading ("Intercessors" beside
    /// "Intercessor Squad"). An alias is one model of its entry and gets the ids beside the others (ADR 0004 Addendum ARMAM-642).
    /// </summary>
    public static IReadOnlyList<Backfilled> Fill(JsonNode table, CatalogueIndex catalogues, IReadOnlySet<string> hosts)
    {
        var filled = new List<Backfilled>();
        foreach (var (box, reading, entries, entry) in Gaps(table, hosts))
        {
            var name = (string)entry["name"]!;
            if (catalogues.Find(reading, name) is not { } ids
                || (!catalogues.IsAlias(reading, name)
                    && entries.OfType<JsonObject>().Any(e => (string?)e["catalogueId"] == ids.CatalogueId && (string?)e["entryId"] == ids.EntryId)))
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
    public static IEnumerable<(JsonObject Box, string Reading, JsonArray Entries, JsonObject Entry)> Gaps(JsonNode table, IReadOnlySet<string> hosts)
    {
        foreach (var box in table["boxes"]!.AsArray().OfType<JsonObject>().Where(b => IsServiceBox(b, hosts)))
        {
            // A Combat Patrol names only its generation and carries no entries.
            foreach (var block in (box["readings"]?.AsArray() ?? []).OfType<JsonObject>())
            {
                var entries = block["entries"]!.AsArray();
                foreach (var entry in entries.OfType<JsonObject>().Where(IsGap).ToList())
                {
                    yield return (box, (string?)entry["reading"] ?? (string)block["reading"]!, entries, entry);
                }
            }
        }
    }
}
