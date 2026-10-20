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
    public void Agree_SameNameTwiceInOneListing_Waits()
    {
        var result = Agreement.Agree([
            English("a.test", L(2, "Sergeant"), L(1, "Sergeant")),
            English("b.test", L(2, "Sergeant"), L(1, "Sergeant")),
        ]);

        Assert.IsNull(result.Entries);
        StringAssert.Contains(result.Waiting, "twice");
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

    // The root entries "Iron Brothers", "Primaris Iron Brothers", "Navy Boarders" and "Iron Strider" name, as the catalogue
    // index would answer.
    private static string? Root(string name) => ContentLines.Normalize(name) switch
    {
        "iron brother" or "primari iron brother" => "40k:root:brothers",
        "navy boarder" => "40k:root:boarders",
        "iron strider" => "40k:root:strider",
        _ => null,
    };

    private static BuildSentence S(int count, string name, bool article = false, params Line[] additions) =>
        new(L(count, name), article, additions.Select(a => new BuildSentence(a, false, [])).ToList());

    private static Listing WithSentences(Listing listing, params BuildSentence[] sentences) => listing with { Sentences = sentences };

    [TestMethod]
    public void Agree_SentenceNamingARootEntry_WinsOverTheSplitLines()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test", L(1, "Navy Sergeant"), L(9, "Navy Armsmen")), S(10, "Navy Boarders")),
            WithSentences(English("b.test", L(1, "Navy Sergeant"), L(9, "Navy Armsmen")), S(10, "Navy Boarders")),
        ], Root);

        Assert.IsNull(result.Waiting);
        CollectionAssert.AreEqual(new[] { "Navy Boarders" }, result.Entries!.Select(e => e.Name).ToArray());
        CollectionAssert.AreEqual(new[] { 10 }, result.Entries![0].Counts.ToArray());
    }

    [TestMethod]
    public void Agree_SentenceNamingNoRootEntry_LeavesTheLines()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test", L(1, "Seer"), L(5, "Blade Guards")), S(6, "Seer Guards")),
            WithSentences(English("b.test", L(1, "Seer"), L(5, "Blade Guards")), S(6, "Seer Guards")),
        ], Root);

        CollectionAssert.AreEqual(new[] { "Seer", "Blade Guards" }, result.Entries!.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void Agree_TwoNamesOfOneRootEntry_ConfirmEachOther()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test"), S(10, "Primaris Iron Brothers")),
            WithSentences(English("b.test"), S(10, "Iron Brothers")),
        ], Root);

        Assert.IsNull(result.Waiting);
        Assert.AreEqual(2, result.Entries!.Single().Hosts.Count);
    }

    [TestMethod]
    public void Agree_TwoCountsOfOneEntryInOneSource_DropThatSource()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test"), S(10, "Iron Brothers"), S(9, "Iron Brothers")),
            WithSentences(English("b.test"), S(10, "Iron Brothers")),
            WithSentences(English("c.test"), S(10, "Iron Brothers")),
        ], Root);

        CollectionAssert.AreEqual(new[] { 10 }, result.Entries!.Single().Counts.ToArray());
        CollectionAssert.AreEqual(new[] { "b.test", "c.test" }, result.Entries!.Single().Hosts.ToArray());
    }

    [TestMethod]
    public void Agree_AdditionInOneSourceOnly_Waits()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test"), S(10, "Iron Brothers", additions: L(3, "Rust Swarms"))),
            WithSentences(English("b.test"), S(10, "Iron Brothers")),
        ], Root);

        Assert.IsNull(result.Entries);
        StringAssert.Contains(result.Waiting, "Rust Swarms");
    }

    [TestMethod]
    public void Agree_AdditionInTwoSources_IsAnEntry()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test"), S(10, "Iron Brothers", additions: L(3, "Rust Swarms"))),
            WithSentences(English("b.test"), S(10, "Iron Brothers", additions: L(3, "Rust Swarms"))),
        ], Root);

        CollectionAssert.AreEqual(new[] { "Iron Brothers", "Rust Swarms" }, result.Entries!.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void Agree_AdditionByArticleWithoutRootEntry_CountsNothing()
    {
        BuildSentence WithLaspistol() => new(L(10, "Iron Brothers"), false, [new BuildSentence(L(1, "laspistol"), true, [])]);

        var result = Agreement.Agree([
            WithSentences(English("a.test"), WithLaspistol()),
            WithSentences(English("b.test"), WithLaspistol()),
        ], Root);

        CollectionAssert.AreEqual(new[] { "Iron Brothers" }, result.Entries!.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void Agree_ArticleWithoutRootEntry_CountsNothing()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test"), S(1, "unique boarding party", article: true), S(1, "Iron Strider", article: true)),
            WithSentences(English("b.test"), S(1, "unique boarding party", article: true), S(1, "Iron Strider", article: true)),
        ], Root);

        CollectionAssert.AreEqual(new[] { "Iron Strider" }, result.Entries!.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void Agree_LaterSentenceOfTheSameKit_CountsNothing()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test"), S(10, "Iron Brothers"), S(10, "customizable Iron Brothers")),
            WithSentences(English("b.test"), S(10, "Iron Brothers"), S(10, "customizable Iron Brothers")),
        ], Root);

        CollectionAssert.AreEqual(new[] { "Iron Brothers" }, result.Entries!.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void Agree_SourceSharingNoEntry_DoesNotHoldTheBox()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test"), S(10, "Iron Brothers")),
            WithSentences(English("b.test"), S(10, "Iron Brothers")),
            English("c.test", L(1, "Brother Sergeant"), L(9, "Brothers with bolters")),
        ], Root);

        Assert.IsNull(result.Waiting);
        CollectionAssert.AreEqual(new[] { "https://a.test/p", "https://b.test/p" }, result.Sources.ToArray());
    }

    [TestMethod]
    public void Agree_GermanSentence_ConfirmsByPosition()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test"), S(10, "Iron Brothers")),
            WithSentences(German("b.test", L(1, "Sergeant"), L(9, "Brüder")), S(10, "Eisenbrüder")),
        ], Root);

        Assert.IsNull(result.Waiting);
        Assert.AreEqual(2, result.Entries!.Single().Hosts.Count);
    }

    [TestMethod]
    public void Agree_SentencesWithoutRootEntries_CompareNames()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test"), S(6, "Swamp Lizards operatives")),
            WithSentences(English("b.test"), S(6, "Swamp Lizards")),
        ]);

        Assert.IsNull(result.Waiting);
        CollectionAssert.AreEqual(new[] { 6 }, result.Entries!.Single().Counts.ToArray());
    }

    [TestMethod]
    public void Agree_NoCountAnywhere_SaysSo()
    {
        var result = Agreement.Agree([English("a.test"), English("b.test")]);

        StringAssert.Contains(result.Waiting, "model count");
    }

    [TestMethod]
    public void Agree_GermanConfirmation_DoesNotKeepASourceSharingNoEntry()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test"), S(10, "Iron Brothers")),
            WithSentences(English("b.test"), S(10, "Iron Brothers")),
            English("c.test", L(5, "Seer Guards"), L(5, "Blade Guards")),
            German("d.test", L(5, "Seherwachen"), L(5, "Klingenwächter")),
        ], Root);

        CollectionAssert.AreEqual(new[] { "Iron Brothers" }, result.Entries!.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void Agree_SourcesSharingNoEntry_SaySo()
    {
        var result = Agreement.Agree([English("a.test", L(10, "Iron Brothers")), English("b.test", L(5, "Seer Guards"))]);

        Assert.AreEqual("no two sources share an entry", result.Waiting);
    }

    private static BuildSentence Names(params string[] names) =>
        new(L(1, names[0]), true, names.Skip(1).Select(n => new BuildSentence(L(1, n), true, [])).ToList()) { Names = true };

    // The key the run gives an alias: its target's root entry and its own name (Runner.RootOf).
    private static string? HeroRoot(string name) => ContentLines.Normalize(name) switch
    {
        "iron hero" => "40k:root:hero",
        "rust aide" => "40k:root:hero|alias:rust aide",
        _ => null,
    };

    [TestMethod]
    public void Agree_BuildOfNamesThatAllHit_CountsEachOnce()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test", L(1, "Iron Hero"), L(1, "Rust Aide")), Names("Iron Hero", "Rust Aide")),
            WithSentences(English("b.test"), Names("Iron Hero", "Rust Aide")),
        ], HeroRoot);

        Assert.IsNull(result.Waiting);
        CollectionAssert.AreEqual(new[] { "Iron Hero", "Rust Aide" }, result.Entries!.Select(e => e.Name).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 2 }, result.Entries!.Select(e => e.Hosts.Count).ToArray());
    }

    [TestMethod]
    public void Agree_BuildOfNamesWithOneMiss_CountsNothing()
    {
        var result = Agreement.Agree([
            WithSentences(English("a.test"), Names("Iron Hero", "Loyal Hound")),
            WithSentences(English("b.test"), Names("Iron Hero", "Loyal Hound")),
        ], HeroRoot);

        StringAssert.Contains(result.Waiting, "model count");
    }
}
