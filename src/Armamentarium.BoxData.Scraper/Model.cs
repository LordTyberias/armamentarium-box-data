namespace Armamentarium.BoxData.Scraper;

/// <summary>A product of the product catalogue: article number, EAN, name, game systems, status, category.</summary>
public sealed record Product(string Code, string? Ean, string Name, IReadOnlyList<string> Systems, string Status, string Category);

/// <summary>One "N x Name" line of a shop's description. Only the number and the name are kept.</summary>
public sealed record Line(int Count, string Name, bool NonPlayer, bool AlternativeBuild);

/// <summary>A shop page that confirmed the product. <see cref="German"/> marks translated model names.</summary>
public sealed record Listing(
    string Host, string Url, bool German, string Title, IReadOnlyList<Line> Lines, IReadOnlyList<string> VariantEans);

/// <summary>An entry two sources confirm: one count, or the counts they disagree on.</summary>
public sealed record ProposedEntry(
    string Name, bool NonPlayer, IReadOnlyList<int> Counts, IReadOnlyList<string> Hosts, bool AlternativeBuild);

/// <summary>Either the entries and the source URLs behind them, or why the box waits.</summary>
public sealed record AgreementResult(IReadOnlyList<ProposedEntry>? Entries, IReadOnlyList<string> Sources, string? Waiting);

/// <summary>A draft pull request: branch, title, body and the whole table as it would be committed.</summary>
public sealed record Proposal(string Branch, string Title, string Body, string Table);

public interface IFetcher
{
    /// <summary>The body of an allowlisted page, or null when the shop does not answer with one.</summary>
    Task<string?> GetAsync(string url, CancellationToken cancellationToken);
}

public interface ISource
{
    string Host { get; }

    /// <summary>The least time between two requests to this host (ADR 0003 §6).</summary>
    TimeSpan Interval { get; }

    /// <summary>The shop's listing of the product, or null when the shop does not confirm it.</summary>
    Task<Listing?> FindAsync(Product product, IFetcher fetcher, CancellationToken cancellationToken);
}

public interface IRepository
{
    Task<(string Text, string Sha)> GetTableAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> OpenScraperBranchesAsync(CancellationToken cancellationToken);

    Task ProposeAsync(Proposal proposal, string tableSha, CancellationToken cancellationToken);
}
