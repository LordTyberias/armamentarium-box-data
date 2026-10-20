using System.Text.Json;
using System.Text.Json.Nodes;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// One row of <c>aliases.json</c> (ADR 0004 Addendum ARMAM-642): a model's own name in a reading, and the root entry it
/// belongs to, named by the entry's name and its catalogue's name. Ids come from the catalogues, never from this file.
/// </summary>
public sealed record Alias(string Reading, string Name, string Target, string Catalogue);

/// <summary>Reads <c>aliases.json</c> as <c>aliases.schema.json</c> describes it.</summary>
public static class Aliases
{
    public const string File = "aliases.json";

    private static readonly HashSet<string> Readings = new(["kt", "40k", "aos", "tow", "hh"], StringComparer.Ordinal);

    /// <summary>The aliases of the file; none when there is no file. A file that breaks the schema fails the run.</summary>
    public static IReadOnlyList<Alias> Read(string? text)
    {
        if (text is null)
        {
            return [];
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{File} is no JSON: {ex.Message}");
        }

        if (root is not JsonObject file || (int?)file["format"] != 1 || file["aliases"] is not JsonArray rows)
        {
            throw new InvalidDataException($"{File} is not format 1 with an aliases array.");
        }

        return rows.Select(Row).ToList();
    }

    private static Alias Row(JsonNode? node)
    {
        string Field(string name) =>
            node is JsonObject row && row[name] is JsonValue value && value.TryGetValue<string>(out var text) && text.Trim().Length > 0
                ? text
                : throw new InvalidDataException($"An alias of {File} has no {name}.");

        var alias = new Alias(Field("reading"), Field("name"), Field("target"), Field("catalogue"));
        return Readings.Contains(alias.Reading) ? alias : throw new InvalidDataException($"An alias of {File} names an unknown reading.");
    }
}
