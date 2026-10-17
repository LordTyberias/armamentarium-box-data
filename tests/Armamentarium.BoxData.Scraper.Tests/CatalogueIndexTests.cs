using System.IO.Compression;
using System.Text;

namespace Armamentarium.BoxData.Scraper.Tests;

// The catalogues below are written for these tests in the shapes BattleScribe uses; none is copied from BSData.
[TestClass]
public sealed class CatalogueIndexTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <catalogue id="cat-iron" name="Iron Brothers" library="false" xmlns="http://www.battlescribe.net/schema/catalogueSchema">
          <selectionEntries>
            <selectionEntry type="unit" id="se-captain" name="Captain">
              <selectionEntries>
                <selectionEntry type="model" id="se-inner" name="Bodyguard"/>
              </selectionEntries>
            </selectionEntry>
            <selectionEntry type="upgrade" id="se-relic" name="Relic Blade"/>
          </selectionEntries>
          <entryLinks>
            <entryLink id="el-troopers" name="Line Troopers" targetId="lib-troopers" type="selectionEntry"/>
          </entryLinks>
          <sharedSelectionEntries>
            <selectionEntry type="unit" id="se-shared" name="Shared Hero"/>
          </sharedSelectionEntries>
        </catalogue>
        """;

    private const string Json = """
        {"catalogue": {"id": "cat-iron", "name": "Iron Brothers", "library": false,
          "selectionEntries": [{"type": "unit", "id": "se-captain", "name": "Captain",
            "selectionEntries": [{"type": "model", "id": "se-inner", "name": "Bodyguard"}]}],
          "entryLinks": [{"id": "el-troopers", "name": "Line Troopers", "targetId": "lib-troopers", "type": "selectionEntry"}]}}
        """;

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [TestMethod]
    public void Xml_RootUnitsAndLinks_AreFoundNestedAndUpgradesAreNot()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Iron Brothers.cat", Bytes(Xml));

        Assert.AreEqual(new RootEntry("cat-iron", "se-captain"), index.Find("40k", "Captain"));
        Assert.AreEqual(new RootEntry("cat-iron", "lib-troopers"), index.Find("40k", "Line Trooper"));
        Assert.IsNull(index.Find("40k", "Bodyguard"));
        Assert.IsNull(index.Find("40k", "Relic Blade"));
        Assert.IsNull(index.Find("40k", "Shared Hero"));
        Assert.IsNull(index.Find("kt", "Captain"));
    }

    [TestMethod]
    public void Json_ReadsLikeXml()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Iron Brothers.json", Bytes(Json));

        Assert.AreEqual(new RootEntry("cat-iron", "se-captain"), index.Find("40k", "Captain"));
        Assert.AreEqual(new RootEntry("cat-iron", "lib-troopers"), index.Find("40k", "Line Troopers"));
        Assert.IsNull(index.Find("40k", "Bodyguard"));
    }

    [TestMethod]
    public void SamePairFromTwoEditions_IsOneHit()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Iron Brothers.cat", Bytes(Xml));
        index.AddCatalogue("40k", "Iron Brothers.json", Bytes(Json));

        Assert.AreEqual(new RootEntry("cat-iron", "se-captain"), index.Find("40k", "Captain"));
    }

    [TestMethod]
    public void NameInTwoCatalogues_IsNoHit()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Iron Brothers.cat", Bytes(Xml));
        index.AddCatalogue("40k", "Greenskins.cat", Bytes(Xml.Replace("cat-iron", "cat-green").Replace("se-captain", "se-boss")));

        Assert.IsNull(index.Find("40k", "Captain"));
    }

    [TestMethod]
    public void OperativeWordsAndPlural_AreIgnored()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("kt", "Iron Brothers.cat", Bytes(Xml));

        Assert.AreEqual(new RootEntry("cat-iron", "lib-troopers"), index.Find("kt", "Line Troopers Kill Team operatives"));
    }

    [TestMethod]
    public void LibraryAndGameSystem_AreNoCatalogues()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Library.cat", Bytes(Xml.Replace("library=\"false\"", "library=\"true\"")));
        index.AddCatalogue("40k", "System.json", Bytes("""{"gameSystem": {"id": "gst", "name": "System"}}"""));

        Assert.IsNull(index.Find("40k", "Captain"));
    }

    [TestMethod]
    public void UnavailableReading_FindsNothing()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("aos", "Iron Brothers.cat", Bytes(Xml));
        index.MarkUnavailable("aos");

        Assert.IsNull(index.Find("aos", "Captain"));
    }

    [TestMethod]
    public void Archive_ReadsCataloguesAndSkipsAnEntryOverTheLimit()
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in new[]
            {
                ("repo-main/Iron Brothers.cat", Xml),
                ("repo-main/Big.cat", Xml.Replace("cat-iron", "cat-big").Replace("Captain", "Warlord") + new string(' ', 2048)),
                ("repo-main/renovate.json", "{\"extends\": []}"),
            })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        var index = new CatalogueIndex();
        var log = new List<string>();
        zip.Position = 0;
        index.AddArchive("40k", zip, maxEntryBytes: 2048, log.Add);

        Assert.AreEqual(new RootEntry("cat-iron", "se-captain"), index.Find("40k", "Captain"));
        Assert.IsNull(index.Find("40k", "Warlord"));
        Assert.IsTrue(log.Any(l => l.Contains("Big.cat", StringComparison.Ordinal)));
    }
}
