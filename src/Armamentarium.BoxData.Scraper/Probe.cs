namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// <c>--host</c>: what one allowlisted shop reads for one product, the measure of a source (ADR 0003 addendum ARMAM-602).
/// Only with <c>--dry-run</c>; a run over part of the sources would weaken the rule of two sources.
/// </summary>
public static class Probe
{
    /// <summary>
    /// The shop a probe asks, checked before any request: no <c>--host</c> is no probe; a <c>--host</c> without a value,
    /// without <c>--dry-run</c> and <c>--code</c> or off the allowlist is an error, never a full run.
    /// </summary>
    public static (ISource? Source, string? Error) Resolve(IReadOnlyList<string> args, IReadOnlyList<ISource> sources)
    {
        var at = args.ToList().IndexOf("--host");
        if (at < 0)
        {
            return (null, null);
        }

        if (at + 1 >= args.Count || args[at + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return (null, "--host needs a host");
        }

        if (!args.Contains("--dry-run") || !args.Contains("--code"))
        {
            return (null, "--host needs --dry-run and --code");
        }

        try
        {
            return (SourceFor(sources, args[at + 1]), null);
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }
    }

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
            yield return $"  build {sentence.Build.Count} x {sentence.Build.Name}{(sentence.Names ? " (names)" : sentence.Article ? " (article)" : "")}";
            if (sentence.Longer is { } longer)
            {
                yield return $"    or {longer.Count} x {longer.Name}";
            }

            foreach (var member in sentence.Members)
            {
                yield return $"    member {member.Count} x {member.Name}";
            }

            foreach (var addition in sentence.Additions)
            {
                yield return $"    plus {addition.Build.Count} x {addition.Build.Name}{(sentence.Names ? "" : addition.Article ? " (article)" : "")}";
            }
        }
    }
}
