using System.Net;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// The only way to a shop: allowlisted hosts, https, at most one request per host interval (five seconds, stretched for a
/// shop that throttles), redirects only within the host (ADR 0003 §5–§7). Any other answer that is not 200 is no finding
/// (null); a captcha or challenge is never worked around. A shop that does not answer (network, timeout, 403, 429, 5xx) throws <see cref="FetchFailedException"/>, so the run
/// can tell "not listed" from "not asked"; after <see cref="MaxFailuresInARow"/> such failures the host rests for the run.
/// A page that says the host has restricted this client rests the host at once.
/// A 429 is a shop that throttles (ADR 0003 Addendum ARMAM-595): its <c>Retry-After</c> is honoured, the host failing at once
/// until then (longer than <see cref="MaxRetryAfter"/> rests it for the run), and from the <see cref="StretchAfter"/>th 429
/// on the host's interval is doubled up to <see cref="MaxStretchedInterval"/> for the rest of the run. A 403 throttles nothing.
/// Every request goes over HTTP/2, over HTTP/1.1 only to a host without it, which the journal names once per host (ADR 0003
/// Addendum ARMAM-603).
/// </summary>
public sealed class Fetcher : IFetcher, IDisposable
{
    // The service's own name, no contact address (ADR 0003 §7). A browser string was measured to draw 429 from Shopify.
    public const string UserAgent = "ABD/1.0";

    public const int MaxFailuresInARow = 20;

    public const int StretchAfter = 3;

    // Every product waits for its slowest fast host; at ten seconds a Shopify product (two requests) takes 20 s and a pass
    // over the whole backlog about 19 hours; the catch-up can add about five, past the unit's 20, where the run stops and
    // the next one goes on from the saved state (ADR 0003 Addendum ARMAM-595).
    public static readonly TimeSpan MaxStretchedInterval = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(10);

    private const int MaxRedirects = 3;

    // What a host serves instead of the page once it has restricted this client (measured: miniset.net, 2026-10-17).
    private static readonly string[] BlockMarkers = ["temporarily restricted", "suspicious automated activity"];

    // Block pages measured at 1.3 kB; the smallest shop answer that carries data is a short JSON, never a whole page.
    private const int BlockPageMaxLength = 16 * 1024;

    private readonly HttpClient _http;
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
            // Shopify answers /products/<handle>.js over HTTP/1.1 with 429 from the first request, over HTTP/2 with 200 up to
            // two requests a second across ten shops (measured from the server, ADR 0003 Addendum ARMAM-603).
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
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

    /// <summary>The interval of a throttling host: doubled up to ten seconds, never shortened.</summary>
    public static TimeSpan Stretch(TimeSpan interval) =>
        interval >= MaxStretchedInterval ? interval : TimeSpan.FromTicks(Math.Min(interval.Ticks * 2, MaxStretchedInterval.Ticks));

    internal TimeSpan IntervalOf(string host) => _gates[host].Interval;

    /// <summary>One line per host that answered 429 in this run, for the journal at the end of the run.</summary>
    public IEnumerable<string> Summary() =>
        _gates.Where(g => g.Value.Throttles > 0)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}: {g.Value.Throttles} answer(s) 429, {g.Value.RetryAfterSeen} with Retry-After, " +
                $"interval {g.Value.Interval.TotalSeconds:0.###} s");

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
            throw new FetchFailedException($"{uri.Host} is skipped for the rest of the run", canRetryInRun: false);
        }

        // The shop asked for the wait; the product is asked again at the end of the run instead of holding every other.
        if (gate.NotBefore is { } notBefore && notBefore > DateTimeOffset.UtcNow)
        {
            var waiting = $"{uri.Host} asked to wait until {notBefore:u}; requests fail at once until then";
            if (!gate.WaitLogged)
            {
                gate.WaitLogged = true;
                _log(waiting);
            }

            throw new FetchFailedException(waiting);
        }

        try
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                using var response = await SendAsync(gate, uri, cancellationToken);
                if (response.Version < HttpVersion.Version20 && !gate.OldProtocolLogged)
                {
                    gate.OldProtocolLogged = true;
                    _log($"{uri.Host} answers over HTTP/{response.Version}");
                }

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

                // The block page can come with any status, 200 included (miniset.net); it is small, a shop page is not.
                var page = await response.Content.ReadAsStringAsync(cancellationToken);
                if (page.Length < BlockPageMaxLength && BlockMarkers.Any(m => page.Contains(m, StringComparison.OrdinalIgnoreCase)))
                {
                    gate.Failures = MaxFailuresInARow;
                    _log($"{uri} answered {status} with a block page: the host restricts this client; it rests for the rest of the run");
                    throw new FetchFailedException($"{uri.Host} restricts this client", canRetryInRun: false);
                }

                if (status == 429)
                {
                    throw Throttled(gate, uri, response);
                }

                // A 403 is the shop's guard refusing this client, not "not listed" (ADR 0003 Addendum ARMAM-596); asking
                // again in the same run would meet the same guard (Addendum ARMAM-595).
                if (status == 403)
                {
                    throw Failed(gate, $"{uri} answered {status}", canRetryInRun: false);
                }

                if (status >= 500)
                {
                    throw Failed(gate, $"{uri} answered {status}");
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

    private FetchFailedException Throttled(HostGate gate, Uri uri, HttpResponseMessage response)
    {
        gate.Throttles++;
        var now = DateTimeOffset.UtcNow;
        var wait = response.Headers.RetryAfter is { } retryAfter ? retryAfter.Delta ?? retryAfter.Date - now : null;
        var message = $"{uri} answered 429";
        if (wait > TimeSpan.Zero)
        {
            gate.RetryAfterSeen++;
            message += $", Retry-After {wait.Value.TotalSeconds:0} s";
            if (wait > MaxRetryAfter)
            {
                gate.Failures = MaxFailuresInARow;
                _log($"{message}: longer than {MaxRetryAfter.TotalMinutes:0} min; the host rests for the rest of the run");
                return new FetchFailedException(message, canRetryInRun: false);
            }

            gate.NotBefore = now + wait.Value;
            gate.WaitLogged = false;
        }

        if (gate.Throttles >= StretchAfter && Stretch(gate.Interval) is var stretched && stretched != gate.Interval)
        {
            gate.Interval = stretched;
            _log($"{uri.Host} answered 429 {gate.Throttles} times; its interval is stretched to {stretched.TotalSeconds:0.###} s for the rest of the run");
        }

        return Failed(gate, message);
    }

    private FetchFailedException Failed(HostGate gate, string message, bool canRetryInRun = true)
    {
        gate.Failures++;
        _log(message);
        if (gate.Failures == MaxFailuresInARow)
        {
            _log($"{MaxFailuresInARow} failures in a row; the host is skipped for the rest of the run");
        }

        return new FetchFailedException(message, canRetryInRun);
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
        public TimeSpan Interval { get; set; } = interval;

        public SemaphoreSlim Lock { get; } = new(1, 1);

        public DateTimeOffset? Last { get; set; }

        public int Failures { get; set; }

        public int Throttles { get; set; }

        public int RetryAfterSeen { get; set; }

        public DateTimeOffset? NotBefore { get; set; }

        // The first request refused inside a wait says so in the journal, the others of the same wait do not.
        public bool WaitLogged { get; set; }

        // A host that answers below HTTP/2 says so once; Shopify throttles it (ADR 0003 Addendum ARMAM-603).
        public bool OldProtocolLogged { get; set; }
    }
}
