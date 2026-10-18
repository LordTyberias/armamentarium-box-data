using System.Net;
using System.Net.Http.Headers;

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

    private static HttpResponseMessage Throttled(RetryConditionHeaderValue? retryAfter = null)
    {
        var response = Status(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = retryAfter;
        return response;
    }

    [TestMethod]
    public async Task Get_TooManyRequestsWithRetryAfter_LeavesTheHostAloneUntilThen()
    {
        var log = new List<string>();
        var handler = new Answers(_ => Throttled(new RetryConditionHeaderValue(TimeSpan.FromSeconds(120))));
        using var fetcher = Make(handler, log);

        var first = await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/a", default));
        var second = await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/b", default));
        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/c", default));

        Assert.AreEqual(1, handler.Requested.Count);
        Assert.IsTrue(first.CanRetryInRun);
        Assert.IsTrue(second.CanRetryInRun);
        Assert.AreEqual(1, log.Count(l => l.Contains("asked to wait until", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Get_RetryAfterOnTheHourOrLater_RestsTheHostForTheRun()
    {
        var log = new List<string>();
        var handler = new Answers(_ => Throttled(new RetryConditionHeaderValue(TimeSpan.FromHours(1))));
        using var fetcher = Make(handler, log);

        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/a", default));
        var rested = await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/b", default));

        Assert.AreEqual(1, handler.Requested.Count);
        Assert.IsFalse(rested.CanRetryInRun);
        Assert.IsTrue(log.Any(l => l.Contains("rest of the run", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Get_RetryAfterInThePastOrMissing_AsksAgain()
    {
        var answers = new Queue<HttpResponseMessage>(
        [
            Throttled(new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-1))),
            Throttled(),
            Status(HttpStatusCode.OK),
        ]);
        var handler = new Answers(_ => answers.Dequeue());
        using var fetcher = Make(handler);

        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/a", default));
        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/b", default));

        Assert.AreEqual("body", await fetcher.GetAsync("https://shop.test/c", default));
        Assert.AreEqual(3, handler.Requested.Count);
    }

    [TestMethod]
    public async Task Get_RetryAfterAsADate_LeavesTheHostAloneUntilThen()
    {
        var handler = new Answers(_ => Throttled(new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(2))));
        using var fetcher = Make(handler);

        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/a", default));
        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/b", default));

        Assert.AreEqual(1, handler.Requested.Count);
    }

    [TestMethod]
    public async Task Get_ThirdTooManyRequests_StretchesThatHostOnly()
    {
        var log = new List<string>();
        using var fetcher = new Fetcher(
            new Dictionary<string, TimeSpan> { ["shop.test"] = TimeSpan.FromMilliseconds(1), ["other.test"] = TimeSpan.FromMilliseconds(1) },
            log.Add,
            new Answers(r => r.RequestUri!.Host == "shop.test" ? Throttled() : Status(HttpStatusCode.OK)));

        for (var i = 0; i < Fetcher.StretchAfter; i++)
        {
            await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/x", default));
        }

        await fetcher.GetAsync("https://other.test/x", default);

        Assert.AreEqual(TimeSpan.FromMilliseconds(2), fetcher.IntervalOf("shop.test"));
        Assert.AreEqual(TimeSpan.FromMilliseconds(1), fetcher.IntervalOf("other.test"));
        Assert.AreEqual(1, log.Count(l => l.Contains("stretched", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Stretch_DoublesUpToTenSecondsAndNeverShortens()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(10), Fetcher.Stretch(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(TimeSpan.FromSeconds(10), Fetcher.Stretch(TimeSpan.FromSeconds(8)));
        Assert.AreEqual(TimeSpan.FromSeconds(60), Fetcher.Stretch(TimeSpan.FromSeconds(60)));
    }

    [TestMethod]
    public async Task Get_Refused_IsNotAskedAgainInTheRunNorStretched()
    {
        using var fetcher = Make(new Answers(_ => Status(HttpStatusCode.Forbidden)));

        for (var i = 0; i < Fetcher.StretchAfter; i++)
        {
            var refused = await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/x", default));
            Assert.IsFalse(refused.CanRetryInRun);
        }

        Assert.AreEqual(0, fetcher.Summary().Count());
    }

    [TestMethod]
    public async Task Summary_NamesEachThrottledHostWithItsCountsAndInterval()
    {
        var answers = new Queue<HttpResponseMessage>([Throttled(), Throttled(new RetryConditionHeaderValue(TimeSpan.FromSeconds(60)))]);
        using var fetcher = Make(new Answers(_ => answers.Dequeue()));

        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/a", default));
        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/b", default));

        var line = fetcher.Summary().Single();
        StringAssert.Contains(line, "shop.test");
        StringAssert.Contains(line, "2 answer(s) 429");
        StringAssert.Contains(line, "1 with Retry-After");
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
