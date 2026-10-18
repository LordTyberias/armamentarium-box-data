using System.Text;

namespace Armamentarium.BoxData.Scraper.Tests;

// The catalogue and the table below are written for these tests; none is copied from BSData or the committed table.
[TestClass]
public sealed class IdCheckTests
{
    private const string Catalogue = """
        <catalogue id="cat-iron" name="Iron Brothers" library="false">
          <selectionEntries>
            <selectionEntry type="model" id="se-sergeant" name="Sergeant"/>
            <selectionEntry type="model" id="se-gunner" name="Gunner"/>
            <selectionEntry type="model" id="se-medic" name="Medic"/>
          </selectionEntries>
        </catalogue>
        """;

    private const string Table = """
        {
          "$schema": "./box-contents.schema.json",
          "format": 1,
          "boxes": [
            {
              "name": "Kill Team: Iron",
              "productCodes": [
                "60010199001"
              ],
              "sources": [
                "https://a.test/k",
                "https://b.test/k"
              ],
              "readings": [
                {
                  "reading": "kt",
                  "entries": [
                    {
                      "role": "team",
                      "catalogueId": "cat-iron",
                      "entryId": "se-sergeant",
                      "name": "Sergeant",
                      "count": 1
                    },
                    {
                      "role": "team",
                      "catalogueId": "cat-iron",
                      "entryId": "se-gone",
                      "name": "Veteran",
                      "count": 1
                    },
                    {
                      "role": "team",
                      "catalogueId": "cat-iron",
                      "choice": [
                        {
                          "entryId": "se-gunner",
                          "name": "Gunner"
                        },
                        {
                          "entryId": "se-medic",
                          "name": "Medic"
                        },
                        {
                          "entryId": "se-lost",
                          "name": "Breacher"
                        }
                      ],
                      "name": "Iron Operatives",
                      "count": 4
                    },
                    {
                      "role": "team",
                      "catalogueId": "cat-iron",
                      "choice": [
                        {
                          "entryId": "se-gunner",
                          "name": "Gunner"
                        },
                        {
                          "entryId": "se-lost",
                          "name": "Breacher"
                        }
                      ],
                      "name": "Iron Pair",
                      "count": 2
                    },
                    {
                      "role": "npo",
                      "reading": "40k",
                      "catalogueId": "cat-iron",
                      "entryId": "se-sergeant",
                      "name": "Sergeant",
                      "count": 3
                    },
                    {
                      "role": "team",
                      "name": "Seer",
                      "count": 1
                    }
                  ]
                }
              ]
            },
            {
              "name": "Combat Patrol: Iron",
              "productCodes": [
                "60010199002"
              ],
              "sources": [
                "https://a.test/c",
                "https://b.test/c"
              ],
              "generation": "current"
            }
          ]
        }

        """;

    private static CatalogueIndex Index(params string[] readings)
    {
        var index = new CatalogueIndex();
        foreach (var reading in readings)
        {
            index.AddCatalogue(reading, "Iron Brothers.cat", Encoding.UTF8.GetBytes(Catalogue));
        }

        return index;
    }

    [TestMethod]
    public void Findings_EveryPairAndOption_WithTheEntrysOwnReading()
    {
        var findings = IdCheck.Findings(Scraper.Table.Read(Table), Index("kt"));

        Assert.AreEqual(8, findings.Count);
        Assert.AreEqual(4, findings.Count(f => f.State == IdState.Valid));
        CollectionAssert.AreEquivalent(
            new[] { "Veteran", "Breacher", "Breacher" },
            findings.Where(f => f.State == IdState.Orphaned).Select(f => f.Name).ToArray());
        var npo = findings.Single(f => f.Reading == "40k");
        Assert.AreEqual(IdState.Unchecked, npo.State);
        Assert.AreEqual("Kill Team: Iron", npo.Box);
    }

    [TestMethod]
    public void Findings_NameNoLongerMatching_IsRenamed()
    {
        var table = Scraper.Table.Read(Table);
        table["boxes"]![0]!["readings"]![0]!["entries"]![0]!["name"] = "Captain";

        var findings = IdCheck.Findings(table, Index("kt", "40k"));

        Assert.AreEqual(IdState.Renamed, findings.Single(f => f.Name == "Captain").State);
        Assert.AreEqual(IdState.Valid, findings.Single(f => f.Reading == "40k").State);
    }

    [TestMethod]
    public void RemoveOrphans_LeavesGapsAndKeepsTheRest()
    {
        var table = Scraper.Table.Read(Table);

        Assert.AreEqual(3, IdCheck.RemoveOrphans(table, Index("kt", "40k")));

        var entries = table["boxes"]![0]!["readings"]![0]!["entries"]!.AsArray();
        // The fixed entry whose pair is gone keeps name and count.
        CollectionAssert.AreEqual(new[] { "role", "name", "count" }, entries[1]!.AsObject().Select(p => p.Key).ToArray());
        // One option of three goes; two stay a choice.
        CollectionAssert.AreEqual(
            new[] { "Gunner", "Medic" }, entries[2]!["choice"]!.AsArray().Select(o => (string)o!["name"]!).ToArray());
        Assert.AreEqual("cat-iron", (string)entries[2]!["catalogueId"]!);
        // One option of two goes; a single one is no choice (the schema asks for two), the entry becomes a gap.
        CollectionAssert.AreEqual(new[] { "role", "name", "count" }, entries[3]!.AsObject().Select(p => p.Key).ToArray());
        Assert.AreEqual("se-sergeant", (string)entries[0]!["entryId"]!);
        Assert.AreEqual("40k", (string)entries[4]!["reading"]!);
        Assert.AreEqual("current", (string)table["boxes"]![1]!["generation"]!);
    }

    [TestMethod]
    public void RemoveOrphans_UncheckedReadingAndValidIds_StayAsTheyAre()
    {
        var table = Scraper.Table.Read(Table.Replace("se-gone", "se-medic").Replace("se-lost", "se-sergeant"));
        var before = Scraper.Table.Write(table);

        Assert.AreEqual(0, IdCheck.RemoveOrphans(table, Index("kt")));
        Assert.AreEqual(before, Scraper.Table.Write(table));
    }
}
