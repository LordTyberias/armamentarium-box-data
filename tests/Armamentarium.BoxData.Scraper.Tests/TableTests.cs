using System.Text.Json.Nodes;

namespace Armamentarium.BoxData.Scraper.Tests;

[TestClass]
public sealed class TableTests
{
    private static string CommittedTable()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "box-contents.json");
            if (File.Exists(path))
            {
                // The working copy is CRLF; the committed object, which the service reads, is LF.
                return File.ReadAllText(path).Replace("\r\n", "\n");
            }
        }

        throw new AssertFailedException("box-contents.json not found above the test output.");
    }

    private const string Small = """
        {
          "$schema": "./box-contents.schema.json",
          "format": 1,
          "boxes": [
            {
              "name": "Kill Team: A",
              "productCodes": [
                "60010199001"
              ],
              "sources": [
                "https://a.test/p",
                "https://b.test/p"
              ],
              "readings": [
                {
                  "reading": "kt",
                  "entries": [
                    {
                      "role": "team",
                      "name": "Seer",
                      "count": 1
                    }
                  ]
                }
              ]
            }
          ]
        }

        """;

    [TestMethod]
    public void Write_CommittedTable_IsByteIdentical()
    {
        var text = CommittedTable();

        Assert.AreEqual(text, Table.Write(Table.Read(text)));
    }

    [TestMethod]
    public void Codes_ListsEveryArticleNumber()
    {
        CollectionAssert.AreEquivalent(new[] { "60010199001" }, Table.Codes(Table.Read(Small)).ToArray());
    }

    [TestMethod]
    public void AddBox_AppendsDraftWithCountsAndCandidates()
    {
        var entries = new[]
        {
            new ProposedEntry("Beast Lads", true, [10], ["a.test", "b.test"], false),
            new ProposedEntry("Hog Riders", false, [4, 5], ["a.test", "b.test"], false),
        };

        var table = Table.Read(Small);
        Table.AddBox(table, Table.Box("Kill Team: B", ["15010199002", "60010199002"], ["https://a.test/q", "https://b.test/q"], ["kt"], entries));
        var box = (JsonObject)table["boxes"]!.AsArray()[1]!;

        Assert.AreEqual("Kill Team: B", (string?)box["name"]);
        var written = box["readings"]![0]!["entries"]!.AsArray();
        Assert.AreEqual("npo", (string?)written[0]!["role"]);
        Assert.AreEqual(10, (int?)written[0]!["count"]);
        Assert.AreEqual("team", (string?)written[1]!["role"]);
        CollectionAssert.AreEqual(new[] { 4, 5 }, written[1]!["countCandidates"]!.AsArray().Select(n => (int)n!).ToArray());
        Assert.IsNull(written[1]!["count"]);
    }

    [TestMethod]
    public void Box_NonPlayerOutsideKillTeam_IsExtra()
    {
        var box = Table.Box(
            "Kill Team: B", ["60010199002"], ["https://a.test/q", "https://b.test/q"], ["kt", "40k"],
            [new ProposedEntry("Beast Lads", true, [10], ["a.test", "b.test"], false)]);

        Assert.AreEqual("npo", (string?)box["readings"]![0]!["entries"]![0]!["role"]);
        Assert.AreEqual("extra", (string?)box["readings"]![1]!["entries"]![0]!["role"]);
    }

    [TestMethod]
    public void CombatPatrolBox_CarriesGenerationInsteadOfReadings()
    {
        var box = Table.CombatPatrolBox("Combat Patrol: X", ["99120101001"], ["https://a.test/x", "https://b.test/x"], "legacy");

        Assert.AreEqual("legacy", (string?)box["generation"]);
        Assert.IsNull(box["readings"]);
    }

    [TestMethod]
    public void AddCodes_ExtendsTheBoxHoldingTheKnownCode()
    {
        var table = Table.Read(Small);

        var name = Table.AddCodes(table, "60010199001", ["04010199001", "60010199001"]);

        Assert.AreEqual("Kill Team: A", name);
        CollectionAssert.AreEqual(
            new[] { "60010199001", "04010199001" },
            table["boxes"]![0]!["productCodes"]!.AsArray().Select(n => (string)n!).ToArray());
    }

    [TestMethod]
    public void Validate_RejectsSingleSourceAndBadCodes()
    {
        Assert.IsNull(Table.Validate(Table.CombatPatrolBox("X", ["99120101001"], ["https://a.test/x", "https://b.test/x"], "current")));
        Assert.IsNotNull(Table.Validate(Table.CombatPatrolBox("X", ["99120101001"], ["https://a.test/x"], "current")));
        Assert.IsNotNull(Table.Validate(Table.CombatPatrolBox("X", ["WGB-1"], ["https://a.test/x", "https://b.test/x"], "current")));
        Assert.IsNotNull(Table.Validate(Table.CombatPatrolBox("X", ["99120101001"], ["http://a.test/x", "https://b.test/x"], "current")));
    }
}
