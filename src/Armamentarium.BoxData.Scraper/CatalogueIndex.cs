using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace Armamentarium.BoxData.Scraper;

/// <summary>A root entry of a catalogue: the catalogue's id and the selectionEntry id (a link's target).</summary>
public sealed record RootEntry(string CatalogueId, string EntryId);

/// <summary>
/// A catalogue repository the service reads ids from (ADR 0004): owner, repository and branch, or the latest release
/// when <see cref="Branch"/> is null. The rows follow the app's BsDataEditionSources; a change there is a change here.
/// </summary>
public sealed record CatalogueSource(string Reading, string Owner, string Repository, string? Branch);

/// <summary>
/// The root entries of the catalogues of every reading, by normalised name (ADR 0004). A name gets ids only when it
/// names exactly one root entry of its reading; one name in two catalogues, or none, gets none.
/// </summary>
public sealed class CatalogueIndex
{
    /// <summary>The compiled allowlist of catalogue repositories (ADR 0004). Nothing else is downloaded.</summary>
    public static readonly CatalogueSource[] Sources =
    [
        new("40k", "BSData", "wh40k-10e", "main"),
        new("40k", "BSData", "wh40k-11e", "main"),
        new("kt", "BSData", "wh40k-killteam", null),
        new("aos", "BSData", "age-of-sigmar-4th", null),
        new("aos", "LordTyberias", "armamentarium-spearhead-data", "main"),
        new("tow", "vflam", "Warhammer-The-Old-World", null),
        new("hh", "BSData", "horus-heresy-3rd-edition", "main"),
    ];

    /// <summary>The largest catalogue file read; the largest measured (11th edition Space Marines JSON) is 6.8 MB.</summary>
    public const int MaxEntryBytes = 32 * 1024 * 1024;

    private readonly Dictionary<string, Dictionary<string, HashSet<RootEntry>>> _byReading = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unavailable = new(StringComparer.Ordinal);

    /// <summary>The ids for a model name, or null when the name names no root entry of the reading or more than one.</summary>
    public RootEntry? Find(string reading, string name) =>
        !_unavailable.Contains(reading)
        && _byReading.TryGetValue(reading, out var names)
        && names.TryGetValue(ContentLines.Normalize(name), out var entries)
        && entries.Count == 1
            ? entries.First()
            : null;

    /// <summary>A reading whose sources could not all be read gets no ids in this run: a missing source could hide a twin.</summary>
    public void MarkUnavailable(string reading) => _unavailable.Add(reading);

    /// <summary>Reads one catalogue file, BattleScribe XML (.cat) or JSON; a library or anything else adds nothing.</summary>
    public void AddCatalogue(string reading, string fileName, byte[] content)
    {
        if (fileName.EndsWith(".cat", StringComparison.OrdinalIgnoreCase))
        {
            AddXml(reading, content);
        }
        else if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            AddJson(reading, content);
        }
    }

    /// <summary>Reads every catalogue of a repository archive in memory; nothing is unpacked to disk.</summary>
    public void AddArchive(string reading, Stream zip, int maxEntryBytes, Action<string> log)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            if (!entry.Name.EndsWith(".cat", StringComparison.OrdinalIgnoreCase)
                && !entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.Length > maxEntryBytes)
            {
                log($"{reading}: {Text.Clean(entry.Name)} skipped, {entry.Length} bytes");
                continue;
            }

            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            // The declared length can lie; the copy stops one byte past the limit.
            CopyAtMost(stream, buffer, maxEntryBytes + 1);
            if (buffer.Length > maxEntryBytes)
            {
                log($"{reading}: {Text.Clean(entry.Name)} skipped, larger than declared");
                continue;
            }

            AddCatalogue(reading, entry.Name, buffer.ToArray());
        }
    }

    /// <summary>
    /// Downloads every source of the allowlist and reads it. A source that fails leaves its reading without ids for this
    /// run (logged); the run goes on.
    /// </summary>
    public static async Task<CatalogueIndex> LoadAsync(HttpClient http, Action<string> log, CancellationToken cancellationToken)
    {
        var index = new CatalogueIndex();
        foreach (var source in Sources)
        {
            try
            {
                var reference = source.Branch is { } branch ? $"heads/{branch}" : $"tags/{await LatestTagAsync(http, source, cancellationToken)}";
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, $"https://codeload.github.com/{source.Owner}/{source.Repository}/zip/refs/{reference}");
                request.Headers.UserAgent.ParseAdd("ArmamentariumBoxData");
                using var response = await http.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var zip = new MemoryStream(await response.Content.ReadAsByteArrayAsync(cancellationToken));
                index.AddArchive(source.Reading, zip, MaxEntryBytes, log);
                log($"{source.Reading}: read {source.Owner}/{source.Repository} at {reference}");
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException or KeyNotFoundException
                                           || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                index.MarkUnavailable(source.Reading);
                log($"{source.Reading}: {source.Owner}/{source.Repository} unreadable, no ids for this reading: " +
                    $"{ex.GetType().Name}: {Text.Clean(ex.Message)}");
            }
        }

        return index;
    }

    private static async Task<string> LatestTagAsync(HttpClient http, CatalogueSource source, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.github.com/repos/{source.Owner}/{source.Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("ArmamentariumBoxData");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var tag = release.RootElement.GetProperty("tag_name").GetString() ?? "";
        // The tag goes into a URL path: only the characters release tags use.
        return tag.Length > 0 && tag.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
            ? tag
            : throw new InvalidDataException("unexpected release tag");
    }

    private static void CopyAtMost(Stream from, Stream to, long limit)
    {
        var buffer = new byte[81920];
        int read;
        while (to.Length < limit && (read = from.Read(buffer, 0, (int)Math.Min(buffer.Length, limit - to.Length))) > 0)
        {
            to.Write(buffer, 0, read);
        }
    }

    private void AddXml(string reading, byte[] content)
    {
        XElement root;
        try
        {
            using var stream = new MemoryStream(content);
            // No DTD, no external entities: the default XmlReaderSettings prohibit DTD processing.
            root = XElement.Load(stream);
        }
        catch (System.Xml.XmlException)
        {
            return;
        }

        if (root.Name.LocalName != "catalogue" || (string?)root.Attribute("library") == "true" || (string?)root.Attribute("id") is not { } id)
        {
            return;
        }

        // Only the root lists: sharedSelectionEntries hold the same element names but are no root offer.
        foreach (var container in root.Elements().Where(c => c.Name.LocalName is "selectionEntries" or "entryLinks"))
        {
            foreach (var child in container.Elements())
            {
                Add(reading, id, child.Name.LocalName, Attr(child, "type"), Attr(child, "id"), Attr(child, "name"), Attr(child, "targetId"));
            }
        }

        static string? Attr(XElement element, string name) => (string?)element.Attribute(name);
    }

    private void AddJson(string reading, byte[] content)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("catalogue", out var catalogue)
                || catalogue.ValueKind != JsonValueKind.Object
                || Scalar(catalogue, "library") == "true"
                || Scalar(catalogue, "id") is not { } id)
            {
                return;
            }

            foreach (var (container, element) in new[] { ("selectionEntries", "selectionEntry"), ("entryLinks", "entryLink") })
            {
                if (!catalogue.TryGetProperty(container, out var children) || children.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var child in children.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Object))
                {
                    Add(reading, id, element, Scalar(child, "type"), Scalar(child, "id"), Scalar(child, "name"), Scalar(child, "targetId"));
                }
            }
        }

        // BattleScribe JSON writes some attributes as strings and some as booleans; both read as text.
        static string? Scalar(JsonElement element, string name) =>
            !element.TryGetProperty(name, out var value) ? null
            : value.ValueKind == JsonValueKind.String ? value.GetString()
            : value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() ? "true" : "false"
            : null;
    }

    // A root selectionEntry of type unit or model, or a root entryLink to a selectionEntry (its own name, its target).
    private void Add(string reading, string catalogueId, string element, string? type, string? id, string? name, string? targetId)
    {
        var entryId = element switch
        {
            "selectionEntry" when type is "unit" or "model" => id,
            "entryLink" when type == "selectionEntry" => targetId,
            _ => null,
        };
        var key = name is null ? "" : ContentLines.Normalize(name.Trim());
        if (entryId is null || key.Length == 0)
        {
            return;
        }

        if (!_byReading.TryGetValue(reading, out var names))
        {
            _byReading[reading] = names = new(StringComparer.Ordinal);
        }

        if (!names.TryGetValue(key, out var entries))
        {
            names[key] = entries = [];
        }

        entries.Add(new RootEntry(catalogueId.Trim(), entryId.Trim()));
    }
}
