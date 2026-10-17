namespace Armamentarium.BoxData.Scraper.Tests;

// Pages are written for these tests in the shapes the shops serve; none is copied from a shop.
[TestClass]
public sealed class SourceTests
{
    private sealed class FakeFetcher(Dictionary<string, string> pages) : IFetcher
    {
        public List<string> Requested { get; } = [];

        public Task<string?> GetAsync(string url, CancellationToken cancellationToken)
        {
            Requested.Add(url);
            return Task.FromResult(pages.TryGetValue(url, out var page) ? page : null);
        }
    }

    private static readonly Product Exo =
        new("60010199084", "5011921272372", "KILL TEAM: EXO (ENGLISH)", ["Kill Team"], "current", "miniatures");

    private const string ShopifyProduct = """
        {"title":"Kill Team - Exo","vendor":"Games Workshop","description":"<ul><li>1x Rider Bladeleader</li><li>2x Swamp Lizards</li></ul>",
         "variants":[{"barcode":"5011921272372","sku":"103-17-EN"},{"barcode":"5011921272389","sku":"103-17-FR"}]}
        """;

    [TestMethod]
    public async Task Shopify_ConfirmedBarcode_GivesListingWithVariants()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["https://shop.test/search/suggest.json?q=5011921272372&resources[type]=product"] =
                """{"resources":{"results":{"products":[{"handle":"kill-team-exo"}]}}}""",
            ["https://shop.test/products/kill-team-exo.js"] = ShopifyProduct,
        });

        var listing = await new ShopifySource("shop.test", German: false).FindAsync(Exo, fetcher, default);

        Assert.IsNotNull(listing);
        Assert.AreEqual("https://shop.test/products/kill-team-exo", listing.Url);
        Assert.AreEqual("Kill Team - Exo", listing.Title);
        CollectionAssert.AreEqual(new[] { 1, 2 }, listing.Lines.Select(l => l.Count).ToArray());
        CollectionAssert.AreEqual(new[] { "5011921272372", "5011921272389" }, listing.VariantEans.ToArray());
    }

    [TestMethod]
    public async Task Shopify_OtherBarcode_IsNoFinding()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["https://shop.test/search/suggest.json?q=5011921272372&resources[type]=product"] =
                """{"resources":{"results":{"products":[{"handle":"other"}]}}}""",
            ["https://shop.test/products/other.js"] =
                """{"title":"Other","vendor":"GW","description":"<li>1x Seer</li>","variants":[{"barcode":"5011921000000"}]}""",
        });

        Assert.IsNull(await new ShopifySource("shop.test", German: false).FindAsync(Exo, fetcher, default));
    }

    [TestMethod]
    public async Task Shopify_UsedGoods_AreSkipped()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["https://shop.test/search/suggest.json?q=5011921272372&resources[type]=product"] =
                """{"resources":{"results":{"products":[{"handle":"used-exo"}]}}}""",
            ["https://shop.test/products/used-exo.js"] =
                """{"title":"Kill Team - Exo (Used)","vendor":"Games Workshop Used","description":"<li>1x Seer</li>","variants":[{"barcode":"5011921272372"}]}""",
        });

        Assert.IsNull(await new ShopifySource("shop.test", German: false).FindAsync(Exo, fetcher, default));
    }

    [TestMethod]
    public async Task Shopify_ProductWithoutEan_IsNotSearched()
    {
        var fetcher = new FakeFetcher([]);

        Assert.IsNull(await new ShopifySource("shop.test", German: false).FindAsync(Exo with { Ean = null }, fetcher, default));
        Assert.AreEqual(0, fetcher.Requested.Count);
    }

    private const string JtlProduct = """
        <html><head><title>Exo</title><meta itemprop="description" content="2x Fake Lines"></head><body>
        <h1 class="product-title">Kill Team: Exo (EN)</h1>
        <span itemprop="gtin13">5011921272372</span>
        <div id="tab-description"><p>Inhalt:</p><ul><li>1x Rider Bladeleader Operative (Miniatur)</li><li>2x Swamp Lizards (Miniaturen)</li></ul></div>
        </body></html>
        """;

    [TestMethod]
    public async Task Jtl_SearchResultLink_LeadsToConfirmedProduct()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["https://jtl.test/?qs=5011921272372"] =
                """<div class="productbox"><a href="https://jtl.test/Kill-Team-Exo-EN">Exo</a><a href="https://jtl.test/?suche=1&amp;kf=2">f</a></div>""",
            ["https://jtl.test/Kill-Team-Exo-EN"] = JtlProduct,
        });

        var listing = await new JtlShopSource("jtl.test", German: true).FindAsync(Exo, fetcher, default);

        Assert.IsNotNull(listing);
        Assert.IsTrue(listing.German);
        Assert.AreEqual("https://jtl.test/Kill-Team-Exo-EN", listing.Url);
        CollectionAssert.AreEqual(new[] { 1, 2 }, listing.Lines.Select(l => l.Count).ToArray());
    }

    [TestMethod]
    public async Task Jtl_SearchAnsweredWithProductPage_IsUsedDirectly()
    {
        var fetcher = new FakeFetcher(new() { ["https://jtl.test/?qs=5011921272372"] = JtlProduct });

        var listing = await new JtlShopSource("jtl.test", German: true).FindAsync(Exo, fetcher, default);

        Assert.AreEqual("https://jtl.test/?qs=5011921272372", listing!.Url);
    }

    [TestMethod]
    public async Task Jtl_ProductWithOtherGtin_IsNoFinding()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["https://jtl.test/?qs=5011921272372"] = JtlProduct.Replace("5011921272372", "5011921000000"),
        });

        Assert.IsNull(await new JtlShopSource("jtl.test", German: true).FindAsync(Exo, fetcher, default));
    }

    private const string ElementProduct = """
        <div class="productOptionWrap sku"><label>SKU / Product Code:</label>
        <div class="productOption">60010199084 </div></div>
        <h1>Kill Team: Exo (English)</h1>
        <div itemprop="description"><p>Inside the box:<br>– 1x Rider Bladeleader<br>– 2x Swamp Lizards</p></div>
        """;

    [TestMethod]
    public async Task ElementGames_ConfirmedArticleNumber_GivesListing()
    {
        var fetcher = new FakeFetcher(new()
        {
            ["https://elementgames.co.uk/search?q=60010199084&ajax=1"] =
                """[{"Name":"Foam tray","FullUrl":"foam\/tray"},{"Name":"Kill Team: Exo","FullUrl":"kill-team-exo"}]""",
            ["https://elementgames.co.uk/foam/tray"] = "<div>no code here</div>",
            ["https://elementgames.co.uk/kill-team-exo"] = ElementProduct,
        });

        var listing = await new ElementGamesSource().FindAsync(Exo, fetcher, default);

        Assert.AreEqual("https://elementgames.co.uk/kill-team-exo", listing!.Url);
        CollectionAssert.AreEqual(new[] { 1, 2 }, listing.Lines.Select(l => l.Count).ToArray());
    }

    [TestMethod]
    public void Allowlist_HoldsTheMeasuredShopsOnly()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "www.wellplayed.ch", "www.irresistibleforce.com.au", "tistaminis.com", "abyssgamestore.ca", "radaddel.de",
                "www.fantasywelt.de", "taschengelddieb.de", "elementgames.co.uk",
            },
            Sources.All.Select(s => s.Host).ToArray());
    }
}
