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
        new(new HashSet<string> { "shop.test" }, (log ?? []).Add, handler, TimeSpan.Zero);

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
    public async Task Get_NotFoundOrRefused_IsNoFinding()
    {
        using var fetcher = Make(new Answers(r => Status(r.RequestUri!.AbsolutePath == "/a" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden)));

        Assert.IsNull(await fetcher.GetAsync("https://shop.test/a", default));
        Assert.IsNull(await fetcher.GetAsync("https://shop.test/b", default));
    }

    [TestMethod]
    public async Task Get_TooManyRequestsOrServerError_Fails()
    {
        using var fetcher = Make(new Answers(r => Status(r.RequestUri!.AbsolutePath == "/a" ? HttpStatusCode.TooManyRequests : HttpStatusCode.BadGateway)));

        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/a", default));
        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/b", default));
    }

    [TestMethod]
    public async Task Get_HostThatKeepsFailing_IsSkippedForTheRestOfTheRun()
    {
        var handler = new Answers(_ => Status(HttpStatusCode.ServiceUnavailable));
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
