using System.Net;

namespace Armamentarium.BoxData.Scraper.Tests;

[TestClass]
public sealed class FetcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 3, 0, 0, TimeSpan.Zero);

    private sealed class Answers(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!.ToString());
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Status(HttpStatusCode status, string? location = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent("body") };
        if (location is not null)
        {
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        }

        return response;
    }

    private static Fetcher Make(Answers handler, List<string>? log = null) =>
        new(new Dictionary<string, TimeSpan> { ["shop.test"] = TimeSpan.Zero }, (log ?? []).Add, handler);

    [TestMethod]
    public async Task Get_HostOutsideTheAllowlist_IsRefused()
    {
        using var fetcher = Make(new Answers(_ => Status(HttpStatusCode.OK)));

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fetcher.GetAsync("https://other.test/x", default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fetcher.GetAsync("http://shop.test/x", default));
    }

    [TestMethod]
    public async Task Get_Ok_ReturnsTheBody()
    {
        using var fetcher = Make(new Answers(_ => Status(HttpStatusCode.OK)));

        Assert.AreEqual("body", await fetcher.GetAsync("https://shop.test/x", default));
    }

    [TestMethod]
    public async Task Get_RedirectWithinTheHost_IsFollowed()
    {
        var handler = new Answers(r => r.RequestUri!.AbsolutePath == "/old"
            ? Status(HttpStatusCode.MovedPermanently, "/new")
            : Status(HttpStatusCode.OK));
        using var fetcher = Make(handler);

        Assert.AreEqual("body", await fetcher.GetAsync("https://shop.test/old", default));
        CollectionAssert.AreEqual(new[] { "https://shop.test/old", "https://shop.test/new" }, handler.Requested);
    }

    [TestMethod]
    public async Task Get_RedirectOffTheHost_IsNoFinding()
    {
        using var fetcher = Make(new Answers(_ => Status(HttpStatusCode.Found, "https://evil.test/x")));

        Assert.IsNull(await fetcher.GetAsync("https://shop.test/x", default));
    }

    [TestMethod]
    public async Task Get_EndlessRedirects_AreNoFinding()
    {
        using var fetcher = Make(new Answers(_ => Status(HttpStatusCode.Found, "/again")));

        Assert.IsNull(await fetcher.GetAsync("https://shop.test/x", default));
    }

    [TestMethod]
    public async Task Get_NotFound_IsNoFinding()
    {
        using var fetcher = Make(new Answers(_ => Status(HttpStatusCode.NotFound)));

        Assert.IsNull(await fetcher.GetAsync("https://shop.test/a", default));
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Forbidden)]
    [DataRow(HttpStatusCode.TooManyRequests)]
    [DataRow(HttpStatusCode.BadGateway)]
    public async Task Get_RefusedTooManyRequestsOrServerError_Fails(HttpStatusCode status)
    {
        using var fetcher = Make(new Answers(_ => Status(status)));

        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/a", default));
    }

    [TestMethod]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    [DataRow(HttpStatusCode.Forbidden)]
    public async Task Get_HostThatKeepsFailing_IsSkippedForTheRestOfTheRun(HttpStatusCode status)
    {
        var handler = new Answers(_ => Status(status));
        var log = new List<string>();
        using var fetcher = Make(handler, log);

        for (var i = 0; i < Fetcher.MaxFailuresInARow + 3; i++)
        {
            await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/x", default));
        }

        Assert.AreEqual(Fetcher.MaxFailuresInARow, handler.Requested.Count);
        Assert.AreEqual(1, log.Count(l => l.Contains("skipped for the rest of the run", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Get_BlockPage_RestsTheHostAtOnce()
    {
        var handler = new Answers(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("Access to this page has been temporarily restricted due to suspicious automated activity."),
        });
        using var fetcher = Make(handler);

        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/a", default));
        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/b", default));

        Assert.AreEqual(1, handler.Requested.Count);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.TooManyRequests)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    public async Task Get_BlockPageWithFailureStatus_RestsTheHostAtOnce(HttpStatusCode status)
    {
        var handler = new Answers(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("Access to this page has been temporarily restricted."),
        });
        using var fetcher = Make(handler);

        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/a", default));
        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/b", default));

        Assert.AreEqual(1, handler.Requested.Count);
    }

    [TestMethod]
    public async Task Get_BlockPageServedWithOk_RestsTheHostToo()
    {
        var handler = new Answers(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<title>Access Temporarily Restricted</title> Your access has been temporarily restricted due to suspicious activity."),
        });
        using var fetcher = Make(handler);

        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/a", default));
        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/b", default));

        Assert.AreEqual(1, handler.Requested.Count);
    }

    [TestMethod]
    public async Task Get_LargePageMentioningARestriction_IsAPage()
    {
        var handler = new Answers(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 20_000) + " temporarily restricted "),
        });
        using var fetcher = Make(handler);

        Assert.IsNotNull(await fetcher.GetAsync("https://shop.test/a", default));
        Assert.IsNotNull(await fetcher.GetAsync("https://shop.test/b", default));
        Assert.AreEqual(2, handler.Requested.Count);
    }

    [TestMethod]
    public async Task Get_EachHostKeepsItsOwnInterval()
    {
        using var fetcher = new Fetcher(
            new Dictionary<string, TimeSpan> { ["slow.test"] = TimeSpan.FromHours(1), ["fast.test"] = TimeSpan.Zero },
            _ => { },
            new Answers(_ => Status(HttpStatusCode.OK)));

        await fetcher.GetAsync("https://slow.test/a", default);
        await fetcher.GetAsync("https://fast.test/a", default);
        await fetcher.GetAsync("https://fast.test/b", default);

        using var wait = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAsync<OperationCanceledException>(() => fetcher.GetAsync("https://slow.test/b", wait.Token));
    }

    [TestMethod]
    public async Task Get_Timeout_FailsButStopIsAStop()
    {
        using var fetcher = Make(new Answers(_ => throw new TaskCanceledException("timeout")));

        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/x", default));
        await Assert.ThrowsAsync<OperationCanceledException>(() => fetcher.GetAsync("https://shop.test/x", new CancellationToken(true)));
    }

    [TestMethod]
    public void DelayBefore_WaitsForTheRestOfTheInterval()
    {
        var interval = TimeSpan.FromSeconds(5);

        Assert.AreEqual(TimeSpan.FromSeconds(3), Fetcher.DelayBefore(Now, Now.AddSeconds(2), interval));
        Assert.AreEqual(TimeSpan.Zero, Fetcher.DelayBefore(Now, Now.AddSeconds(7), interval));
        Assert.AreEqual(TimeSpan.Zero, Fetcher.DelayBefore(null, Now, interval));
    }
}
