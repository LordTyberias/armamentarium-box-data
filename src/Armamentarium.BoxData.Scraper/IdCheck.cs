using System.Text.Json.Nodes;

namespace Armamentarium.BoxData.Scraper;

/// <summary>One pair of ids in the table and what this run's catalogues say about it (ADR 0004 Addendum ARMAM-597).</summary>
public sealed record IdFinding(string Box, string Reading, string Name, RootEntry Ids, IdState State);

/// <summary>
/// Holds the ids the table already carries against the catalogues of the run (ADR 0004 Addendum ARMAM-597): every fixed
/// entry and every option of a choice, in the entry's own reading where it names one. Nothing here writes the table on its
/// own; <see cref="RemoveOrphans"/> builds the content of a draft the maintainer merges.
/// </summary>
public static class IdCheck
{
    public static IReadOnlyList<IdFinding> Findings(JsonNode table, CatalogueIndex catalogues) =>
        Pairs(table, catalogues).Select(p => p.Finding).ToList();

    /// <summary>
    /// Removes every orphaned pair from <paramref name="table"/>: a fixed entry, or a choice left with fewer than two
    /// options (the schema asks for two), keeps name and count only and becomes a gap. Returns the number of pairs removed.
    /// </summary>
    public static int RemoveOrphans(JsonNode table, CatalogueIndex catalogues)
    {
        var orphans = Pairs(table, catalogues).Where(p => p.Finding.State == IdState.Orphaned).ToList();
        foreach (var (_, entry, option) in orphans)
        {
            if (option is null)
            {
                Gap(entry);
            }
            else
            {
                entry["choice"]!.AsArray().Remove(option);
            }
        }

        foreach (var entry in orphans.Where(o => o.Option is not null).Select(o => o.Entry).Distinct())
        {
            if (entry["choice"]!.AsArray().Count < 2)
            {
                Gap(entry);
            }
        }

        return orphans.Count;
    }

    private static void Gap(JsonObject entry)
    {
        entry.Remove("catalogueId");
        entry.Remove("entryId");
        entry.Remove("choice");
        // The entry's own reading names the family of its catalogue id; without the id it says nothing.
        entry.Remove("reading");
    }

    private static IEnumerable<(IdFinding Finding, JsonObject Entry, JsonNode? Option)> Pairs(JsonNode table, CatalogueIndex catalogues)
    {
        foreach (var box in table["boxes"]!.AsArray().OfType<JsonObject>())
        {
            // A Combat Patrol names only its generation and carries no ids.
            foreach (var block in (box["readings"]?.AsArray() ?? []).OfType<JsonObject>())
            {
                foreach (var entry in block["entries"]!.AsArray().OfType<JsonObject>())
                {
                    if ((string?)entry["catalogueId"] is not { } catalogueId)
                    {
                        continue;
                    }

                    var reading = (string?)entry["reading"] ?? (string)block["reading"]!;
                    var pairs = entry["choice"] is JsonArray choice
                        ? choice.OfType<JsonObject>().Select(o => ((JsonNode?)o, (string)o["entryId"]!, (string)o["name"]!)).ToList()
                        : [(null, (string)entry["entryId"]!, (string)entry["name"]!)];
                    foreach (var (option, entryId, name) in pairs)
                    {
                        var ids = new RootEntry(catalogueId, entryId);
                        yield return (new IdFinding((string)box["name"]!, reading, name, ids, catalogues.Check(reading, ids, name)), entry, option);
                    }
                }
            }
        }
    }
}
