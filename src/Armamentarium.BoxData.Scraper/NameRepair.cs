using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Armamentarium.BoxData.Scraper;

/// <summary>One entry the repair changes: renamed to <see cref="NewName"/>, or removed when it is null.</summary>
public sealed record NameRepairing(string Box, string Reading, string Name, string? NewName);

/// <summary>What <see cref="NameRepair.Apply"/> changed.</summary>
public sealed record NameRepairs(IReadOnlyList<NameRepairing> Renamed, IReadOnlyList<NameRepairing> Removed, IReadOnlyList<string> BoxesRemoved);

/// <summary>
/// Applies the reader's fixes of ADR 0004 Addendum ARMAM-598 to names the service wrote before them: a name that is no model
/// (excluded or description only) leaves its box, a clause read into the name ("… accompanied by", "accompanying …",
/// "… usable by …") is cut, or the entry leaves when the cut name is already in the reading. A reading left without entries goes, a box left without readings goes whole (the schema asks
/// for one) and comes back with the next run. Only gaps of service boxes: an entry with ids names a root entry, a box
/// written by hand is a decision (<see cref="IdBackfill.IsServiceBox"/>). Builds the content of a draft the maintainer merges.
/// </summary>
public static partial class NameRepair
{
    public static NameRepairs Apply(JsonNode table, IReadOnlySet<string> hosts)
    {
        var renamed = new List<NameRepairing>();
        var removed = new List<NameRepairing>();
        var boxesRemoved = new List<string>();
        var boxes = table["boxes"]!.AsArray();
        foreach (var box in boxes.OfType<JsonObject>().Where(b => IdBackfill.IsServiceBox(b, hosts)).ToList())
        {
            var boxName = (string)box["name"]!;
            if (box["readings"] is not JsonArray blocks)
            {
                continue;
            }

            foreach (var block in blocks.OfType<JsonObject>().ToList())
            {
                var reading = (string)block["reading"]!;
                var entries = block["entries"]!.AsArray();
                foreach (var entry in entries.OfType<JsonObject>().Where(IdBackfill.IsGap).ToList())
                {
                    var name = (string)entry["name"]!;
                    var cut = Clause().Match(name) is { Success: true } clause ? clause.Groups["name"].Value : name;
                    var twice = cut != name && entries.OfType<JsonObject>().Any(e => e != entry
                        && ContentLines.Normalize((string)e["name"]!) == ContentLines.Normalize(cut));
                    if (ContentLines.IsExcluded(name) || !ContentLines.IsModelName(name) || twice)
                    {
                        entries.Remove(entry);
                        removed.Add(new NameRepairing(boxName, reading, name, null));
                    }
                    else if (cut != name)
                    {
                        entry["name"] = cut;
                        renamed.Add(new NameRepairing(boxName, reading, name, cut));
                    }
                }

                if (entries.Count == 0)
                {
                    blocks.Remove(block);
                }
            }

            if (blocks.Count == 0)
            {
                boxes.Remove(box);
                boxesRemoved.Add(boxName);
            }
        }

        return new NameRepairs(renamed, removed, boxesRemoved);
    }

    // Only the words the build sentence reader learnt in ARMAM-598 (BuildSentences.Stop, BuildSentences.Name); other words
    // that end a name there ("on", "with") are part of names the lines wrote ("Lord on Steed").
    [GeneratedRegex(@"^(?:accompanying\s+)?(?<name>.+?)(?:\s+(?:accompanied|usable)\b.*)?$", RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex Clause();
}
