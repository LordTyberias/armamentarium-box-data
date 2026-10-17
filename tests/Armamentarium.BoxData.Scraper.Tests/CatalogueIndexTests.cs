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
    public void DocumentTypeDeclaration_IsUnreadable()
    {
        var index = new CatalogueIndex();
        var withDtd = Xml.Replace(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><!DOCTYPE catalogue [<!ENTITY a \"aaaa\">]>");

        Assert.IsFalse(index.AddCatalogue("40k", "Iron Brothers.cat", Bytes(withDtd)));
        Assert.IsNull(index.Find("40k", "Captain"));
    }

    [TestMethod]
    public void SpearheadIds_AreRead()
    {
        var index = new CatalogueIndex();

        Assert.IsTrue(index.AddCatalogue("aos", "Skaven.cat", Bytes(
            Xml.Replace("cat-iron", "sph-catalogue::skaven").Replace("se-captain", "sph::clawpack::01-grey-seer"))));
        Assert.AreEqual(new RootEntry("sph-catalogue::skaven", "sph::clawpack::01-grey-seer"), index.Find("aos", "Captain"));
    }

    [TestMethod]
    public void BrokenCatalogueOrStrangeId_IsUnreadable()
    {
        var index = new CatalogueIndex();

        Assert.IsFalse(index.AddCatalogue("40k", "Broken.cat", Bytes("<catalogue id=\"x\"")));
        Assert.IsFalse(index.AddCatalogue("40k", "Broken.json", Bytes("{\"catalogue\": ")));
        Assert.IsFalse(index.AddCatalogue("40k", "Odd.cat", Bytes(Xml.Replace("se-captain", "<b>x</b>".Replace("<", "&lt;")))));
        Assert.IsTrue(index.AddCatalogue("40k", "renovate.json", Bytes("{\"extends\": []}")));
    }

    [TestMethod]
    public void Archive_WithAnUnreadableCatalogue_LeavesTheReadingWithoutIds()
    {
        using var zip = Zip(("repo-main/Iron Brothers.cat", Xml), ("repo-main/Broken.cat", "<catalogue"));
        var index = new CatalogueIndex();
        var log = new List<string>();

        index.AddArchive("40k", zip, maxEntryBytes: 1 << 20, log.Add);

        Assert.IsNull(index.Find("40k", "Captain"));
        Assert.IsTrue(log.Any(l => l.Contains("Broken.cat", StringComparison.Ordinal)));
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    [TestMethod]
    public async Task Load_ReadsGoodSourcesAndLeavesFailingOnesWithoutIds()
    {
        using var zip = Zip(("r-main/Iron Brothers.cat", Xml));
        var bytes = zip.ToArray();
        var http = new HttpClient(new Stub(request => request.RequestUri!.AbsoluteUri switch
        {
            "https://codeload.github.com/o/good/zip/refs/heads/main" => new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) },
            "https://api.github.com/repos/o/badtag/releases/latest" => new(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"tag_name\":\"..\"}") },
            _ => new(System.Net.HttpStatusCode.NotFound),
        }));
        var log = new List<string>();

        var index = await CatalogueIndex.LoadAsync(
            http, log.Add, default,
            [new("40k", "o", "good", "main"), new("kt", "o", "badtag", null), new("aos", "o", "missing", "main"), new("aos", "o", "good", "main")]);

        Assert.AreEqual(new RootEntry("cat-iron", "se-captain"), index.Find("40k", "Captain"));
        Assert.IsNull(index.Find("aos", "Captain"));
        Assert.IsTrue(log.Any(l => l.StartsWith("kt:", StringComparison.Ordinal) && l.Contains("unreadable", StringComparison.Ordinal)));
    }

    private static MemoryStream Zip(params (string Name, string Text)[] files)
    {
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        zip.Position = 0;
        return zip;
    }

    [TestMethod]
    public void Archive_AnEntryOverTheLimit_LeavesTheReadingWithoutIds()
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

        Assert.IsNull(index.Find("40k", "Captain"));
        Assert.IsNull(index.Find("40k", "Warlord"));
        Assert.IsTrue(log.Any(l => l.Contains("Big.cat", StringComparison.Ordinal)));
    }
}
