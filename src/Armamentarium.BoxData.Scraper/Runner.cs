using System.Text;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// One run (ADR 0003 §10–§12): works through the candidates until five scraper pull requests are open; a product the
/// shops confirm becomes a draft box, or new article numbers of a box already in the table.
/// </summary>
public sealed class Runner(
    IRepository repository,
    IReadOnlyList<ISource> sources,
    IFetcher fetcher,
    ScanState state,
    Action<string> log,
    Action? persist = null)
{
    public const int MaxOpen = 5;

    public async Task RunAsync(
        IReadOnlyList<Product> products, DateTimeOffset now, CancellationToken cancellationToken, string? onlyCode = null)
    {
        var open = new HashSet<string>(await repository.OpenScraperBranchesAsync(cancellationToken), StringComparer.Ordinal);
        if (open.Count >= MaxOpen)
        {
            log($"{open.Count} scraper pull requests are open; nothing to do");
            return;
        }

        var (text, sha) = await repository.GetTableAsync(cancellationToken);
        var known = Table.Codes(Table.Read(text));
        var codeByEan = products.Where(p => p.Ean is not null)
            .GroupBy(p => p.Ean!)
            .ToDictionary(g => g.Key, g => g.First().Code, StringComparer.Ordinal);

        foreach (var product in ProductCatalogue.Candidates(products, known))
        {
            if (open.Count >= MaxOpen)
            {
                log($"{MaxOpen} scraper pull requests are open; stopping");
                break;
            }

            if ((onlyCode is not null && product.Code != onlyCode) || (onlyCode is null && !state.IsDue(product.Code, now)))
            {
                continue;
            }

            var listings = await FindAsync(product, cancellationToken);
            Mark([product.Code], now);
            log($"{product.Code} {product.Name}: {listings.Count} shop(s) confirm it");

            var codes = listings.SelectMany(l => l.VariantEans)
                .Select(e => codeByEan.GetValueOrDefault(e))
                .OfType<string>()
                .Append(product.Code)
                .Where(c => ProductCatalogue.ArticleNumber().IsMatch(c))
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToList();
            var knownCode = codes.FirstOrDefault(known.Contains);
            var proposal = knownCode is null
                ? NewBox(product, codes, listings, text)
                : LanguageVariants(knownCode, codes.Where(c => !known.Contains(c)).ToList(), listings, text);
            if (proposal is null)
            {
                continue;
            }

            if (open.Contains(proposal.Branch))
            {
                log($"{proposal.Branch} is already open; skipped");
                continue;
            }

            await repository.ProposeAsync(proposal, sha, cancellationToken);
            open.Add(proposal.Branch);
            Mark(codes, now);
            log($"proposed {proposal.Branch}: {proposal.Title}");
        }
    }

    private void Mark(IEnumerable<string> codes, DateTimeOffset now)
    {
        foreach (var code in codes)
        {
            state.Mark(code, now);
        }

        persist?.Invoke();
    }

    private async Task<List<Listing>> FindAsync(Product product, CancellationToken cancellationToken)
    {
        var found = await Task.WhenAll(sources.Select(async source =>
        {
            try
            {
                return await source.FindAsync(product, fetcher, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                log($"{source.Host}: {product.Code} failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }));
        return found.OfType<Listing>().ToList();
    }

    private Proposal? NewBox(Product product, IReadOnlyList<string> codes, IReadOnlyList<Listing> listings, string text)
    {
        var name = listings.FirstOrDefault(l => !l.German && l.Title.Length > 0)?.Title
            ?? listings.FirstOrDefault(l => l.Title.Length > 0)?.Title
            ?? product.Name;
        var body = new StringBuilder()
            .AppendLine("Draft from the scraper service (ADR 0003). Before merging, complete by hand: catalogue ids")
            .AppendLine("(`catalogueId` with `entryId` or `choice`), readings, catalogue names and roles. The merge is the hand check.")
            .AppendLine()
            .AppendLine($"**Article numbers:** {string.Join(", ", codes)}  ")
            .AppendLine($"**Catalogue name and status:** {product.Name}, {product.Status}")
            .AppendLine();

        System.Text.Json.Nodes.JsonObject box;
        if (ProductCatalogue.IsCombatPatrol(product))
        {
            var urls = listings.Select(l => l.Url).Distinct().ToList();
            if (urls.Count < 2)
            {
                log($"{product.Code} waits: {urls.Count} shop(s) confirm the Combat Patrol");
                return null;
            }

            var generation = product.Status == "current" ? "current" : "legacy";
            box = Table.CombatPatrolBox(name, codes, urls, generation);
            Sources(body, urls);
            body.AppendLine("**Notes**")
                .AppendLine($"- The generation is set to `{generation}` from the catalogue status `{product.Status}`; check it.");
        }
        else
        {
            var agreement = Agreement.Agree(listings);
            if (agreement.Entries is null)
            {
                log($"{product.Code} waits: {agreement.Waiting}");
                return null;
            }

            var readings = ProductCatalogue.ReadingsOf(product);
            box = Table.Box(name, codes, agreement.Sources, readings, agreement.Entries);
            Sources(body, agreement.Sources);
            body.AppendLine($"**Entries** (in every reading: {string.Join(", ", readings)})")
                .AppendLine()
                .AppendLine("| Entry | Count | Confirmed by |")
                .AppendLine("|---|---|---|");
            foreach (var entry in agreement.Entries)
            {
                body.AppendLine($"| {Cell(entry.Name)} | {string.Join(" or ", entry.Counts)} | {string.Join(", ", entry.Hosts)} |");
            }

            var notes = agreement.Entries.SelectMany(Notes).ToList();
            if (notes.Count > 0)
            {
                body.AppendLine().AppendLine("**Notes**");
                notes.ForEach(n => body.AppendLine($"- {n}"));
            }
        }

        if (Table.Validate(box) is { } error)
        {
            log($"{product.Code} not proposed: {error}");
            return null;
        }

        var table = Table.Read(text);
        Table.AddBox(table, box);
        return new Proposal($"{GitHubRepository.BranchPrefix}{codes[0]}", $"data: add {name}", body.ToString(), Table.Write(table));
    }

    private Proposal? LanguageVariants(string knownCode, IReadOnlyList<string> added, IReadOnlyList<Listing> listings, string text)
    {
        if (added.Count == 0)
        {
            return null;
        }

        var table = Table.Read(text);
        var name = Table.AddCodes(table, knownCode, added);
        var body = new StringBuilder()
            .AppendLine($"Language variants of **{Cell(name)}** (ADR 0003 §11): {string.Join(", ", added)}.")
            .AppendLine()
            .AppendLine($"Found as variants of the same shop product as {knownCode}:")
            .AppendLine();
        foreach (var url in listings.Where(l => l.VariantEans.Count > 1).Select(l => l.Url).Distinct())
        {
            body.AppendLine($"- {url}");
        }

        return new Proposal(
            $"{GitHubRepository.BranchPrefix}{added[0]}", $"data: add language variants to {name}", body.ToString(), Table.Write(table));
    }

    private static void Sources(StringBuilder body, IEnumerable<string> urls)
    {
        body.AppendLine("**Sources**");
        foreach (var url in urls)
        {
            body.AppendLine($"- {url}");
        }

        body.AppendLine();
    }

    private static IEnumerable<string> Notes(ProposedEntry entry)
    {
        if (entry.Counts.Count > 1)
        {
            yield return $"{Cell(entry.Name)}: the sources disagree ({string.Join(", ", entry.Counts)}); entered as `countCandidates`.";
        }

        if (entry.AlternativeBuild)
        {
            yield return $"{Cell(entry.Name)}: a source names an alternative build; counted as the build the box is sold as.";
        }

        if (entry.NonPlayer)
        {
            yield return $"{Cell(entry.Name)}: named as non-player models; entered with role `npo`.";
        }
    }

    // Shop text never reaches the table's prose; in a Markdown cell it must not break the row either.
    private static string Cell(string text) => text.Replace("|", "/").Replace("\n", " ");
}
