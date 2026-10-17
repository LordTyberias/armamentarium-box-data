namespace Armamentarium.BoxData.Scraper.Tests;

[TestClass]
public sealed class AgreementTests
{
    private static Listing English(string host, params Line[] lines) =>
        new(host, $"https://{host}/p", false, "Box", lines, []);

    private static Listing German(string host, params Line[] lines) =>
        new(host, $"https://{host}/p", true, "Box", lines, []);

    private static Line L(int count, string name, bool npo = false, bool alt = false) => new(count, name, npo, alt);

    [TestMethod]
    public void Agree_TwoEnglishSourcesWithSameCounts_GivesCounts()
    {
        var result = Agreement.Agree([
            English("a.test", L(1, "Rider Bladeleader operative"), L(2, "Swamp Lizards")),
            English("b.test", L(1, "Rider Bladeleader"), L(2, "Swamp Lizard operatives")),
        ]);

        Assert.IsNull(result.Waiting);
        CollectionAssert.AreEqual(new[] { "Rider Bladeleader", "Swamp Lizards" }, result.Entries!.Select(e => e.Name).ToArray());
        CollectionAssert.AreEqual(new[] { 2 }, result.Entries![1].Counts.ToArray());
        CollectionAssert.AreEqual(new[] { "https://a.test/p", "https://b.test/p" }, result.Sources.ToArray());
    }

    [TestMethod]
    public void Agree_DifferentCounts_GivesCandidates()
    {
        var result = Agreement.Agree([
            English("a.test", L(4, "Hog Riders")),
            English("b.test", L(5, "Hog Riders")),
        ]);

        CollectionAssert.AreEqual(new[] { 4, 5 }, result.Entries!.Single().Counts.ToArray());
    }

    [TestMethod]
    public void Agree_EntryInOneSourceOnly_Waits()
    {
        var result = Agreement.Agree([
            English("a.test", L(1, "Seer"), L(5, "Blade Guards")),
            English("b.test", L(1, "Seer")),
        ]);

        Assert.IsNull(result.Entries);
        StringAssert.Contains(result.Waiting, "Blade Guards");
    }

    [TestMethod]
    public void Agree_GermanSourceWithSameSequence_ConfirmsByPosition()
    {
        var result = Agreement.Agree([
            English("a.test", L(1, "Seer"), L(5, "Blade Guards")),
            German("b.test", L(1, "Seher"), L(5, "Klingenwächter")),
        ]);

        Assert.IsNull(result.Waiting);
        CollectionAssert.AreEqual(new[] { "Seer", "Blade Guards" }, result.Entries!.Select(e => e.Name).ToArray());
        Assert.IsTrue(result.Entries!.All(e => e.Hosts.Count == 2));
    }

    [TestMethod]
    public void Agree_GermanSourceWithOtherSequence_DoesNotConfirm()
    {
        var result = Agreement.Agree([
            English("a.test", L(1, "Seer"), L(5, "Blade Guards")),
            German("b.test", L(1, "Seher"), L(4, "Klingenwächter")),
        ]);

        Assert.IsNull(result.Entries);
    }

    [TestMethod]
    public void Agree_OnlyGermanSources_Waits()
    {
        var result = Agreement.Agree([German("a.test", L(1, "Seher")), German("b.test", L(1, "Seher"))]);

        Assert.IsNull(result.Entries);
    }

    [TestMethod]
    public void Agree_ListingsWithoutLines_DoNotCount()
    {
        var result = Agreement.Agree([English("a.test", L(1, "Seer")), English("b.test")]);

        Assert.IsNull(result.Entries);
    }

    [TestMethod]
    public void Agree_CarriesNonPlayerAndAlternativeBuild()
    {
        var result = Agreement.Agree([
            English("a.test", L(10, "Beast Lads", npo: true), L(5, "Blade Guards", alt: true)),
            English("b.test", L(10, "Beast Lads"), L(5, "Blade Guards")),
        ]);

        Assert.IsTrue(result.Entries![0].NonPlayer);
        Assert.IsTrue(result.Entries![1].AlternativeBuild);
    }
}
