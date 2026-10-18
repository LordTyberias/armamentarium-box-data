using System.Text;
using System.Text.Json.Nodes;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// One run (ADR 0003 §10–§12): works through the candidates until five scraper pull requests are open; a product the
/// shops confirm becomes a draft box, or new article numbers of a box already in the table. With <c>autoMerge</c> each
/// pull request is merged at once and the next one is built on the merged table (ADR 0004); entries get the ids of the
/// one root entry their name names in <c>catalogues</c>.
/// </summary>
public sealed class Runner(
    IRepository repository,
    IReadOnlyList<ISource> sources,
    IFetcher fetcher,
    ScanState state,
    Action<string> log,
    Action? persist = null,
    bool autoMerge = false,
    CatalogueIndex? catalogues = null)
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

            var (listings, failed) = await FindAsync(product, cancellationToken);
            log($"{product.Code} {Text.Clean(product.Name)}: {listings.Count} shop(s) confirm it, {failed} did not answer");

            // A shop that did not answer may hold the missing evidence: the product comes back in three days instead of
            // resting 30 (ADR 0003 §12).
            if (failed > 0 && listings.Count < 2)
            {
                Retry(product.Code, now);
                continue;
            }

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
                Mark([product.Code], now);
                continue;
            }

            if (open.Contains(proposal.Branch))
            {
                log($"{proposal.Branch} is already open; skipped");
                Mark([product.Code], now);
                continue;
            }

            int number;
            try
            {
                number = await repository.ProposeAsync(proposal, sha, cancellationToken);
            }
            catch (GitHubException ex)
            {
                log($"{proposal.Branch} not proposed: {ex.Message}");
                Retry(product.Code, now);
                continue;
            }

            Mark(codes, now);
            log($"proposed {proposal.Branch}: {proposal.Title}");
            if (!autoMerge)
            {
                open.Add(proposal.Branch);
                continue;
            }

            try
            {
                await repository.MergeAsync(number, cancellationToken);
            }
            catch (GitHubException ex)
            {
                // The pull request stays open and counts like a draft; the maintainer sees why in the journal.
                log($"{proposal.Branch} not merged: {ex.Message}");
                open.Add(proposal.Branch);
                continue;
            }

            log($"merged {proposal.Branch}");
            try
            {
                await repository.DeleteBranchAsync(proposal.Branch, cancellationToken);
            }
            catch (GitHubException ex)
            {
                // The merge stands; a branch left behind only blocks a later proposal of the same name.
                log($"{proposal.Branch} merged, branch not deleted: {ex.Message}");
            }

            (text, sha) = await repository.GetTableAsync(cancellationToken);
            known = Table.Codes(Table.Read(text));
        }
    }

    private void Retry(string code, DateTimeOffset now)
    {
        state.Retry(code, now);
        persist?.Invoke();
    }

    private void Mark(IEnumerable<string> codes, DateTimeOffset now)
    {
        foreach (var code in codes)
        {
            state.Mark(code, now);
        }

        persist?.Invoke();
    }

    // A slow source (miniset.net, one request a minute) never confirms a box alone, so it is asked only when a fast one has
    // found the product and the fast findings alone would not make a proposal; otherwise its interval would set the pace of
    // the whole run (ADR 0003 §6).
    private async Task<(List<Listing> Listings, int Failed)> FindAsync(Product product, CancellationToken cancellationToken)
    {
        var fast = sources.Where(s => s.Interval <= Page.DefaultInterval).ToList();
        var (listings, failed) = await AskAsync(fast, product, cancellationToken);
        var slow = sources.Except(fast).ToList();
        if (listings.Count > 0 && slow.Count > 0 && !Suffices(product, listings))
        {
            var (more, moreFailed) = await AskAsync(slow, product, cancellationToken);
            listings.AddRange(more);
            failed += moreFailed;
        }

        return (listings, failed);
    }

    private static bool Suffices(Product product, IReadOnlyList<Listing> listings) =>
        ProductCatalogue.IsCombatPatrol(product)
            ? listings.Select(l => l.Url).Distinct().Count() >= 2
            : Agreement.Agree(listings).Entries is not null;

    private async Task<(List<Listing> Listings, int Failed)> AskAsync(
        IEnumerable<ISource> asked, Product product, CancellationToken cancellationToken)
    {
        var found = await Task.WhenAll(asked.Select(async source =>
        {
            try
            {
                return (Listing: await source.FindAsync(product, fetcher, cancellationToken), Failed: false);
            }
            catch (FetchFailedException)
            {
                // The shop did not answer; the fetcher has logged it.
                return (Listing: null, Failed: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The shop answered with something unreadable (a changed page, a timed-out pattern): no finding, logged.
                // Counting it as "did not answer" would bring every product back each run until the parser is fixed.
                log($"{source.Host}: {product.Code} unreadable: {ex.GetType().Name}: {Text.Clean(ex.Message)}");
                return (Listing: null, Failed: false);
            }
        }));
        return (found.Select(f => f.Listing).OfType<Listing>().ToList(), found.Count(f => f.Failed));
    }

    private Proposal? NewBox(Product product, IReadOnlyList<string> codes, IReadOnlyList<Listing> listings, string text)
    {
        // The table names a box in English (README, Box); a German shop's title never becomes the name, nor the title of a
        // listing that is no source of the box (ADR 0003 addendum ARMAM-601).
        var agreement = ProductCatalogue.IsCombatPatrol(product) ? null : Agreement.Agree(listings);
        var sourceUrls = agreement?.Sources ?? listings.Select(l => l.Url).Distinct().ToList();
        var englishTitle = listings.FirstOrDefault(l => !l.German && l.Title.Length > 0 && sourceUrls.Contains(l.Url))?.Title;
        var name = englishTitle ?? Text.Clean(product.Name);
        var notes = new List<string>();
        if (englishTitle is null)
        {
            notes.Add("The box name is the catalogue name; set the English product name.");
        }

        var body = autoMerge
            ? new StringBuilder()
                .AppendLine("From the scraper service, merged by the scraper service (`--auto-merge`, ADR 0004). Ids come from a")
                .AppendLine("catalogue entry of exactly the same name; an entry without ids had no such entry.")
            : new StringBuilder()
                .AppendLine("Draft from the scraper service (ADR 0003). Before merging, complete by hand: catalogue ids")
                .AppendLine("(`catalogueId` with `entryId` or `choice`), readings, catalogue names and roles. The merge is the hand check.");
        body.AppendLine()
            .AppendLine($"**Box name:** {Text.Code(name)}  ")
            .AppendLine($"**Article numbers:** {string.Join(", ", codes)}  ")
            .AppendLine($"**Catalogue name and status:** {Text.Code(product.Name)}, {Text.Code(product.Status)}")
            .AppendLine();

        JsonObject box;
        if (agreement is null)
        {
            if (sourceUrls.Count < 2)
            {
                log($"{product.Code} waits: {sourceUrls.Count} shop(s) confirm the Combat Patrol");
                return null;
            }

            var generation = product.Status == "current" ? "current" : "legacy";
            box = Table.CombatPatrolBox(name, codes, sourceUrls, generation);
            Sources(body, sourceUrls);
            notes.Add($"The generation is set to `{generation}` from the catalogue status; check it.");
        }
        else
        {
            if (agreement.Entries is null)
            {
                log($"{product.Code} waits: {Text.Clean(agreement.Waiting ?? "")}");
                return null;
            }

            var readings = ProductCatalogue.ReadingsOf(product);
            box = Table.Box(name, codes, agreement.Sources, readings, agreement.Entries, IdsOf);
            Sources(body, agreement.Sources);
            body.AppendLine($"**Entries** (in every reading: {string.Join(", ", readings)})")
                .AppendLine()
                .AppendLine("| Entry | Count | Confirmed by | Ids |")
                .AppendLine("|---|---|---|---|");
            foreach (var entry in agreement.Entries)
            {
                var ids = readings.Select(r => IdsOf(r, entry.Name) is { } e ? $"{r}: {Text.Code(e.CatalogueId)} {Text.Code(e.EntryId)}" : null)
                    .OfType<string>()
                    .ToList();
                body.AppendLine($"| {Text.Code(entry.Name)} | {string.Join(" or ", entry.Counts)} | {string.Join(", ", entry.Hosts)} | " +
                    $"{(ids.Count > 0 ? string.Join("; ", ids) : "none")} |");
            }

            body.AppendLine();
            notes.AddRange(agreement.Entries.SelectMany(Notes));
        }

        if (Table.Validate(box) is { } error)
        {
            log($"{product.Code} not proposed: {error}");
            return null;
        }

        if (notes.Count > 0)
        {
            body.AppendLine("**Notes**");
            notes.ForEach(n => body.AppendLine($"- {n}"));
        }

        var table = Table.Read(text);
        Table.AddBox(table, box);
        return new Proposal(
            $"{GitHubRepository.BranchPrefix}{codes[0]}", $"data: add {name}", body.ToString(), Table.Write(table), Draft: !autoMerge);
    }

    // Ids by name only while the service merges by itself (ADR 0004); a draft leaves them to the maintainer.
    private RootEntry? IdsOf(string reading, string name) => autoMerge ? catalogues?.Find(reading, name) : null;

    private Proposal? LanguageVariants(string knownCode, IReadOnlyList<string> added, IReadOnlyList<Listing> listings, string text)
    {
        if (added.Count == 0)
        {
            return null;
        }

        var table = Table.Read(text);
        var name = Text.Clean(Table.AddCodes(table, knownCode, added));
        var body = new StringBuilder()
            .AppendLine($"Language variants of {Text.Code(name)} (ADR 0003 §11): {string.Join(", ", added)}.")
            .AppendLine()
            .AppendLine($"Found as variants of the same shop product as {knownCode}:")
            .AppendLine();
        foreach (var url in listings.Where(l => l.VariantEans.Count > 1).Select(l => l.Url).Distinct())
        {
            body.AppendLine($"- {url}");
        }

        return new Proposal(
            $"{GitHubRepository.BranchPrefix}{added[0]}",
            $"data: add language variants to {name}",
            body.ToString(),
            Table.Write(table),
            Draft: !autoMerge);
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
            yield return $"{Text.Code(entry.Name)}: the sources disagree ({string.Join(", ", entry.Counts)}); entered as `countCandidates`.";
        }

        if (entry.AlternativeBuild)
        {
            yield return $"{Text.Code(entry.Name)}: a source names an alternative build; counted as the build the box is sold as.";
        }

        if (entry.NonPlayer)
        {
            yield return $"{Text.Code(entry.Name)}: named as non-player models; role `npo` in Kill Team, `extra` in any other reading.";
        }
    }
}
