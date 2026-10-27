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

    // Read as names in the table until ARMAM-598: "… accompanied by", "accompanying …", "… usable by … wizards".
    [TestMethod]
    public void Parse_ClauseAfterTheName_EndsIt()
    {
        CollectionAssert.AreEqual(
            new[] { (10, "Swamp Lizards"), (3, "Rust Spells") },
            Read("<p>This kit builds 10 Swamp Lizards accompanied by a pet.</p><p>This kit builds 3 Rust Spells usable by Rust wizards.</p>"));
    }

    [TestMethod]
    public void Parse_AccompanyingBeforeTheName_IsNoPartOfIt()
    {
        CollectionAssert.AreEqual(
            new[] { (1, "Iron Strider"), (2, "Rust Drones") },
            Read("<p>This kit builds 1 Iron Strider plus 2 accompanying Rust Drones.</p>"));
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

    [TestMethod]
    public void Parse_FillerAndAdditionInTheName_AreTidied()
    {
        CollectionAssert.AreEqual(
            new[] { (1, "Blade Warden") },
            Read("<p>This kit builds a Blade Warden model.</p><p>This kit builds 10 Easy to build Rust Wraiths.</p>"));
    }

    // "builds Ciaphas Cain and Jurgen" names each model once; whether it counts decides Agreement (ADR 0003 Addendum ARMAM-642).
    [TestMethod]
    public void Parse_BuildOfNames_GivesEachNameOnce()
    {
        var sentence = BuildSentences.Parse("<p>This multipart plastic kit builds  Iron Hero and Rust Aide , a characterful pair of heroes.</p>").Single();

        Assert.IsTrue(sentence.Names);
        CollectionAssert.AreEqual(new[] { (1, "Iron Hero"), (1, "Rust Aide") },
            new[] { sentence.Build }.Concat(sentence.Additions.Select(a => a.Build)).Select(l => (l.Count, l.Name)).ToArray());
    }

    [TestMethod]
    public void Parse_BuildOfLowerCaseWords_IsNoNames()
    {
        Assert.AreEqual(0, BuildSentences.Parse("<p>This kit builds the hero and his retinue.</p>").Count);
    }

    // Two units joined by "and" read as one model each; Agreement takes it only when both names hit and two sources agree
    // (ADR 0003 Addendum ARMAM-642). Pinned here so a change to that rule is a decision.
    [TestMethod]
    public void Parse_BuildOfTwoUnitNames_ReadsAsNames()
    {
        var sentence = BuildSentences.Parse("<p>This kit builds Hellblasters and Eradicators.</p>").Single();

        Assert.IsTrue(sentence.Names);
    }

    // ARMAM-688: "… makes a striking addition to any army" names no model; a build later in the sentence still counts.
    [TestMethod]
    public void Parse_ArticleBuildWithoutACapital_IsNoBuild()
    {
        CollectionAssert.AreEqual(Array.Empty<(int, string)>(), Read("<p>The Iron Strider makes a striking addition to any army.</p>"));
        CollectionAssert.AreEqual(
            new[] { (10, "Iron Brothers") },
            Read("<p>The Iron Strider makes a fine centrepiece, and this kit builds 10 Iron Brothers.</p>"));
    }

    // ARMAM-688: "builds a Rust Chieftain on Iron Steed" stopped at "on" and named the hero on foot.
    [TestMethod]
    public void Parse_NameOnAMount_OffersTheLongerName()
    {
        var sentence = BuildSentences.Parse("<p>This boxed set builds a Rust Chieftain on Iron Steed, an imposing mounted leader.</p>").Single();

        Assert.AreEqual("Rust Chieftain", sentence.Build.Name);
        Assert.AreEqual("Rust Chieftain on Iron Steed", sentence.Longer?.Name);
        Assert.AreEqual(1, sentence.Longer?.Count);
    }

    // ARMAM-688: "builds a <unit> – consisting of three … and one …" names the unit's models.
    [TestMethod]
    public void Parse_UnitConsistingOfModels_GivesTheMembers()
    {
        var sentence = BuildSentences.Parse(
            "<p>This kit builds a Rust Gun Squad – consisting of three Rust Gunners and one Fire Warden – for use in your armies.</p>").Single();

        Assert.AreEqual("Rust Gun Squad", sentence.Build.Name);
        Assert.IsTrue(sentence.Article);
        CollectionAssert.AreEqual(new[] { (3, "Rust Gunners"), (1, "Fire Warden") }, sentence.Members.Select(m => (m.Count, m.Name)).ToArray());
        Assert.AreEqual(0, sentence.Additions.Count);
    }
}
