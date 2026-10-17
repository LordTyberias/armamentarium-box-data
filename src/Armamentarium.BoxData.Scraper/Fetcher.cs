using System.Net;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// The only way to a shop: allowlisted hosts, https, at most one request per five seconds and host, redirects only within
/// the host (ADR 0003 §5–§7). A captcha or challenge is an answer that is not 200 — logged, never worked around.
/// </summary>
public sealed class Fetcher : IFetcher, IDisposable
{
    // The service's own name, no contact address (ADR 0003 §7). A browser string was measured to draw 429 from Shopify.
    public const string UserAgent = "ABD/1.0";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private const int MaxRedirects = 3;

    private readonly HttpClient _http;
    private readonly IReadOnlySet<string> _hosts;
    private readonly Action<string> _log;
    private readonly Dictionary<string, HostGate> _gates;

    public Fetcher(IReadOnlySet<string> hosts, Action<string> log)
    {
        _hosts = hosts;
        _log = log;
        _gates = hosts.ToDictionary(h => h, _ => new HostGate(), StringComparer.OrdinalIgnoreCase);
        _http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(60),
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

        try
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                using var response = await SendAsync(uri, cancellationToken);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    var next = new Uri(uri, location);
                    if (next.Scheme != Uri.UriSchemeHttps || !string.Equals(next.Host, uri.Host, StringComparison.OrdinalIgnoreCase))
                    {
                        _log($"{uri} redirects off the host to {next.Host}; skipped");
                        return null;
                    }

                    uri = next;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _log($"{uri} answered {(int)response.StatusCode}");
                    return null;
                }

                return await response.Content.ReadAsStringAsync(cancellationToken);
            }

            _log($"{uri} redirected more than {MaxRedirects} times; skipped");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _log($"{uri} failed: {ex.Message}");
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _log($"{uri} timed out");
            return null;
        }
    }

    public void Dispose() => _http.Dispose();

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        var gate = _gates[uri.Host];
        await gate.Lock.WaitAsync(cancellationToken);
        try
        {
            await Task.Delay(DelayBefore(gate.Last, DateTimeOffset.UtcNow, Interval), cancellationToken);
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
    }
}
