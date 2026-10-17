using System.Text.Json;
using System.Text.RegularExpressions;

namespace Armamentarium.BoxData.Scraper;

/// <summary>The products the service looks for, read from warhub-catalog's <c>products.json</c> (ADR 0003 §4).</summary>
public static partial class ProductCatalogue
{
    public const string ProductsUrl = "https://warhub.github.io/warhub-catalog/products.json";

    // Game system of the catalogue -> reading of the table (ADR 0002). Order is the order of the readings in a box.
    private static readonly (string System, string Reading)[] Readings =
    [
        ("Kill Team", "kt"),
        ("Warhammer 40,000", "40k"),
        ("Age of Sigmar", "aos"),
        ("The Old World", "tow"),
        ("Horus Heresy", "hh"),
    ];

    /// <summary>Every Games Workshop product with an article number.</summary>
    public static IReadOnlyList<Product> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var products = new List<Product>();
        foreach (var item in document.RootElement.GetProperty("products").EnumerateArray())
        {
            if (Text(item, "manufacturer") != "games-workshop" || Text(item, "productCode") is not { } code)
            {
                continue;
            }

            var systems = item.TryGetProperty("gameSystems", out var s) && s.ValueKind == JsonValueKind.Array
                ? s.EnumerateArray().Select(x => x.GetString()).OfType<string>().ToList()
                : [];
            products.Add(new Product(
                code, Text(item, "ean"), Text(item, "name") ?? code, systems, Text(item, "status") ?? "", Text(item, "category") ?? ""));
        }

        return products;
    }

    /// <summary>Miniatures of the app's systems that no box holds yet: Kill Team first, then current, then code.</summary>
    public static IReadOnlyList<Product> Candidates(IReadOnlyList<Product> products, IReadOnlySet<string> known) =>
        products
            .Where(p => p.Category == "miniatures" && ArticleNumber().IsMatch(p.Code) && !known.Contains(p.Code))
            .Where(p => ReadingsOf(p).Count > 0)
            .OrderBy(p => p.Systems.Contains("Kill Team") ? 0 : 1)
            .ThenBy(p => p.Status == "current" ? 0 : 1)
            .ThenBy(p => p.Code, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<string> ReadingsOf(Product product) =>
        Readings.Where(r => product.Systems.Contains(r.System)).Select(r => r.Reading).ToList();

    public static bool IsCombatPatrol(Product product) =>
        product.Name.Contains("combat patrol", StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // The table's key form (box-contents.schema.json): six to eleven digits.
    [GeneratedRegex("^[0-9]{6,11}$")]
    internal static partial Regex ArticleNumber();
}
