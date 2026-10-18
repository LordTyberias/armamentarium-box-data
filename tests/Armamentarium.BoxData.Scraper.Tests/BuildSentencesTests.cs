namespace Armamentarium.BoxData.Scraper.Tests;

// The texts below are written for these tests in the shapes the shops use; none is copied from a shop.
[TestClass]
public sealed class BuildSentencesTests
{
    private static (int Count, string Name)[] Read(string html) =>
        BuildSentences.Parse(html).SelectMany(s => new[] { s.Build }.Concat(s.Additions.Select(a => a.Build))).Select(l => (l.Count, l.Name)).ToArray();

    [TestMethod]
    public void Parse_CountByArticle_IsMarked()
    {
        var sentences = BuildSentences.Parse("<p>This kit builds an Iron Strider.</p><p>This kit makes one Iron Strider.</p>");

        CollectionAssert.AreEqual(new[] { true }, sentences.Select(s => s.Article).ToArray());
    }

    [TestMethod]
    public void Parse_OrBetweenWeapons_KeepsTheAdditions()
    {
        var sentence = BuildSentences.Parse(
            "<p>This kit builds ten Iron Brothers – each with a choice of a rust rifle or a rust reaper – as well as three Rust Swarms.</p>").Single();

        Assert.AreEqual(10, sentence.Build.Count);
        CollectionAssert.AreEqual(new[] { (3, "Rust Swarms") }, sentence.Additions.Select(a => (a.Build.Count, a.Build.Name)).ToArray());
    }

    [TestMethod]
    public void Parse_AdditionByArticle_IsMarked()
    {
        var sentence = BuildSentences.Parse("<p>This kit builds 10 Iron Brothers, each armed with a rust rifle and a rust knife.</p>").Single();

        Assert.IsTrue(sentence.Additions.Single().Article);
    }

    [TestMethod]
    public void Parse_SentenceSeveralTimes_IsOneSentence()
    {
        Assert.AreEqual(1, BuildSentences.Parse("<p>Builds 10 Iron Brothers</p><p>This kit builds 10 Iron Brothers.</p>").Count);
    }

    [TestMethod]
    public void Parse_SquadOfDigits_GivesCountAndName()
    {
        CollectionAssert.AreEqual(new[] { (10, "Iron Brothers") }, Read("<p>This kit builds a squad of 10 Iron Brothers.</p>"));
    }

    [TestMethod]
    public void Parse_NumberWord_GivesCount()
    {
        CollectionAssert.AreEqual(new[] { (6, "Swamp Lizards operatives") }, Read("<p>This kit builds six Swamp Lizards operatives.</p>"));
    }

    [TestMethod]
    public void Parse_ArticleBeforeTheName_IsOne()
    {
        CollectionAssert.AreEqual(
            new[] { (1, "Iron Strider") },
            Read("<p>This kit builds an Iron Strider. Alternatively, you can build a Strider Commander.</p>"));
    }

    [TestMethod]
    public void Parse_ComponentsBeforeTheBuild_AreNeverTheCount()
    {
        CollectionAssert.AreEqual(
            new[] { (10, "Blade Guards") },
            Read("<p>This set comprises 108 components, with which you can build 10 Blade Guards.</p>"));
    }

    [TestMethod]
    public void Parse_BasesAndPartsWithoutABuild_GiveNothing()
    {
        Assert.AreEqual(0, Read("<p>Comes as 79 plastic components and 10 Citadel 32mm Round bases.</p>").Length);
    }

    [TestMethod]
    public void Parse_AdditionalModels_AreASecondEntry()
    {
        CollectionAssert.AreEqual(
            new[] { (10, "Iron Brothers"), (3, "Rust Swarms") },
            Read("<p>This kit builds ten Iron Brothers with various options, as well as three Rust Swarms.</p>"));
    }

    [TestMethod]
    public void Parse_AlternativeBuild_CountsTheBaseBuild()
    {
        CollectionAssert.AreEqual(
            new[] { (20, "Iron Brothers") },
            Read("<p>This kit builds 20 Iron Brothers, as a squad of 20 models or two units of 10, and 2 Rust Swarms.</p>"));
    }

    [TestMethod]
    public void Parse_GermanBuild_GivesCount()
    {
        CollectionAssert.AreEqual(new[] { (10, "Eisenbrüder") }, Read("<p>Mit diesem Set kannst du 10 Eisenbrüder bauen.</p>"));
        CollectionAssert.AreEqual(new[] { (10, "Eisenbrüder") }, Read("<p>Dieses Set ermöglicht den Bau von 10 Eisenbrüder.</p>"));
        CollectionAssert.AreEqual(new[] { (1, "Eisenläufer") }, Read("<p>Mit diesem Bausatz kannst du einen Eisenläufer bauen.</p>"));
        CollectionAssert.AreEqual(new[] { (10, "Klingen") }, Read("<p>Damit lässt sich einen Trupp aus 10 Klingen bauen.</p>"));
    }

    [TestMethod]
    public void Parse_GermanPartsBeforeTheBuild_AreNeverTheCount()
    {
        CollectionAssert.AreEqual(
            new[] { (10, "Eisenbrüder"), (3, "Rostschwärme") },
            Read("<p>Das Set enthält 70 Kunststoffteile aus denen du zehn Eisenbrüder bauen kannst sowie drei Rostschwärme.</p>"));
    }

    [TestMethod]
    public void Parse_OddNumbers_GiveNothing()
    {
        Assert.AreEqual(0, Read("<p>This kit builds 1000 Iron Brothers.</p>").Length);
        Assert.AreEqual(0, Read("<p>This kit builds ١٠ Iron Brothers.</p>").Length);
    }

    [TestMethod]
    public void Parse_HeadsAndTransfersAfterTheBuild_AreDropped()
    {
        CollectionAssert.AreEqual(
            new[] { (10, "Iron Brothers") },
            Read("<p>This kit builds 10 Iron Brothers and 24 heads, plus 2 transfer sheets.</p>"));
    }

    [TestMethod]
    public void Parse_ElfInAName_EndsNothing()
    {
        CollectionAssert.AreEqual(new[] { (10, "High Elf Spearmen") }, Read("<p>This kit builds 10 High Elf Spearmen.</p>"));
    }

    [TestMethod]
    public void Parse_ZeroCount_GivesNothing()
    {
        Assert.AreEqual(0, Read("<p>This kit builds 0 Iron Brothers.</p>").Length);
    }

    [TestMethod]
    public void Parse_Constructs_IsABuildVerb()
    {
        CollectionAssert.AreEqual(new[] { (10, "Iron Brothers") }, Read("<p>This kit constructs 10 Iron Brothers.</p>"));
    }
}
