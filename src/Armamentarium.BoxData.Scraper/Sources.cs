using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Armamentarium.BoxData.Scraper;

/// <summary>The compiled allowlist (ADR 0003 §5). A shop not listed here is never asked.</summary>
public static class Sources
{
    public static IReadOnlyList<ISource> All { get; } =
    [
        new ShopifySource("www.wellplayed.ch", German: false),
        new ShopifySource("www.irresistibleforce.com.au", German: false),
        new ShopifySource("tistaminis.com", German: false),
        new ShopifySource("abyssgamestore.ca", German: false),
        new ShopifySource("radaddel.de", German: true),
        // Its terms forbid scraping; the maintainer carries that risk (Armamentarium ADR 0118 Addendum ARMAM-591).
        new ShopifySource("www.retrofixgames.com", German: false),
        new JtlShopSource("www.fantasywelt.de", German: true),
        new JtlShopSource("taschengelddieb.de", German: true),
        new ElementGamesSource(),
        new MinisetSource(),
    ];
}

internal static partial class Page
{
    // Search results tried per product and shop; the right product is among the first in every shop measured.
    public const int Tries = 3;

    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    // Every pattern that runs over a shop page has a time limit; a page built to stall a pattern costs one source, not the run.
    public const int RegexTimeout = 2_000;

    public static string Title(string html)
    {
        var match = Heading().Match(html);
        return match.Success ? Text.Clean(WebUtility.HtmlDecode(Tag().Replace(match.Groups[1].Value, string.Empty))) : string.Empty;
    }

    /// <summary>The description from its marker on, at most 30,000 characters; empty without the marker.</summary>
    public static string Section(string html, string marker)
    {
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        return start < 0 ? string.Empty : html.Substring(start, Math.Min(30_000, html.Length - start));
    }

    public static bool IsUsed(string text) => Used().IsMatch(text);

    [GeneratedRegex("<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline | RegexOptions.IgnoreCase, RegexTimeout)]
    private static partial Regex Heading();

    [GeneratedRegex("<[^>]*>", RegexOptions.None, RegexTimeout)]
    private static partial Regex Tag();

    [GeneratedRegex(@"\bused\b|gebraucht", RegexOptions.IgnoreCase, RegexTimeout)]
    private static partial Regex Used();
}

/// <summary>Shopify: product search by EAN, confirmed by a variant's barcode in <c>/products/&lt;handle&gt;.js</c>.</summary>
public sealed partial record ShopifySource(string Host, bool German) : ISource
{
    public TimeSpan Interval => Page.DefaultInterval;

    public async Task<Listing?> FindAsync(Product product, IFetcher fetcher, CancellationToken cancellationToken)
    {
        if (product.Ean is not { } ean)
        {
            return null;
        }

        var suggest = await fetcher.GetAsync(
            $"https://{Host}/search/suggest.json?q={ean}&resources[type]=product", cancellationToken);
        if (suggest is null)
        {
            return null;
        }

        List<string> handles;
        using (var document = JsonDocument.Parse(suggest))
        {
            handles = document.RootElement.GetProperty("resources").GetProperty("results").GetProperty("products")
                .EnumerateArray()
                .Select(p => p.TryGetProperty("handle", out var h) ? h.GetString() : null)
                .OfType<string>()
                .Where(h => Handle().IsMatch(h))
                .Take(Page.Tries)
                .ToList();
        }

        foreach (var handle in handles)
        {
            var json = await fetcher.GetAsync($"https://{Host}/products/{handle}.js", cancellationToken);
            if (json is null)
            {
                continue;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var barcodes = root.GetProperty("variants").EnumerateArray()
                .Select(v => v.TryGetProperty("barcode", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null)
                .OfType<string>()
                .Where(b => b.Length > 0)
                .Distinct()
                .ToList();
            var title = Text.Clean(Field(root, "title"));
            if (!barcodes.Contains(ean) || Page.IsUsed(title) || Page.IsUsed(Field(root, "vendor")))
            {
                continue;
            }

            return new Listing(
                Host, $"https://{Host}/products/{handle}", German, title, ContentLines.Parse(Field(root, "description")), barcodes);
        }

        return null;
    }

    private static string Field(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]*\z", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex Handle();
}

/// <summary>JTL shop: search by EAN, confirmed by <c>itemprop="gtin13"</c>; the description sits in the description tab.</summary>
public sealed partial record JtlShopSource(string Host, bool German) : ISource
{
    public TimeSpan Interval => Page.DefaultInterval;

    public async Task<Listing?> FindAsync(Product product, IFetcher fetcher, CancellationToken cancellationToken)
    {
        if (product.Ean is not { } ean)
        {
            return null;
        }

        var searchUrl = $"https://{Host}/?qs={ean}";
        var search = await fetcher.GetAsync(searchUrl, cancellationToken);
        if (search is null)
        {
            return null;
        }

        // A single hit can come back as the product page itself.
        if (Confirms(search, ean))
        {
            return ListingOf(searchUrl, search);
        }

        var link = ProductLink(Host);
        var links = ProductBox().Matches(search)
            .Select(m => link.Match(search, m.Index, Math.Min(4_000, search.Length - m.Index)))
            .Where(m => m.Success)
            .Select(m => $"https://{Host}/{m.Groups[1].Value}")
            .Distinct()
            .Take(Page.Tries)
            .ToList();

        foreach (var url in links)
        {
            var page = await fetcher.GetAsync(url, cancellationToken);
            if (page is not null && Confirms(page, ean) && ListingOf(url, page) is { } listing)
            {
                return listing;
            }
        }

        return null;
    }

    private Listing? ListingOf(string url, string page)
    {
        var title = Page.Title(page);
        return Page.IsUsed(title)
            ? null
            : new Listing(Host, url, German, title, ContentLines.Parse(Page.Section(page, "id=\"tab-description\"")), []);
    }

    private static bool Confirms(string page, string ean) =>
        Gtin().Matches(page).Any(m => m.Groups[1].Value == ean);

    private static Regex ProductLink(string host) =>
        new($@"(?:href|action)=""https://{Regex.Escape(host)}/([A-Za-z0-9][A-Za-z0-9_.\-/]*)""",
            RegexOptions.None, TimeSpan.FromMilliseconds(Page.RegexTimeout));

    [GeneratedRegex(@"class=""[^""]*\bproductbox\b", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex ProductBox();

    [GeneratedRegex(@"itemprop=""gtin13""[^>]*>\s*([0-9]{8,14})\s*<", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex Gtin();
}

/// <summary>Element Games: search by article number, confirmed by the page's "SKU / Product Code". No EAN on the page.</summary>
public sealed partial class ElementGamesSource : ISource
{
    public string Host => "elementgames.co.uk";

    public TimeSpan Interval => Page.DefaultInterval;

    public async Task<Listing?> FindAsync(Product product, IFetcher fetcher, CancellationToken cancellationToken)
    {
        var search = await fetcher.GetAsync($"https://{Host}/search?q={product.Code}&ajax=1", cancellationToken);
        if (search is null)
        {
            return null;
        }

        List<string> paths;
        using (var document = JsonDocument.Parse(search))
        {
            paths = document.RootElement.EnumerateArray()
                .Select(r => r.TryGetProperty("FullUrl", out var u) ? u.GetString() : null)
                .OfType<string>()
                .Where(p => PathShape().IsMatch(p))
                .Take(Page.Tries)
                .ToList();
        }

        foreach (var path in paths)
        {
            var url = $"https://{Host}/{path}";
            var page = await fetcher.GetAsync(url, cancellationToken);
            if (page is null || !Sku().Matches(page).Any(m => m.Groups[1].Value == product.Code))
            {
                continue;
            }

            var title = Page.Title(page);
            if (Page.IsUsed(title))
            {
                continue;
            }

            return new Listing(Host, url, false, title, ContentLines.Parse(Page.Section(page, "<div itemprop=\"description\">")), []);
        }

        return null;
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-/]*\z", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex PathShape();

    [GeneratedRegex(@"SKU / Product Code:</label>\s*<div class=""productOption"">\s*([0-9]+)\s*<", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex Sku();
}

/// <summary>
/// miniset.net: one page per article number (<c>/sets/gw-&lt;code&gt;</c>), confirmed by its canonical address. The host
/// restricts clients after about a dozen requests at five seconds, so it is asked once a minute.
/// </summary>
public sealed class MinisetSource : ISource
{
    public string Host => "miniset.net";

    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    public async Task<Listing?> FindAsync(Product product, IFetcher fetcher, CancellationToken cancellationToken)
    {
        var url = $"https://{Host}/sets/gw-{product.Code}";
        var page = await fetcher.GetAsync(url, cancellationToken);
        if (page is null || !page.Contains($"<link rel=\"canonical\" href=\"{url}\"", StringComparison.Ordinal))
        {
            return null;
        }

        return new Listing(Host, url, false, Page.Title(page), ContentLines.Parse(Page.Section(page, "data-lang=\"en\"")), []);
    }
}
