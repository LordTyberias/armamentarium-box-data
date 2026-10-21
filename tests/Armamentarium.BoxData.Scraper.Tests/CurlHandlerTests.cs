using System.Net;
using System.Text;

namespace Armamentarium.BoxData.Scraper.Tests;

[TestClass]
public sealed class CurlHandlerTests
{
    private sealed class Inner : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("inner") });
        }
    }

    private sealed class Curl(CurlResult result)
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<CurlResult> Run(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Calls.Add(arguments);
            return Task.FromResult(result);
        }
    }

    private static CurlResult Output(string text, int exitCode = 0, string error = "") =>
        new(exitCode, Encoding.UTF8.GetBytes(text), error);

    private static HttpClient Client(Inner inner, Curl curl)
    {
        var client = new HttpClient(new CurlHandler(inner, curl.Run));
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Fetcher.UserAgent);
        return client;
    }

    [TestMethod]
    public async Task Send_ShopifyProduct_GoesToCurlWithTheRequestHeaders()
    {
        var inner = new Inner();
        var curl = new Curl(Output("HTTP/2 200 \r\ncontent-type: application/json\r\n\r\n{}"));
        using var client = Client(inner, curl);

        using var response = await client.GetAsync("https://shop.test/products/a-kit.js");

        Assert.AreEqual("{}", await response.Content.ReadAsStringAsync());
        Assert.IsEmpty(inner.Requested);
        var arguments = curl.Calls.Single();
        Assert.Contains("--http2", arguments);
        Assert.Contains("User-Agent: ABD/1.0", arguments);
        Assert.AreEqual("https://shop.test/products/a-kit.js", arguments[^1]);
        Assert.DoesNotContain("-L", arguments);
    }

    [TestMethod]
    [DataRow("https://shop.test/search/suggest.json?q=1&resources[type]=product")]
    [DataRow("https://shop.test/products/a-kit")]
    [DataRow("https://shop.test/search?q=99120105152&ajax=1")]
    public async Task Send_AnyOtherAddress_StaysWithTheInnerHandler(string url)
    {
        var inner = new Inner();
        var curl = new Curl(Output("HTTP/2 200 \r\n\r\n"));
        using var client = Client(inner, curl);

        using var response = await client.GetAsync(url);

        Assert.AreEqual("inner", await response.Content.ReadAsStringAsync());
        Assert.IsEmpty(curl.Calls);
    }

    [TestMethod]
    public async Task Send_TooManyRequests_CarriesStatusRetryAfterAndProtocol()
    {
        var curl = new Curl(Output("HTTP/2 429 \r\nretry-after: 60\r\ncontent-type: text/plain\r\n\r\nslow down"));
        using var client = Client(new Inner(), curl);

        using var response = await client.GetAsync("https://shop.test/products/a-kit.js");

        Assert.AreEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.AreEqual(TimeSpan.FromSeconds(60), response.Headers.RetryAfter?.Delta);
        Assert.AreEqual(HttpVersion.Version20, response.Version);
        Assert.AreEqual("slow down", await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task Send_RedirectOverHttp11_CarriesLocationAndProtocol()
    {
        var curl = new Curl(Output("HTTP/1.1 301 Moved Permanently\r\nLocation: /products/b-kit.js\r\n\r\n"));
        using var client = Client(new Inner(), curl);

        using var response = await client.GetAsync("https://shop.test/products/a-kit.js");

        Assert.AreEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.AreEqual("/products/b-kit.js", response.Headers.Location?.OriginalString);
        Assert.AreEqual(HttpVersion.Version11, response.Version);
    }

    [TestMethod]
    public async Task Send_CurlTimesOut_IsATimeout()
    {
        var curl = new Curl(Output("", exitCode: 28, error: "curl: (28) Operation timed out"));
        using var client = Client(new Inner(), curl);

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => client.GetAsync("https://shop.test/products/a-kit.js"));
    }

    [TestMethod]
    public async Task Send_CurlFails_IsARequestFailure()
    {
        var curl = new Curl(Output("", exitCode: 7, error: "curl: (7) Failed to connect"));
        using var client = Client(new Inner(), curl);

        var failure = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.GetAsync("https://shop.test/products/a-kit.js"));
        StringAssert.Contains(failure.Message, "Failed to connect");
    }

    [TestMethod]
    public async Task Fetcher_ThrottledThroughCurl_HonoursRetryAfter()
    {
        var curl = new Curl(Output("HTTP/2 429 \r\nretry-after: 60\r\n\r\n"));
        var log = new List<string>();
        using var fetcher = new Fetcher(
            new Dictionary<string, TimeSpan> { ["shop.test"] = TimeSpan.Zero }, log.Add, new CurlHandler(new Inner(), curl.Run));

        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/products/a-kit.js", default));
        await Assert.ThrowsExactlyAsync<FetchFailedException>(() => fetcher.GetAsync("https://shop.test/products/b-kit.js", default));

        Assert.HasCount(1, curl.Calls);
        Assert.IsTrue(log.Any(l => l.Contains("asked to wait until")));
    }
}
