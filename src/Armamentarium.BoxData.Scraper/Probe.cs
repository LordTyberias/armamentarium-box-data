namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// <c>--host</c>: what one allowlisted shop reads for one product, the measure of a source (ADR 0003 addendum ARMAM-602).
/// Only with <c>--dry-run</c>; a run over part of the sources would weaken the rule of two sources.
/// </summary>
public static class Probe
{
    public static ISource SourceFor(IReadOnlyList<ISource> sources, string host) =>
        sources.FirstOrDefault(s => string.Equals(s.Host, host, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"{host} is not on the allowlist (Sources.All).", nameof(host));

    public static IEnumerable<string> Describe(string host, Listing? listing)
    {
        if (listing is null)
        {
            yield return $"{host}: not confirmed";
            yield break;
        }

        yield return $"{host}: {listing.Url} {listing.Title}";
        foreach (var line in listing.Lines)
        {
            yield return $"  line {line.Count} x {line.Name}{(line.NonPlayer ? " (npo)" : "")}";
        }

        foreach (var sentence in listing.Sentences)
        {
            yield return $"  build {sentence.Build.Count} x {sentence.Build.Name}{(sentence.Article ? " (article)" : "")}";
            foreach (var addition in sentence.Additions)
            {
                yield return $"    plus {addition.Build.Count} x {addition.Build.Name}{(addition.Article ? " (article)" : "")}";
            }
        }
    }
}
