using System.Text;

namespace Armamentarium.BoxData.Scraper.Tests;

// The catalogues and aliases below are written for these tests; none is copied from BSData or the committed table.
[TestClass]
public sealed class AliasesTests
{
    private const string Ultra = """
        <catalogue id="cat-ultra" name="Imperium - Ultra" library="false">
          <selectionEntries>
            <selectionEntry type="unit" id="se-wardens" name="Wardens of Ultra"/>
            <selectionEntry type="model" id="se-titus" name="Captain Titus"/>
          </selectionEntries>
        </catalogue>
        """;

    private const string Other = """
        <catalogue id="cat-other" name="Imperium - Other" library="false">
          <selectionEntries>
            <selectionEntry type="unit" id="se-other-wardens" name="Wardens of Ultra"/>
          </selectionEntries>
        </catalogue>
        """;

    private static CatalogueIndex Index()
    {
        var index = new CatalogueIndex();
        index.AddCatalogue("40k", "Ultra.cat", Encoding.UTF8.GetBytes(Ultra));
        index.AddCatalogue("40k", "Other.cat", Encoding.UTF8.GetBytes(Other));
        return index;
    }

    private static string File(string aliases) => $$"""{"format": 1, "aliases": [{{aliases}}]}""";

    private const string Gadriel = """{"reading": "40k", "name": "Ancient Gadriel", "target": "Wardens of Ultra", "catalogue": "Imperium - Ultra"}""";

    [TestMethod]
    public void Read_FileWithAliases_GivesThem()
    {
        var aliases = Aliases.Read(File(Gadriel));

        Assert.AreEqual(new Alias("40k", "Ancient Gadriel", "Wardens of Ultra", "Imperium - Ultra"), aliases.Single());
    }

    [TestMethod]
    public void Read_NoFile_GivesNone()
    {
        Assert.AreEqual(0, Aliases.Read(null).Count);
    }

    [TestMethod]
    public void Read_OtherFormatOrMissingField_Throws()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Aliases.Read("""{"format": 2, "aliases": []}"""));
        Assert.ThrowsExactly<InvalidDataException>(() => Aliases.Read(File("""{"reading": "40k", "name": "Ancient Gadriel", "target": "Wardens of Ultra"}""")));
        Assert.ThrowsExactly<InvalidDataException>(() => Aliases.Read(File(Gadriel.Replace("\"40k\"", "\"9e\""))));
    }

    [TestMethod]
    public void Find_AliasName_GivesTheTargetInItsCatalogue()
    {
        var index = Index();
        var unresolved = index.UseAliases(Aliases.Read(File(Gadriel)));

        Assert.AreEqual(0, unresolved.Count);
        Assert.AreEqual(new RootEntry("cat-ultra", "se-wardens"), index.Find("40k", "Ancient Gadriel"));
        Assert.AreEqual(new RootEntry("cat-ultra", "se-wardens"), index.Find("40k", "ancient  gadriel"));
        Assert.IsTrue(index.IsAlias("40k", "Ancient Gadriel"));
        Assert.IsFalse(index.IsAlias("40k", "Captain Titus"));
        Assert.IsNull(index.Find("kt", "Ancient Gadriel"));
    }

    [TestMethod]
    public void Find_AliasWhoseTargetIsNotOneEntry_IsAGapAndReported()
    {
        var index = Index();
        var wrongCatalogue = Gadriel.Replace("Imperium - Ultra", "Imperium - Nowhere");

        var unresolved = index.UseAliases(Aliases.Read(File(wrongCatalogue)));

        Assert.AreEqual("Ancient Gadriel", unresolved.Single().Name);
        Assert.IsNull(index.Find("40k", "Ancient Gadriel"));
        Assert.IsFalse(index.IsAlias("40k", "Ancient Gadriel"));
    }

    [TestMethod]
    public void Check_PairAnAliasGives_IsValid()
    {
        var index = Index();
        index.UseAliases(Aliases.Read(File(Gadriel)));

        Assert.AreEqual(IdState.Valid, index.Check("40k", new RootEntry("cat-ultra", "se-wardens"), "Ancient Gadriel"));
    }

    [TestMethod]
    public void CommittedFile_ReadsAndNamesEachAliasOnce()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, Aliases.File);
            if (System.IO.File.Exists(path))
            {
                var aliases = Aliases.Read(System.IO.File.ReadAllText(path));
                Assert.AreEqual(aliases.Count, aliases.Select(a => (a.Reading, ContentLines.Normalize(a.Name))).Distinct().Count());
                return;
            }
        }

        throw new AssertFailedException($"{Aliases.File} not found above the test output.");
    }
}
