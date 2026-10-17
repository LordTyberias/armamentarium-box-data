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

        public List<Proposal> Proposals { get; } = [];

        public Task<(string Text, string Sha)> GetTableAsync(CancellationToken cancellationToken) =>
            Task.FromResult((Table, "sha"));

        public Task<IReadOnlyList<string>> OpenScraperBranchesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Open);

        public Task ProposeAsync(Proposal proposal, string tableSha, CancellationToken cancellationToken)
        {
            if (Fail(proposal) is { } error)
            {
                throw error;
            }

            Proposals.Add(proposal);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedSource(string host, Func<Product, Listing?> find) : ISource
    {
        public string Host => host;

        public Task<Listing?> FindAsync(Product product, IFetcher fetcher, CancellationToken cancellationToken) =>
            Task.FromResult(find(product));
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
        IReadOnlyList<Product> products, IReadOnlyList<ISource> sources, FakeRepository? repo = null, ScanState? state = null)
    {
        repo ??= new FakeRepository();
        state ??= new ScanState();
        var log = new List<string>();
        await new Runner(repo, sources, new NoFetcher(), state, log.Add).RunAsync(products, Now, default);
        return (repo, state, log);
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
    public async Task Run_FailingSource_IsLoggedAndOthersStillCount()
    {
        var broken = new FixedSource("c.test", _ => throw new HttpRequestException("down"));

        var (repo, _, log) = await Run(
            [P("60010199002", "5011921000002")],
            [Shop("a.test", new Line(5, "Blade Guards", false, false)), Shop("b.test", new Line(5, "Blade Guards", false, false)), broken]);

        Assert.AreEqual(1, repo.Proposals.Count);
        Assert.IsTrue(log.Any(l => l.Contains("c.test", StringComparison.Ordinal)));
    }
}
