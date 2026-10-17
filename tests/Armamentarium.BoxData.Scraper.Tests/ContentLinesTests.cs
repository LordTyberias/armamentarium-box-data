namespace Armamentarium.BoxData.Scraper.Tests;

// The texts below are written for these tests in the shapes the shops use; none is copied from a shop.
[TestClass]
public sealed class ContentLinesTests
{
    [TestMethod]
    public void Parse_ListItems_ReturnsCountAndCleanName()
    {
        var lines = ContentLines.Parse(
            "<ul><li>1x Rider Bladeleader Operative (Miniatur)</li><li>2x Swamp Lizards (Miniaturen)</li></ul>");

        CollectionAssert.AreEqual(
            new[] { new Line(1, "Rider Bladeleader Operative", false, false), new Line(2, "Swamp Lizards", false, false) },
            lines.ToArray());
    }

    [TestMethod]
    public void Parse_DashLinesWithoutX_AreLines()
    {
        var lines = ContentLines.Parse("<p>– 1 Rider Bladeleader operative<br>– 3 Swamp Lizard operatives</p>");

        CollectionAssert.AreEqual(new[] { 1, 3 }, lines.Select(l => l.Count).ToArray());
    }

    [TestMethod]
    public void Parse_TotalsAndAccessories_AreDropped()
    {
        var lines = ContentLines.Parse(
            "• 12x Citadel Miniatures<br>• 6x Iron Brothers Kill Team operatives, cast in grey plastic<br>" +
            "• 10x D6 dice<br>• 1x double-sided game board<br>• 2x sheets of slot-together MDF terrain<br>" +
            "• 1x token sheet<br>• 36x Datacards (Karten)<br>• 12x Citadel Bases (32mm, rund)<br>" +
            "• 1x Campaign Dossier (Buch)<br>• 2x Referenzbögen<br>• 1x Transfer sheet");

        CollectionAssert.AreEqual(
            new[] { new Line(6, "Iron Brothers Kill Team operatives", false, false) }, lines.ToArray());
    }

    [TestMethod]
    public void Parse_LinesUnderATerrainHeading_AreDropped()
    {
        var lines = ContentLines.Parse(
            "<p>12 Iron Brothers</p><p>– 1 Captain<br>– 5 Line Troopers</p>" +
            "<p>9 plastic push-fit terrain pieces</p><p>– 4 large Ruins<br>– 2 Galvanic Pylons</p>" +
            "<p>20 Greenskins</p><p>– 20 Lads</p>");

        CollectionAssert.AreEqual(new[] { "Captain", "Line Troopers", "Lads" }, lines.Select(l => l.Name).ToArray());
    }

    [TestMethod]
    public void Parse_InlineDashList_SplitsIntoLines()
    {
        var lines = ContentLines.Parse("<p>Inhalt: 1x Seer - 5x Blade Guards - 10 x Avengers</p>");

        CollectionAssert.AreEqual(new[] { 1, 5, 10 }, lines.Select(l => l.Count).ToArray());
        Assert.AreEqual("Avengers", lines[2].Name);
    }

    [TestMethod]
    public void Parse_AlternativeBuild_IsFlaggedAndCut()
    {
        var lines = ContentLines.Parse("<li>5x Blade Guards (which can alternatively be built as Spear Guards)</li>");

        Assert.AreEqual(new Line(5, "Blade Guards", false, true), lines.Single());
    }

    [TestMethod]
    public void Parse_NonPlayerMarker_IsFlagged()
    {
        var lines = ContentLines.Parse(
            "<li>10x Beast Lads Nichtspieler-Operatives (Miniaturen)</li><li>5x Hog Riders non-player operatives</li>");

        Assert.IsTrue(lines.All(l => l.NonPlayer));
    }

    [TestMethod]
    public void Parse_Prose_IsIgnored()
    {
        var lines = ContentLines.Parse("<p>This box contains 10 Beast Lads and five Hog Riders for the missions.</p>");

        Assert.AreEqual(0, lines.Count);
    }

    [TestMethod]
    public void Normalize_IgnoresOperativeWordsPluralAndCase()
    {
        Assert.AreEqual(
            ContentLines.Normalize("Swamp Lizard operative"), ContentLines.Normalize("Swamp Lizards Operatives"));
        Assert.AreEqual(
            ContentLines.Normalize("Iron Brothers operatives"), ContentLines.Normalize("Iron Brothers Kill Team operatives"));
        Assert.AreEqual(
            ContentLines.Normalize("Beast Lads"), ContentLines.Normalize("Beast Lads non-player operatives"));
    }
}
