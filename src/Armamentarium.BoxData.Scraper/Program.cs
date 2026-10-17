using System.Runtime.InteropServices;

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

        // systemd stops the unit with SIGTERM; the run ends between two requests, the state of every finished product is saved.
        using var stop = new CancellationTokenSource();
        void Stop(PosixSignalContext context)
        {
            context.Cancel = true;
            stop.Cancel();
        }

        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);

        try
        {
            // GitHub's API and the catalogue on GitHub Pages: no redirect off the requested host, at most 64 MB
            // (products.json measured 13.6 MB).
            using var github = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(100),
                MaxResponseContentBufferSize = 64 * 1024 * 1024,
            };
            IRepository repository = new GitHubRepository(github, dryRun ? null : ReadToken());
            if (dryRun)
            {
                repository = new DryRunRepository(repository, Log);
            }

            var statePath = dryRun ? null : Path.Combine(Required("STATE_DIRECTORY"), "state.json");
            var state = statePath is null ? new ScanState() : ScanState.Load(statePath);
            var products = ProductCatalogue.Parse(await github.GetStringAsync(ProductCatalogue.ProductsUrl, stop.Token));
            Log($"{products.Count} Games Workshop products in the catalogue");

            using var fetcher = new Fetcher(Sources.All.ToDictionary(s => s.Host, s => s.Interval, StringComparer.OrdinalIgnoreCase), Log);
            await new Runner(repository, Sources.All, fetcher, state, Log, statePath is null ? null : () => state.Save(statePath))
                .RunAsync(products, DateTimeOffset.UtcNow, stop.Token, onlyCode);
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            Log("Run stopped; the next run continues from the saved state");
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
