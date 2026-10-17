using System.Net;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// The only way to a shop: allowlisted hosts, https, at most one request per five seconds and host, redirects only within
/// the host (ADR 0003 §5–§7). An answer that is not 200 is no finding (null) — a captcha or challenge is logged, never worked
/// around. A shop that does not answer (network, timeout, 429, 5xx) throws <see cref="FetchFailedException"/>, so the run
/// can tell "not listed" from "not asked"; after <see cref="MaxFailuresInARow"/> such failures the host rests for the run.
/// </summary>
public sealed class Fetcher : IFetcher, IDisposable
{
    // The service's own name, no contact address (ADR 0003 §7). A browser string was measured to draw 429 from Shopify.
    public const string UserAgent = "ABD/1.0";

    public const int MaxFailuresInARow = 20;

    private const int MaxRedirects = 3;

    private readonly HttpClient _http;
    private readonly IReadOnlySet<string> _hosts;
    private readonly Action<string> _log;
    private readonly TimeSpan _interval;
    private readonly Dictionary<string, HostGate> _gates;

    public Fetcher(IReadOnlySet<string> hosts, Action<string> log)
        : this(hosts, log, new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        }, TimeSpan.FromSeconds(5))
    {
    }

    internal Fetcher(IReadOnlySet<string> hosts, Action<string> log, HttpMessageHandler handler, TimeSpan interval)
    {
        _hosts = hosts;
        _log = log;
        _interval = interval;
        _gates = hosts.ToDictionary(h => h, _ => new HostGate(), StringComparer.OrdinalIgnoreCase);
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
        if (uri.Scheme != Uri.UriSchemeHttps || !_hosts.Contains(uri.Host))
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

                gate.Failures = 0;
                if (!response.IsSuccessStatusCode)
                {
                    _log($"{uri} answered {status}");
                    return null;
                }

                return await response.Content.ReadAsStringAsync(cancellationToken);
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
            await Task.Delay(DelayBefore(gate.Last, DateTimeOffset.UtcNow, _interval), cancellationToken);
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

    private sealed class HostGate
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);

        public DateTimeOffset? Last { get; set; }

        public int Failures { get; set; }
    }
}
