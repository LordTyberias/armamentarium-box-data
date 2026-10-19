using System.Text.Json.Nodes;

namespace Armamentarium.BoxData.Scraper.Tests;

// The table below is written for these tests in the shapes the service wrote before ARMAM-598; none is copied from the table.
[TestClass]
public sealed class NameRepairTests
{
    private static readonly HashSet<string> Hosts = new(["shop.test"], StringComparer.OrdinalIgnoreCase);

    private static JsonNode Table(string host, params string[] names) =>
        Scraper.Table.Read($$"""
            {"format": 1, "boxes": [{
              "name": "Box",
              "productCodes": ["60010199001"],
              "sources": ["https://{{host}}/a", "https://shop.test/b"],
              "readings": [{"reading": "kt", "entries": [{{string.Join(",", names.Select(n => $$"""{"role": "team", "name": "{{n}}", "count": 2}"""))}}]}]
            }]}
            """);

    private static string[] Names(JsonNode table) =>
        table["boxes"]!.AsArray().SelectMany(b => b!["readings"]!.AsArray()).SelectMany(r => r!["entries"]!.AsArray())
            .Select(e => (string)e!["name"]!).ToArray();

    [TestMethod]
    public void Apply_ClauseWordsReadBeforeARMAM598_AreCut()
    {
        var table = Table("shop.test", "Rust Wasps accompanied by", "accompanying Rust Drones", "Rust Spells usable by Rust wizards", "Iron Strider");

        var repairs = NameRepair.Apply(table, Hosts);

        CollectionAssert.AreEqual(new[] { "Rust Wasps", "Rust Drones", "Rust Spells", "Iron Strider" }, Names(table));
        Assert.AreEqual(3, repairs.Renamed.Count);
        Assert.AreEqual(new NameRepairing("Box", "kt", "Rust Wasps accompanied by", "Rust Wasps"), repairs.Renamed[0]);
    }

    [TestMethod]
    public void Apply_ExcludedName_IsRemoved()
    {
        var table = Table("shop.test", "double-sided gaming boards", "Iron Strider");

        var repairs = NameRepair.Apply(table, Hosts);

        CollectionAssert.AreEqual(new[] { "Iron Strider" }, Names(table));
        Assert.AreEqual(new NameRepairing("Box", "kt", "double-sided gaming boards", null), repairs.Removed.Single());
    }

    [TestMethod]
    public void Apply_BoxLeftWithoutAnEntry_IsRemovedWhole()
    {
        var table = Table("shop.test", "double-sided gaming boards");

        var repairs = NameRepair.Apply(table, Hosts);

        Assert.AreEqual(0, table["boxes"]!.AsArray().Count);
        CollectionAssert.AreEqual(new[] { "Box" }, repairs.BoxesRemoved.ToArray());
    }

    [TestMethod]
    public void Apply_HandBox_IsLeftAlone()
    {
        var table = Table("review.test", "double-sided gaming boards", "Rust Wasps accompanied by");

        var repairs = NameRepair.Apply(table, Hosts);

        Assert.AreEqual(0, repairs.Removed.Count + repairs.Renamed.Count);
        Assert.AreEqual(2, Names(table).Length);
    }

    [TestMethod]
    public void Apply_NamesWithOtherStopWords_AreKept()
    {
        var table = Table("shop.test", "Lord on Rust Steed", "XV26 Rust Suits");

        Assert.AreEqual(0, NameRepair.Apply(table, Hosts).Renamed.Count);
    }
}
