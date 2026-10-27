namespace Armamentarium.BoxData.Scraper.Tests;

[TestClass]
public sealed class ProbeTests
{
    [TestMethod]
    public void SourceFor_AllowlistedHost_GivesThatSourceOnly()
    {
        var source = Probe.SourceFor(Sources.All, "radaddel.de");

        Assert.AreEqual("radaddel.de", source.Host);
    }

    [TestMethod]
    public void SourceFor_HostOffTheAllowlist_Throws()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => Probe.SourceFor(Sources.All, "shop.test"));

        StringAssert.Contains(ex.Message, "shop.test");
    }

    [TestMethod]
    public void Resolve_WithoutHost_IsNoProbe()
    {
        Assert.AreEqual((null, null), Probe.Resolve(["--dry-run", "--code", "1"], Sources.All));
    }

    [TestMethod]
    [DataRow(new[] { "--dry-run", "--code", "1", "--host" })]
    [DataRow(new[] { "--dry-run", "--host", "--code", "1" })]
    [DataRow(new[] { "--host", "radaddel.de", "--code", "1" })]
    [DataRow(new[] { "--dry-run", "--host", "radaddel.de" })]
    [DataRow(new[] { "--dry-run", "--code", "1", "--host", "shop.test" })]
    public void Resolve_IncompleteOrUnknown_GivesErrorAndNoSource(string[] args)
    {
        var (source, error) = Probe.Resolve(args, Sources.All);

        Assert.IsNull(source);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void Resolve_Complete_GivesTheSource()
    {
        var (source, error) = Probe.Resolve(["--dry-run", "--host", "radaddel.de", "--code", "1,2"], Sources.All);

        Assert.AreEqual("radaddel.de", source?.Host);
        Assert.IsNull(error);
    }

    [TestMethod]
    public void Describe_NoListing_SaysNotConfirmed()
    {
        CollectionAssert.AreEqual(new[] { "shop.test: not confirmed" }, Probe.Describe("shop.test", null).ToArray());
    }

    [TestMethod]
    public void Describe_Listing_NamesLinesAndBuildSentences()
    {
        var listing = new Listing(
            "shop.test",
            "https://shop.test/products/exo",
            German: false,
            "Kill Team - Exo",
            [new Line(2, "Swamp Lizards", NonPlayer: true, AlternativeBuild: false)],
            [])
        {
            Sentences =
            [
                new BuildSentence(
                    new Line(10, "Kasrkin", false, false),
                    Article: false,
                    [new BuildSentence(new Line(3, "Scarab Swarms", false, false), Article: false, [])]),
            ],
        };

        CollectionAssert.AreEqual(
            new[]
            {
                "shop.test: https://shop.test/products/exo Kill Team - Exo",
                "  line 2 x Swamp Lizards (npo)",
                "  build 10 x Kasrkin",
                "    plus 3 x Scarab Swarms",
            },
            Probe.Describe("shop.test", listing).ToArray());
    }

    [TestMethod]
    public void Describe_BuildOfNames_SaysNames()
    {
        var listing = new Listing("shop.test", "https://shop.test/p", German: false, "Hero", [], [])
        {
            Sentences = BuildSentences.Parse("<p>This kit builds Iron Hero and Rust Aide.</p>"),
        };

        CollectionAssert.AreEqual(
            new[] { "shop.test: https://shop.test/p Hero", "  build 1 x Iron Hero (names)", "    plus 1 x Rust Aide" },
            Probe.Describe("shop.test", listing).ToArray());
    }

    // ARMAM-688: the name over "on" and the members of a built unit, as the agreement may take them.
    [TestMethod]
    public void Describe_LongerNameAndMembers_AreShown()
    {
        var listing = new Listing("shop.test", "https://shop.test/p", German: false, "Box", [], [])
        {
            Sentences = BuildSentences.Parse(
                "<p>This kit builds a Rust Chieftain on Iron Steed.</p><p>This kit builds a Rust Gun Squad – consisting of three Rust Gunners and one Fire Warden.</p>"),
        };

        CollectionAssert.AreEqual(
            new[]
            {
                "shop.test: https://shop.test/p Box",
                "  build 1 x Rust Chieftain (article)",
                "    or 1 x Rust Chieftain on Iron Steed",
                "  build 1 x Rust Gun Squad (article)",
                "    member 3 x Rust Gunners",
                "    member 1 x Fire Warden",
            },
            Probe.Describe("shop.test", listing).ToArray());
    }
}
