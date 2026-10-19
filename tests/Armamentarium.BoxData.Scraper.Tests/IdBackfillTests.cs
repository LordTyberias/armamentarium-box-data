using System.Text;

namespace Armamentarium.BoxData.Scraper.Tests;

// The catalogue and the table below are written for these tests; none is copied from BSData or the committed table.
[TestClass]
public sealed class IdBackfillTests
{
    private const string Catalogue = """
        <catalogue id="cat-iron" name="Iron Brothers" library="false">
          <selectionEntries>
            <selectionEntry type="model" id="se-sergeant" name="Sergeant"/>
            <selectionEntry type="model" id="se-gunner" name="Gunner"/>
          </selectionEntries>
        </catalogue>
        """;

    private static string Box(string name, string host, string entries) => $$"""
            {
              "name": "{{name}}",
              "productCodes": [
                "60010199001"
              ],
              "sources": [
                "https://{{host}}/a",
                "https://shop.test/b"
              ],
              "readings": [
                {
                  "reading": "kt",
                  "entries": [
        {{entries}}
                  ]
                }
              ]
            }
        """;

    private const string Gap = """
                    {
                      "role": "team",
                      "name": "Sergeants",
                      "count": 1
                    }
        """;

    private static readonly HashSet<string> Hosts = new(["shop.test", "other.test"], StringComparer.OrdinalIgnoreCase);

    private static CatalogueIndex Iron()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("kt", "Iron.cat", Encoding.UTF8.GetBytes(Catalogue));
        return index;
    }

    private static System.Text.Json.Nodes.JsonNode Table(params string[] boxes) =>
        Scraper.Table.Read($$"""{"format": 1, "boxes": [{{string.Join(",", boxes)}}]}""");

    [TestMethod]
    public void Fill_GapOfAServiceBox_GetsTheIdsBeforeItsName()
    {
        var table = Table(Box("Kill Team: Iron", "other.test", Gap));

        var filled = IdBackfill.Fill(table, Iron(), Hosts);

        Assert.AreEqual(new Backfilled("Kill Team: Iron", "kt", "Sergeants", new RootEntry("cat-iron", "se-sergeant")), filled.Single());
        var entry = table["boxes"]![0]!["readings"]![0]!["entries"]![0]!.AsObject();
        CollectionAssert.AreEqual(
            new[] { "role", "catalogueId", "entryId", "name", "count" }, entry.Select(p => p.Key).ToArray());
    }

    [TestMethod]
    public void Fill_HitTheReadingAlreadyLists_StaysAGap()
    {
        const string Listed = """
                            {
                              "role": "team",
                              "catalogueId": "cat-iron",
                              "entryId": "se-sergeant",
                              "name": "Sergeant",
                              "count": 1
                            },
            """;
        var table = Table(Box("Kill Team: Iron", "other.test", Listed + Gap));

        Assert.AreEqual(0, IdBackfill.Fill(table, Iron(), Hosts).Count);
    }

    [TestMethod]
    public void Fill_TwoGapsOfOneHit_GiveTheIdsOnce()
    {
        var table = Table(Box("Kill Team: Iron", "other.test", Gap + "," + Gap.Replace("Sergeants", "Sergeant")));

        Assert.AreEqual(1, IdBackfill.Fill(table, Iron(), Hosts).Count);
    }

    [TestMethod]
    public void Fill_HandBox_IsLeftAlone()
    {
        var table = Table(Box("Kill Team: Iron", "review.test", Gap));

        Assert.AreEqual(0, IdBackfill.Fill(table, Iron(), Hosts).Count);
        Assert.IsNull(table["boxes"]![0]!["readings"]![0]!["entries"]![0]!["catalogueId"]);
    }

    [TestMethod]
    public void Fill_NameWithoutOneHit_StaysAGap()
    {
        var table = Table(Box("Kill Team: Iron", "other.test", Gap.Replace("Sergeants", "Veteran")));

        Assert.AreEqual(0, IdBackfill.Fill(table, Iron(), Hosts).Count);
    }

    [TestMethod]
    public void IsServiceBox_EverySourceHostAllowlisted()
    {
        var table = Table(Box("A", "other.test", Gap), Box("B", "review.test", Gap));
        var boxes = table["boxes"]!.AsArray().Select(b => b!.AsObject()).ToList();

        Assert.IsTrue(IdBackfill.IsServiceBox(boxes[0], Hosts));
        Assert.IsFalse(IdBackfill.IsServiceBox(boxes[1], Hosts));
    }
}
