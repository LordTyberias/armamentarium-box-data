namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// Entry point of the daily run (ADR 0003 §2). systemd hands the token in through <c>LoadCredential</c>
/// (<c>$CREDENTIALS_DIRECTORY/github-token</c>) and the state directory through <c>$STATE_DIRECTORY</c>.
///
///   --dry-run        reads GitHub without a token, writes nothing, prints each proposal instead of opening it
///   --code &lt;number&gt;  searches this one article number only, whatever the state says (with --dry-run for a check)
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var dryRun = args.Contains("--dry-run");
        var codeAt = Array.IndexOf(args, "--code");
        var onlyCode = codeAt >= 0 && codeAt + 1 < args.Length ? args[codeAt + 1] : null;
        void Log(string line) => Console.WriteLine(line);

        try
        {
            using var github = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
            IRepository repository = new GitHubRepository(github, dryRun ? null : ReadToken());
            if (dryRun)
            {
                repository = new DryRunRepository(repository, Log);
            }

            var statePath = dryRun ? null : Path.Combine(Required("STATE_DIRECTORY"), "state.json");
            var state = statePath is null ? new ScanState() : ScanState.Load(statePath);
            var products = ProductCatalogue.Parse(await github.GetStringAsync(ProductCatalogue.ProductsUrl));
            Log($"{products.Count} Games Workshop products in the catalogue");

            using var fetcher = new Fetcher(Sources.All.Select(s => s.Host).ToHashSet(StringComparer.OrdinalIgnoreCase), Log);
            await new Runner(repository, Sources.All, fetcher, state, Log, statePath is null ? null : () => state.Save(statePath))
                .RunAsync(products, DateTimeOffset.UtcNow, CancellationToken.None, onlyCode);
            return 0;
        }
        catch (Exception ex)
        {
            Log($"Run failed: {ex}");
            return 1;
        }
    }

    private static string ReadToken()
    {
        var token = File.ReadAllText(Path.Combine(Required("CREDENTIALS_DIRECTORY"), "github-token")).Trim();
        return token.Length > 0 ? token : throw new InvalidOperationException("The github-token credential is empty.");
    }

    private static string Required(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{variable} is not set; the systemd unit provides it (deploy/).");

    /// <summary>Reads like the real repository, prints proposals instead of opening them.</summary>
    private sealed class DryRunRepository(IRepository inner, Action<string> log) : IRepository
    {
        public Task<(string Text, string Sha)> GetTableAsync(CancellationToken cancellationToken) =>
            inner.GetTableAsync(cancellationToken);

        public Task<IReadOnlyList<string>> OpenScraperBranchesAsync(CancellationToken cancellationToken) =>
            inner.OpenScraperBranchesAsync(cancellationToken);

        public Task ProposeAsync(Proposal proposal, string tableSha, CancellationToken cancellationToken)
        {
            log($"--- would open draft {proposal.Branch}: {proposal.Title}");
            log(proposal.Body);
            return Task.CompletedTask;
        }
    }
}
