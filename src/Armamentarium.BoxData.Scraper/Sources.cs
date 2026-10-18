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
        // Measured in ARMAM-602 (ADR 0003 addendum ARMAM-602); obsidiangames.ca and mrmuffinstrains.com keep Shopify's
        // standard ban on scraping in their terms, a risk the maintainer carries like the one above.
        new ShopifySource("shop.afk.games", German: false),
        new ShopifySource("obsidiangames.ca", German: false),
        new ShopifySource("www.goblingaming.co.uk", German: false),
        new ShopifySource("mrmuffinstrains.com", German: false),
        new ElementGamesSource(),
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

            return Listings.Read(Host, $"https://{Host}/products/{handle}", German, title, Field(root, "description"), barcodes);
        }

        return null;
    }

    private static string Field(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]*\z", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex Handle();
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

            return Listings.Read(Host, url, false, title, Page.Section(page, "<div itemprop=\"description\">"), []);
        }

        return null;
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-/]*\z", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex PathShape();

    [GeneratedRegex(@"SKU / Product Code:</label>\s*<div class=""productOption"">\s*([0-9]+)\s*<", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex Sku();
}

/// <summary>A listing read from a shop's description: its "N x Name" lines and its build sentences.</summary>
internal static class Listings
{
    public static Listing Read(
        string host, string url, bool german, string title, string description, IReadOnlyList<string> variantEans) =>
        new(host, url, german, title, ContentLines.Parse(description), variantEans)
        {
            Sentences = BuildSentences.Parse(description),
        };
}
