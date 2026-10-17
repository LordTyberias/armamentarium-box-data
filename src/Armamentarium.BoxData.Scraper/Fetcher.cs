using System.Net;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// The only way to a shop: allowlisted hosts, https, at most one request per five seconds and host, redirects only within
/// the host (ADR 0003 §5–§7). An answer that is not 200 is no finding (null) — a captcha or challenge is logged, never worked
/// around. A shop that does not answer (network, timeout, 429, 5xx) throws <see cref="FetchFailedException"/>, so the run
/// can tell "not listed" from "not asked"; after <see cref="MaxFailuresInARow"/> such failures the host rests for the run.
/// A page that says the host has restricted this client rests the host at once.
/// </summary>
public sealed class Fetcher : IFetcher, IDisposable
{
    // The service's own name, no contact address (ADR 0003 §7). A browser string was measured to draw 429 from Shopify.
    public const string UserAgent = "ABD/1.0";

    public const int MaxFailuresInARow = 20;

    private const int MaxRedirects = 3;

    private readonly HttpClient _http;
    // What a host serves instead of the page once it has restricted this client (measured: miniset.net, 2026-10-17).
    private static readonly string[] BlockMarkers = ["temporarily restricted", "suspicious automated activity"];

    // Block pages measured at 1.3 kB; the smallest shop answer that carries data is a short JSON, never a whole page.
    private const int BlockPageMaxLength = 16 * 1024;

    private readonly Action<string> _log;
    private readonly Dictionary<string, HostGate> _gates;

    /// <param name="hosts">Each allowlisted host with its least interval between two requests.</param>
    public Fetcher(IReadOnlyDictionary<string, TimeSpan> hosts, Action<string> log)
        : this(hosts, log, new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        })
    {
    }

    internal Fetcher(IReadOnlyDictionary<string, TimeSpan> hosts, Action<string> log, HttpMessageHandler handler)
    {
        _log = log;
        _gates = hosts.ToDictionary(h => h.Key, h => new HostGate(h.Value), StringComparer.OrdinalIgnoreCase);
        _http = new HttpClient(handler)
        {
            // A shop that hangs would hold every product of the run; 20 s is four times the slowest page measured.
            Timeout = TimeSpan.FromSeconds(20),
            // The largest shop page measured is 1.4 MB.
            MaxResponseContentBufferSize = 16 * 1024 * 1024,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-GB,en;q=0.9,de;q=0.8");
    }

    public static TimeSpan DelayBefore(DateTimeOffset? last, DateTimeOffset now, TimeSpan interval)
    {
        if (last is null)
        {
            return TimeSpan.Zero;
        }

        var rest = interval - (now - last.Value);
        return rest > TimeSpan.Zero ? rest : TimeSpan.Zero;
    }

    public async Task<string?> GetAsync(string url, CancellationToken cancellationToken)
    {
        var uri = new Uri(url);
        if (uri.Scheme != Uri.UriSchemeHttps || !_gates.ContainsKey(uri.Host))
        {
            throw new ArgumentException($"{uri.Host} is not an allowlisted https host.", nameof(url));
        }

        var gate = _gates[uri.Host];
        if (gate.Failures >= MaxFailuresInARow)
        {
            throw new FetchFailedException($"{uri.Host} is skipped for the rest of the run");
        }

        try
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                using var response = await SendAsync(gate, uri, cancellationToken);
                var status = (int)response.StatusCode;
                if (status is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    var next = new Uri(uri, location);
                    if (next.Scheme != Uri.UriSchemeHttps || !string.Equals(next.Host, uri.Host, StringComparison.OrdinalIgnoreCase))
                    {
                        _log($"{uri} redirects off the host to {next.Host}; skipped");
                        gate.Failures = 0;
                        return null;
                    }

                    uri = next;
                    continue;
                }

                if (status == 429 || status >= 500)
                {
                    throw Failed(gate, $"{uri} answered {status}");
                }

                // The block page can come with 200 as well (miniset.net); it is small, a shop page is not.
                var page = await response.Content.ReadAsStringAsync(cancellationToken);
                if (page.Length < BlockPageMaxLength && BlockMarkers.Any(m => page.Contains(m, StringComparison.OrdinalIgnoreCase)))
                {
                    gate.Failures = MaxFailuresInARow;
                    _log($"{uri} answered {status} with a block page: the host restricts this client; it rests for the rest of the run");
                    throw new FetchFailedException($"{uri.Host} restricts this client");
                }

                gate.Failures = 0;
                if (!response.IsSuccessStatusCode)
                {
                    _log($"{uri} answered {status}");
                    return null;
                }

                return page;
            }

            _log($"{uri} redirected more than {MaxRedirects} times; skipped");
            gate.Failures = 0;
            return null;
        }
        catch (HttpRequestException ex)
        {
            throw Failed(gate, $"{uri} failed: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failed(gate, $"{uri} timed out");
        }
    }

    public void Dispose() => _http.Dispose();

    private FetchFailedException Failed(HostGate gate, string message)
    {
        gate.Failures++;
        _log(message);
        if (gate.Failures == MaxFailuresInARow)
        {
            _log($"{MaxFailuresInARow} failures in a row; the host is skipped for the rest of the run");
        }

        return new FetchFailedException(message);
    }

    private async Task<HttpResponseMessage> SendAsync(HostGate gate, Uri uri, CancellationToken cancellationToken)
    {
        await gate.Lock.WaitAsync(cancellationToken);
        try
        {
            await Task.Delay(DelayBefore(gate.Last, DateTimeOffset.UtcNow, gate.Interval), cancellationToken);
            try
            {
                return await _http.GetAsync(uri, HttpCompletionOption.ResponseContentRead, cancellationToken);
            }
            finally
            {
                gate.Last = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            gate.Lock.Release();
        }
    }

    private sealed class HostGate(TimeSpan interval)
    {
        public TimeSpan Interval { get; } = interval;

        public SemaphoreSlim Lock { get; } = new(1, 1);

        public DateTimeOffset? Last { get; set; }

        public int Failures { get; set; }
    }
}
