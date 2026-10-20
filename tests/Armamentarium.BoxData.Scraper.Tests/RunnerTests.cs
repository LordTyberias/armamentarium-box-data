namespace Armamentarium.BoxData.Scraper.Tests;

[TestClass]
public sealed class RunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 3, 0, 0, TimeSpan.Zero);

    private const string Table = """
        {
          "$schema": "./box-contents.schema.json",
          "format": 1,
          "boxes": [
            {
              "name": "Kill Team: Known",
              "productCodes": [
                "60010199001"
              ],
              "sources": [
                "https://a.test/k",
                "https://b.test/k"
              ],
              "readings": [
                {
                  "reading": "kt",
                  "entries": [
                    {
                      "role": "team",
                      "name": "Seer",
                      "count": 1
                    }
                  ]
                }
              ]
            }
          ]
        }

        """;

    private sealed class FakeRepository(params string[] open) : IRepository
    {
        public List<string> Open { get; } = [.. open];

        public Func<Proposal, Exception?> Fail { get; init; } = _ => null;

        public Func<string, Exception?> FailMerge { get; init; } = _ => null;

        public List<Proposal> Proposals { get; } = [];

        public List<string> Merged { get; } = [];

        public List<string> ShasProposedOn { get; } = [];

        private string _table = Table;

        public string Text
        {
            init => _table = value;
        }

        public Task<(string Text, string Sha)> GetTableAsync(CancellationToken cancellationToken) =>
            Task.FromResult((_table, $"sha{Merged.Count}"));

        public Task<string?> GetAliasesAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> OpenScraperBranchesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Open);

        public Task<int> ProposeAsync(Proposal proposal, string tableSha, CancellationToken cancellationToken)
        {
            if (Fail(proposal) is { } error)
            {
                throw error;
            }

            Proposals.Add(proposal);
            ShasProposedOn.Add(tableSha);
            return Task.FromResult(Proposals.Count);
        }

        public Task MergeAsync(int number, CancellationToken cancellationToken)
        {
            var branch = Proposals[number - 1].Branch;
            if (FailMerge(branch) is { } error)
            {
                throw error;
            }

            _table = Proposals[number - 1].Table;
            Merged.Add(branch);
            return Task.CompletedTask;
        }

        public Func<string, Exception?> FailDelete { get; init; } = _ => null;

        public Task DeleteBranchAsync(string branch, CancellationToken cancellationToken) =>
            FailDelete(branch) is { } error ? Task.FromException(error) : Task.CompletedTask;
    }

    private sealed class FixedSource(string host, Func<Product, Listing?> find, TimeSpan? interval = null) : ISource
    {
        public int Asked { get; private set; }

        public string Host => host;

        public TimeSpan Interval => interval ?? TimeSpan.Zero;

        public Task<Listing?> FindAsync(Product product, IFetcher fetcher, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult(find(product));
        }
    }

    private sealed class NoFetcher : IFetcher
    {
        public Task<string?> GetAsync(string url, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private static Product P(string code, string ean, string name = "KILL TEAM: NEW") =>
        new(code, ean, name, ["Kill Team"], "current", "miniatures");

    private static ISource Shop(string host, params Line[] lines) =>
        new FixedSource(host, p => new Listing(host, $"https://{host}/{p.Code}", false, "Kill Team: New", lines, [p.Ean!]));

    private static async Task<(FakeRepository Repo, ScanState State, List<string> Log)> Run(
        IReadOnlyList<Product> products,
        IReadOnlyList<ISource> sources,
        FakeRepository? repo = null,
        ScanState? state = null,
        bool autoMerge = false,
        CatalogueIndex? catalogues = null,
        List<string>? notices = null)
    {
        repo ??= new FakeRepository();
        state ??= new ScanState();
        var log = new List<string>();
        await new Runner(repo, sources, new NoFetcher(), state, log.Add, autoMerge: autoMerge, catalogues: catalogues, notice: notices is null ? null : notices.Add)
            .RunAsync(products, Now, default);
        return (repo, state, log);
    }

    private static ISource[] TwoShops(params Line[] lines) => [Shop("a.test", lines), Shop("b.test", lines)];

    [TestMethod]
    public async Task Run_WithoutAutoMerge_OpensDraftsAndMergesNothing()
    {
        var (repo, _, _) = await Run([P("60010199002", "5011921000002")], TwoShops(new Line(5, "Blade Guards", false, false)));

        Assert.IsTrue(repo.Proposals.Single().Draft);
        Assert.AreEqual(0, repo.Merged.Count);
    }

    [TestMethod]
    public async Task Run_AutoMerge_MergesEachBoxAndBuildsTheNextOnTheMergedTable()
    {
        var (repo, _, _) = await Run(
            [P("60010199002", "5011921000002"), P("60010199003", "5011921000003")],
            TwoShops(new Line(5, "Blade Guards", false, false)),
            autoMerge: true);

        CollectionAssert.AreEqual(new[] { "scraper/60010199002", "scraper/60010199003" }, repo.Merged);
        Assert.IsFalse(repo.Proposals[0].Draft);
        StringAssert.Contains(repo.Proposals[0].Body, "merged by the scraper service");
        StringAssert.Contains(repo.Proposals[1].Table, "\"60010199002\"");
        CollectionAssert.AreEqual(new[] { "sha0", "sha1" }, repo.ShasProposedOn);
    }

    [TestMethod]
    public async Task Run_AutoMerge_KeepsGoingPastFiveMergedBoxes()
    {
        var products = Enumerable.Range(10, 7).Select(i => P($"600101990{i}", $"50119210000{i}")).ToList();

        var (repo, _, _) = await Run(products, TwoShops(new Line(5, "Blade Guards", false, false)), autoMerge: true);

        Assert.AreEqual(7, repo.Merged.Count);
    }

    [TestMethod]
    public async Task Run_MergeRefused_LeavesThePullRequestOpenAndLogs()
    {
        var repo = new FakeRepository { FailMerge = _ => new GitHubException("GitHub PUT pulls/1/merge answered 405") };

        var (_, state, log) = await Run(
            [P("60010199002", "5011921000002")], TwoShops(new Line(5, "Blade Guards", false, false)), repo, autoMerge: true);

        Assert.AreEqual(0, repo.Merged.Count);
        Assert.IsTrue(log.Any(l => l.Contains("405", StringComparison.Ordinal)));
        Assert.IsFalse(state.IsDue("60010199002", Now));
    }

    [TestMethod]
    public async Task Run_MergeRefused_IsANotice()
    {
        var repo = new FakeRepository { FailMerge = _ => new GitHubException("answered 405") };
        var notices = new List<string>();

        await Run(
            [P("60010199002", "5011921000002")], TwoShops(new Line(5, "Blade Guards", false, false)), repo, autoMerge: true,
            notices: notices);

        Assert.AreEqual("scraper/60010199002 not merged: answered 405", notices.Single());
    }

    [TestMethod]
    public async Task Run_ProposalRefused_IsANotice()
    {
        var repo = new FakeRepository { Fail = _ => new GitHubException("answered 403") };
        var notices = new List<string>();

        await Run([P("60010199002", "5011921000002")], TwoShops(new Line(5, "Blade Guards", false, false)), repo, notices: notices);

        Assert.AreEqual("scraper/60010199002 not proposed: answered 403", notices.Single());
    }

    [TestMethod]
    public async Task Run_MergedBox_IsNoNotice()
    {
        var notices = new List<string>();

        await Run(
            [P("60010199002", "5011921000002")], TwoShops(new Line(5, "Blade Guards", false, false)), autoMerge: true,
            catalogues: Blades(), notices: notices);

        Assert.AreEqual(0, notices.Count);
    }

    [TestMethod]
    public async Task Run_FiveRefusedMerges_StopTheRun()
    {
        var repo = new FakeRepository { FailMerge = _ => new GitHubException("answered 405") };
        var products = Enumerable.Range(10, 6).Select(i => P($"600101990{i}", $"50119210000{i}")).ToList();

        await Run(products, TwoShops(new Line(5, "Blade Guards", false, false)), repo, autoMerge: true);

        Assert.AreEqual(5, repo.Proposals.Count);
    }

    [TestMethod]
    public async Task Run_MergedButBranchNotDeleted_CountsAsMergedAndReadsTheTable()
    {
        var repo = new FakeRepository { FailDelete = _ => new GitHubException("answered 422") };

        var (_, _, log) = await Run(
            [P("60010199002", "5011921000002"), P("60010199003", "5011921000003")],
            TwoShops(new Line(5, "Blade Guards", false, false)),
            repo,
            autoMerge: true);

        Assert.AreEqual(2, repo.Merged.Count);
        StringAssert.Contains(repo.Proposals[1].Table, "\"60010199002\"");
        Assert.IsTrue(log.Any(l => l.Contains("branch not deleted", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Run_CatalogueHit_WritesTheIdsAndAMissKeepsTheName()
    {
        var catalogues = new CatalogueIndex();
        catalogues.AddCatalogue("kt", "Blades.cat", System.Text.Encoding.UTF8.GetBytes(
            """<catalogue id="cat-blades" name="Blades" library="false"><selectionEntries><selectionEntry type="model" id="se-guard" name="Blade Guard"/></selectionEntries></catalogue>"""));

        var (repo, _, _) = await Run(
            [P("60010199002", "5011921000002")],
            TwoShops(new Line(5, "Blade Guards", false, false), new Line(1, "Seer of Nowhere", false, false)),
            autoMerge: true,
            catalogues: catalogues);

        var table = repo.Proposals.Single().Table;
        StringAssert.Contains(table, "\"catalogueId\": \"cat-blades\"");
        StringAssert.Contains(table, "\"entryId\": \"se-guard\"");
        Assert.AreEqual(1, table.Split("\"entryId\"").Length - 1);
    }

    [TestMethod]
    public async Task Run_BuildSentenceNamingARootEntry_WinsOverTheSplitLines()
    {
        var catalogues = new CatalogueIndex();
        catalogues.AddCatalogue("kt", "Blades.cat", System.Text.Encoding.UTF8.GetBytes(
            """<catalogue id="cat-blades" name="Blades" library="false"><selectionEntries><selectionEntry type="unit" id="se-boarders" name="Navy Boarders"/></selectionEntries></catalogue>"""));
        ISource SplitShop(string host) => new FixedSource(host, p => new Listing(
            host, $"https://{host}/{p.Code}", false, "Navy Boarders",
            [new Line(1, "Navy Sergeant", false, false), new Line(9, "Navy Armsmen", false, false)], [p.Ean!])
        {
            Sentences = BuildSentences.Parse("<p>This kit builds a squad of 10 Navy Boarders.</p>"),
        });

        var (repo, _, _) = await Run(
            [P("60010199002", "5011921000002")], [SplitShop("a.test"), SplitShop("b.test")], autoMerge: true, catalogues: catalogues);

        var table = repo.Proposals.Single().Table;
        StringAssert.Contains(table, "\"entryId\": \"se-boarders\"");
        StringAssert.Contains(table, "\"count\": 10");
        Assert.IsFalse(table.Contains("Navy Sergeant", StringComparison.Ordinal));
    }

    // The known box with a Seer whose ids name an entry the catalogue no longer has at its root.
    private static readonly string OrphanTable = Table.Replace(
        "\"role\": \"team\",", "\"role\": \"team\",\n              \"catalogueId\": \"cat-blades\",\n              \"entryId\": \"se-gone\",");

    private static CatalogueIndex Blades()
    {
        var catalogues = new CatalogueIndex();
        catalogues.AddCatalogue("kt", "Blades.cat", System.Text.Encoding.UTF8.GetBytes(
            """<catalogue id="cat-blades" name="Blades" library="false"><selectionEntries><selectionEntry type="model" id="se-guard" name="Blade Guard"/></selectionEntries></catalogue>"""));
        return catalogues;
    }

    [TestMethod]
    public async Task Run_OrphanedId_OpensADraftThatIsNeverMerged()
    {
        var (repo, _, log) = await Run(
            [], [], new FakeRepository { Text = OrphanTable }, autoMerge: true, catalogues: Blades());

        var proposal = repo.Proposals.Single();
        Assert.AreEqual(Runner.StaleIdsBranch, proposal.Branch);
        Assert.IsTrue(proposal.Draft);
        Assert.AreEqual(0, repo.Merged.Count);
        Assert.IsFalse(proposal.Table.Contains("se-gone", StringComparison.Ordinal));
        StringAssert.Contains(proposal.Table, "\"name\": \"Seer\"");
        Assert.IsTrue(log.Any(l => l.Contains("se-gone", StringComparison.Ordinal) && l.Contains("orphaned", StringComparison.Ordinal)));
        Assert.IsTrue(log.Any(l => l.StartsWith("id check: 1 pair(s), 0 valid, 1 orphaned", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Run_OrphanedId_IsANoticeWithItsDraft()
    {
        var notices = new List<string>();

        await Run([], [], new FakeRepository { Text = OrphanTable }, autoMerge: true, catalogues: Blades(), notices: notices);

        Assert.AreEqual(2, notices.Count);
        StringAssert.StartsWith(notices[0], "id check: 1 pair(s), 0 valid, 1 orphaned");
        Assert.AreEqual($"proposed {Runner.StaleIdsBranch}: 1 orphaned pair(s)", notices[1]);
    }

    [TestMethod]
    public async Task Run_UncheckedReading_IsANotice()
    {
        var notices = new List<string>();

        await Run([], [], new FakeRepository { Text = OrphanTable }, catalogues: new CatalogueIndex(), notices: notices);

        StringAssert.EndsWith(notices.Single(), "unchecked: kt");
    }

    [TestMethod]
    public async Task Run_OrphanedIdWithoutASlot_IsANotice()
    {
        var open = Enumerable.Range(1, Runner.MaxOpen).Select(i => $"scraper/6001019900{i}").ToArray();
        var notices = new List<string>();

        await Run([], [], new FakeRepository(open) { Text = OrphanTable }, catalogues: Blades(), notices: notices);

        Assert.IsTrue(notices.Any(n => n.Contains("no slot", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Run_OrphanedIdWithItsDraftOpen_ProposesNothingAgain()
    {
        var (repo, _, log) = await Run(
            [], [], new FakeRepository(Runner.StaleIdsBranch) { Text = OrphanTable }, catalogues: Blades());

        Assert.AreEqual(0, repo.Proposals.Count);
        Assert.IsTrue(log.Any(l => l.Contains("already open", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Run_FiveOpen_StillChecksButOpensNoDraft()
    {
        var open = Enumerable.Range(1, Runner.MaxOpen).Select(i => $"scraper/6001019900{i}").ToArray();
        var (repo, _, log) = await Run(
            [P("60010199009", "5011921000009")], TwoShops(new Line(5, "Blade Guards", false, false)),
            new FakeRepository(open) { Text = OrphanTable }, catalogues: Blades());

        Assert.AreEqual(0, repo.Proposals.Count);
        Assert.IsTrue(log.Any(l => l.Contains("no slot", StringComparison.Ordinal)));
        Assert.IsTrue(log.Any(l => l.Contains("nothing to do", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Run_LeftoverStaleIdsBranch_IsDeletedAndTheDraftProposedOnce()
    {
        var failed = false;
        var repo = new FakeRepository
        {
            Text = OrphanTable,
            Fail = p => p.Branch == Runner.StaleIdsBranch && !failed && (failed = true) ? new GitHubException("answered 422") : null,
        };

        var (_, _, log) = await Run([], [], repo, catalogues: Blades());

        Assert.AreEqual(Runner.StaleIdsBranch, repo.Proposals.Single().Branch);
        Assert.IsTrue(log.Any(l => l.Contains("deleting a leftover branch", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Run_StaleIdsDraftFailingTwice_IsLoggedAndTheRunGoesOn()
    {
        var repo = new FakeRepository
        {
            Text = OrphanTable,
            Fail = p => p.Branch == Runner.StaleIdsBranch ? new GitHubException("answered 403") : null,
        };

        var (_, _, log) = await Run(
            [P("60010199009", "5011921000009")], TwoShops(new Line(5, "Blade Guards", false, false)), repo, catalogues: Blades());

        Assert.IsTrue(log.Any(l => l == $"{Runner.StaleIdsBranch} not proposed: answered 403"));
        Assert.AreEqual("scraper/60010199009", repo.Proposals.Single().Branch);
    }

    [TestMethod]
    public async Task Run_RenamedId_IsReportedAndProposesNothing()
    {
        var (repo, _, log) = await Run(
            [], [], new FakeRepository { Text = OrphanTable.Replace("se-gone", "se-guard") }, catalogues: Blades());

        Assert.AreEqual(0, repo.Proposals.Count);
        Assert.IsTrue(log.Any(l => l.Contains("se-guard renamed", StringComparison.Ordinal)));
        Assert.IsTrue(log.Any(l => l.StartsWith("id check: 1 pair(s), 0 valid, 0 orphaned, 1 renamed", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Run_NoOrphanedId_ProposesNothingForTheCheck()
    {
        var (repo, _, log) = await Run([], [], catalogues: Blades());

        Assert.AreEqual(0, repo.Proposals.Count);
        Assert.IsTrue(log.Any(l => l.StartsWith("id check: 0 pair(s)", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Run_DraftWithoutAutoMerge_CarriesNoIds()
    {
        var catalogues = new CatalogueIndex();
        catalogues.AddCatalogue("kt", "Blades.cat", System.Text.Encoding.UTF8.GetBytes(
            """<catalogue id="cat-blades" name="Blades" library="false"><selectionEntries><selectionEntry type="model" id="se-guard" name="Blade Guard"/></selectionEntries></catalogue>"""));

        var (repo, _, _) = await Run(
            [P("60010199002", "5011921000002")], TwoShops(new Line(5, "Blade Guards", false, false)), catalogues: catalogues);

        Assert.IsFalse(repo.Proposals.Single().Table.Contains("\"entryId\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Run_TwoAgreeingShops_ProposeDraftForTheBox()
    {
        var (repo, state, _) = await Run(
            [P("60010199002", "5011921000002")],
            [Shop("a.test", new Line(5, "Blade Guards", false, false)), Shop("b.test", new Line(5, "Blade Guards", false, false))]);

        var proposal = repo.Proposals.Single();
        Assert.AreEqual("scraper/60010199002", proposal.Branch);
        Assert.AreEqual("data: add Kill Team: New", proposal.Title);
        StringAssert.Contains(proposal.Table, "\"60010199002\"");
        StringAssert.Contains(proposal.Table, "\"Blade Guards\"");
        StringAssert.Contains(proposal.Body, "https://a.test/60010199002");
        Assert.IsFalse(state.IsDue("60010199002", Now));
    }

    [TestMethod]
    public async Task Run_OneShopOnly_ProposesNothingAndWaits()
    {
        var (repo, state, log) = await Run(
            [P("60010199002", "5011921000002")], [Shop("a.test", new Line(5, "Blade Guards", false, false))]);

        Assert.AreEqual(0, repo.Proposals.Count);
        Assert.IsFalse(state.IsDue("60010199002", Now));
        Assert.IsTrue(log.Any(l => l.Contains("60010199002", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Run_FiveOpenDrafts_ProposesNothing()
    {
        var repo = new FakeRepository("scraper/1", "scraper/2", "scraper/3", "scraper/4", "scraper/5");

        var (_, state, _) = await Run(
            [P("60010199002", "5011921000002")],
            [Shop("a.test", new Line(5, "Blade Guards", false, false)), Shop("b.test", new Line(5, "Blade Guards", false, false))],
            repo);

        Assert.AreEqual(0, repo.Proposals.Count);
        Assert.IsTrue(state.IsDue("60010199002", Now));
    }

    [TestMethod]
    public async Task Run_StopsAtFiveOpenDrafts()
    {
        var products = Enumerable.Range(2, 8).Select(i => P($"6001019900{i}", $"501192100000{i}")).ToArray();

        var (repo, _, _) = await Run(
            products,
            [Shop("a.test", new Line(5, "Blade Guards", false, false)), Shop("b.test", new Line(5, "Blade Guards", false, false))],
            new FakeRepository("scraper/x"));

        Assert.AreEqual(4, repo.Proposals.Count);
    }

    [TestMethod]
    public async Task Run_ProductNotDue_IsNotSearched()
    {
        var state = new ScanState();
        state.Mark("60010199002", Now.AddDays(-1));

        var (repo, _, _) = await Run(
            [P("60010199002", "5011921000002")],
            [new FixedSource("a.test", _ => throw new AssertFailedException("searched"))],
            state: state);

        Assert.AreEqual(0, repo.Proposals.Count);
    }

    [TestMethod]
    public async Task Run_VariantOfKnownBox_ProposesLanguageVariant()
    {
        var known = P("60010199001", "5011921000001", "KILL TEAM: KNOWN (ENGLISH)");
        var french = P("01010199001", "5011921000011", "KILL TEAM: KNOWN (FRENCH)");
        var shop = new FixedSource("a.test", p => new Listing("a.test", "https://a.test/known", false, "Known", [], [known.Ean!, french.Ean!]));

        var (repo, _, _) = await Run([known, french], [shop]);

        var proposal = repo.Proposals.Single();
        Assert.AreEqual("scraper/01010199001", proposal.Branch);
        Assert.AreEqual("data: add language variants to Kill Team: Known", proposal.Title);
        StringAssert.Contains(proposal.Table, "\"01010199001\"");
        StringAssert.Contains(proposal.Body, "https://a.test/known");
    }

    [TestMethod]
    public async Task Run_CombatPatrol_ProposesGenerationFromStatus()
    {
        var patrol = new Product("99120101001", "5011921000003", "COMBAT PATROL: X", ["Warhammer 40,000"], "discontinued", "miniatures");

        var (repo, _, _) = await Run([patrol], [Shop("a.test"), Shop("b.test")]);

        var proposal = repo.Proposals.Single();
        StringAssert.Contains(proposal.Table, "\"generation\": \"legacy\"");
        StringAssert.Contains(proposal.Body, "generation");
    }

    [TestMethod]
    public async Task Run_SourcesFailWithTooFewListings_ProductStaysDue()
    {
        var failing = new FixedSource("c.test", _ => throw new FetchFailedException("c.test answered 429"));

        var (repo, state, _) = await Run(
            [P("60010199002", "5011921000002")], [Shop("a.test", new Line(5, "Blade Guards", false, false)), failing]);

        Assert.AreEqual(0, repo.Proposals.Count);
        Assert.IsFalse(state.IsDue("60010199002", Now));
        Assert.IsTrue(state.IsDue("60010199002", Now.AddDays(3)));
    }

    [TestMethod]
    public async Task Run_UnreadableSource_CountsAsAnAnswer()
    {
        var unreadable = new FixedSource("c.test", _ => throw new System.Text.Json.JsonException("not json"));

        var (_, state, log) = await Run(
            [P("60010199002", "5011921000002")], [Shop("a.test", new Line(5, "Blade Guards", false, false)), unreadable]);

        Assert.IsFalse(state.IsDue("60010199002", Now.AddDays(3)));
        Assert.IsTrue(log.Any(l => l.Contains("c.test", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Run_FailedProposal_IsLoggedAndTheRunGoesOn()
    {
        var repo = new FakeRepository
        {
            Fail = p => p.Branch == "scraper/60010199002" ? new GitHubException("GitHub POST answered 422") : null,
        };

        var (_, state, log) = await Run(
            [P("60010199002", "5011921000002"), P("60010199003", "5011921000003")],
            [Shop("a.test", new Line(5, "Blade Guards", false, false)), Shop("b.test", new Line(5, "Blade Guards", false, false))],
            repo);

        Assert.AreEqual("scraper/60010199003", repo.Proposals.Single().Branch);
        Assert.IsTrue(log.Any(l => l.Contains("422", StringComparison.Ordinal)));
        Assert.IsTrue(state.IsDue("60010199002", Now.AddDays(3)));
    }

    [TestMethod]
    public async Task Run_OnlyGermanTitles_UsesTheCatalogueNameAndSaysSo()
    {
        ISource German(string host) => new FixedSource(host, p =>
            new Listing(host, $"https://{host}/{p.Code}", true, "Kill Team: Neues", [new Line(5, "Klingen", false, false)], [p.Ean!]));
        var english = new FixedSource("a.test", p =>
            new Listing("a.test", $"https://a.test/{p.Code}", false, "", [new Line(5, "Blade Guards", false, false)], [p.Ean!]));

        var (repo, _, _) = await Run([P("60010199002", "5011921000002", "KILL TEAM: NEW (ENGLISH)")], [english, German("b.test")]);

        var proposal = repo.Proposals.Single();
        Assert.AreEqual("data: add KILL TEAM: NEW (ENGLISH)", proposal.Title);
        StringAssert.Contains(proposal.Body, "catalogue name");
    }

    [TestMethod]
    public async Task Run_TitleOfAListingOutsideTheSources_NeverNamesTheBox()
    {
        // A shop that matched the EAN without a content list confirms nothing; its title must not name the box (ARMAM-601).
        var stray = new FixedSource("stray.test", p =>
            new Listing("stray.test", $"https://stray.test/{p.Code}", false, "Horus Heresy: False Gods (PB)", [], [p.Ean!]));

        var (repo, _, _) = await Run(
            [P("60010199002", "5011921000002")], [stray, .. TwoShops(new Line(5, "Blade Guards", false, false))]);

        Assert.AreEqual("data: add Kill Team: New", repo.Proposals.Single().Title);
    }

    [TestMethod]
    public async Task Run_ShopText_CannotFormatTheBody()
    {
        ISource Shop2(string host) => new FixedSource(host, p => new Listing(
            host, $"https://{host}/{p.Code}", false, "Box [x](https://evil.test) @someone",
            [new Line(5, "Guards @someone #1", false, false)], [p.Ean!]));

        var (repo, _, _) = await Run([P("60010199002", "5011921000002")], [Shop2("a.test"), Shop2("b.test")]);

        var body = repo.Proposals.Single().Body;
        StringAssert.Contains(body, "`Guards @someone #1`");
        StringAssert.Contains(body, "`Box [x](https://evil.test) @someone`");
    }

    [TestMethod]
    public async Task Run_SlowSource_IsAskedOnlyAfterAFastFinding()
    {
        var slow = new FixedSource("slow.test", p => new Listing(
            "slow.test", $"https://slow.test/{p.Code}", false, "Kill Team: New", [new Line(5, "Blade Guards", false, false)], []),
            TimeSpan.FromSeconds(60));
        var nothing = new FixedSource("a.test", _ => null);

        await Run([P("60010199002", "5011921000002")], [nothing, slow]);
        Assert.AreEqual(0, slow.Asked);

        var (repo, _, _) = await Run([P("60010199002", "5011921000002")], [Shop("b.test", new Line(5, "Blade Guards", false, false)), slow]);
        Assert.AreEqual(1, slow.Asked);
        Assert.AreEqual(1, repo.Proposals.Count);
    }

    [TestMethod]
    public async Task Run_SlowSource_IsNotAskedWhenTheFastOnesSuffice()
    {
        var slow = new FixedSource("slow.test", _ => throw new AssertFailedException("asked"), TimeSpan.FromSeconds(60));

        var (repo, _, _) = await Run(
            [P("60010199002", "5011921000002")],
            [Shop("a.test", new Line(5, "Blade Guards", false, false)), Shop("b.test", new Line(5, "Blade Guards", false, false)), slow]);

        Assert.AreEqual(0, slow.Asked);
        Assert.AreEqual(1, repo.Proposals.Count);
    }

    [TestMethod]
    public async Task Run_FailingSource_IsLoggedAndOthersStillCount()
    {
        var broken = new FixedSource("c.test", _ => throw new HttpRequestException("down"));

        var (repo, _, log) = await Run(
            [P("60010199002", "5011921000002")],
            [Shop("a.test", new Line(5, "Blade Guards", false, false)), Shop("b.test", new Line(5, "Blade Guards", false, false)), broken]);

        Assert.AreEqual(1, repo.Proposals.Count);
        Assert.IsTrue(log.Any(l => l.Contains("c.test", StringComparison.Ordinal)));
    }

    private static FixedSource FailsOnceThenFinds(string host, Exception failure, params Line[] lines)
    {
        var failed = new HashSet<string>(StringComparer.Ordinal);
        return new FixedSource(host, p => failed.Add(p.Code)
            ? throw failure
            : new Listing(host, $"https://{host}/{p.Code}", false, "Kill Team: New", lines, [p.Ean!]));
    }

    [TestMethod]
    public async Task Run_ThrottledShop_IsAskedAgainAtTheEndOfTheRun()
    {
        var line = new Line(5, "Blade Guards", false, false);
        var throttled = FailsOnceThenFinds("c.test", new FetchFailedException("c.test answered 429"), line);

        var (repo, _, log) = await Run([P("60010199002", "5011921000002")], [Shop("a.test", line), throttled]);

        Assert.AreEqual(2, throttled.Asked);
        Assert.AreEqual(1, repo.Proposals.Count);
        Assert.IsTrue(log.Any(l => l.Contains("asking 1 product(s) again", StringComparison.Ordinal)));
        Assert.IsTrue(log.Any(l => l.Contains("catch-up: 1 asked again, 0 still without an answer", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Run_RefusingShop_IsNotAskedAgainInTheRun()
    {
        var line = new Line(5, "Blade Guards", false, false);
        var refusing = FailsOnceThenFinds("c.test", new FetchFailedException("c.test answered 403", canRetryInRun: false), line);

        var (repo, state, _) = await Run([P("60010199002", "5011921000002")], [Shop("a.test", line), refusing]);

        Assert.AreEqual(1, refusing.Asked);
        Assert.AreEqual(0, repo.Proposals.Count);
        Assert.IsTrue(state.IsDue("60010199002", Now.AddDays(3)));
    }

    [TestMethod]
    public async Task Run_ThrottledShopThatStaysThrottled_WaitsThreeDays()
    {
        var throttled = new FixedSource("c.test", _ => throw new FetchFailedException("c.test answered 429"));

        var (repo, state, log) = await Run(
            [P("60010199002", "5011921000002")], [Shop("a.test", new Line(5, "Blade Guards", false, false)), throttled]);

        Assert.AreEqual(2, throttled.Asked);
        Assert.AreEqual(0, repo.Proposals.Count);
        Assert.IsFalse(state.IsDue("60010199002", Now));
        Assert.IsTrue(state.IsDue("60010199002", Now.AddDays(3)));
        Assert.IsTrue(log.Any(l => l.Contains("catch-up: 1 asked again, 1 still without an answer", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Run_StoppedAtFiveOpen_DoesNotCatchUp()
    {
        var line = new Line(5, "Blade Guards", false, false);
        var throttled = new FixedSource("c.test", p => p.Code == "60010199002"
            ? throw new FetchFailedException("c.test answered 429")
            : new Listing("c.test", $"https://c.test/{p.Code}", false, "Kill Team: New", [line], [p.Ean!]));

        var (repo, _, log) = await Run(
            [P("60010199002", "5011921000002"), P("60010199003", "5011921000003")],
            [Shop("a.test", line), throttled],
            new FakeRepository("scraper/1", "scraper/2", "scraper/3", "scraper/4"));

        Assert.AreEqual(1, repo.Proposals.Count);
        Assert.AreEqual("scraper/60010199003", repo.Proposals[0].Branch);
        Assert.IsFalse(log.Any(l => l.Contains("catch-up", StringComparison.Ordinal)));
    }

    // The known box, written by the service from the shops a.test and b.test, with a gap the catalogue now names.
    private static readonly string GapTable = Table.Replace("\"Seer\"", "\"Blade Guards\"");

    [TestMethod]
    public async Task Run_GapWithAHit_IsBackfilledAndMergedBeforeTheBoxes()
    {
        var (repo, _, log) = await Run(
            [P("60010199002", "5011921000002")], TwoShops(new Line(5, "Blade Guards", false, false)),
            new FakeRepository { Text = GapTable }, autoMerge: true, catalogues: Blades());

        Assert.AreEqual(Runner.BackfillIdsBranch, repo.Proposals[0].Branch);
        Assert.IsFalse(repo.Proposals[0].Draft);
        StringAssert.Contains(repo.Proposals[0].Table, "\"entryId\": \"se-guard\"");
        StringAssert.Contains(repo.Proposals[0].Body, "Blade Guards");
        CollectionAssert.AreEqual(new[] { Runner.BackfillIdsBranch, "scraper/60010199002" }, repo.Merged);
        CollectionAssert.AreEqual(new[] { "sha0", "sha1" }, repo.ShasProposedOn);
        Assert.IsTrue(log.Contains($"merged {Runner.BackfillIdsBranch}"));
    }

    [TestMethod]
    public async Task Run_BackfillWithoutAutoMerge_IsADraft()
    {
        var (repo, _, _) = await Run([], TwoShops(), new FakeRepository { Text = GapTable }, catalogues: Blades());

        Assert.AreEqual(Runner.BackfillIdsBranch, repo.Proposals.Single().Branch);
        Assert.IsTrue(repo.Proposals.Single().Draft);
        Assert.AreEqual(0, repo.Merged.Count);
    }

    [TestMethod]
    public async Task Run_GapOfAHandBox_IsNotBackfilled()
    {
        var (repo, _, _) = await Run([], [], new FakeRepository { Text = GapTable }, autoMerge: true, catalogues: Blades());

        Assert.AreEqual(0, repo.Proposals.Count);
    }

    [TestMethod]
    public async Task Run_NameReadBeforeTheFix_OpensADraftThatIsNeverMerged()
    {
        var (repo, _, _) = await Run(
            [], TwoShops(), new FakeRepository { Text = Table.Replace("\"Seer\"", "\"Seers accompanied by\"") },
            autoMerge: true, catalogues: Blades());

        var proposal = repo.Proposals.Single();
        Assert.AreEqual(Runner.FixNamesBranch, proposal.Branch);
        Assert.IsTrue(proposal.Draft);
        Assert.AreEqual(0, repo.Merged.Count);
        StringAssert.Contains(proposal.Table, "\"name\": \"Seers\"");
        StringAssert.Contains(proposal.Body, "Seers accompanied by");
    }

    [TestMethod]
    public async Task Run_UpgradeSetInTheTable_IsRemovedAndSaidNotToComeBack()
    {
        var (repo, _, _) = await Run(
            [], TwoShops(), new FakeRepository { Text = Table.Replace("\"Kill Team: Known\"", "\"Kill Team: Known Upgrades\"") },
            autoMerge: true, catalogues: Blades());

        var proposal = repo.Proposals.Single();
        Assert.AreEqual(Runner.FixNamesBranch, proposal.Branch);
        StringAssert.Contains(proposal.Body, "Upgrade set removed, not proposed again: `Kill Team: Known Upgrades`");
    }

    // Cain and Jurgen: a build of names, the second an alias of the first's entry, stays two lines with the same ids
    // (ADR 0004 and ADR 0003 Addenda ARMAM-642).
    [TestMethod]
    public async Task Run_BuildOfNamesWithAnAlias_ProposesEachModelWithTheIds()
    {
        var catalogues = new CatalogueIndex();
        catalogues.AddCatalogue("kt", "Heroes.cat", System.Text.Encoding.UTF8.GetBytes(
            """<catalogue id="cat-heroes" name="Heroes" library="false"><selectionEntries><selectionEntry type="model" id="se-hero" name="Iron Hero"/></selectionEntries></catalogue>"""));
        catalogues.UseAliases([new Alias("kt", "Rust Aide", "Iron Hero", "Heroes")]);
        ISource NamesShop(string host) => new FixedSource(host, p => new Listing(host, $"https://{host}/{p.Code}", false, "Iron Hero", [], [p.Ean!])
        {
            Sentences = BuildSentences.Parse("<p>This multipart plastic kit builds Iron Hero and Rust Aide.</p>"),
        });

        var (repo, _, _) = await Run(
            [P("60010199002", "5011921000002")], [NamesShop("a.test"), NamesShop("b.test")], autoMerge: true, catalogues: catalogues);

        var table = repo.Proposals.Single().Table;
        StringAssert.Contains(table, "\"name\": \"Iron Hero\"");
        StringAssert.Contains(table, "\"name\": \"Rust Aide\"");
        Assert.AreEqual(2, table.Split("\"entryId\": \"se-hero\"").Length - 1);
    }
}
