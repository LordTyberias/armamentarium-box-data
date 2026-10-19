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
    public void Parse_GroupTotalOverItsMembers_IsDropped()
    {
        var lines = ContentLines.Parse(
            "<p>7x Iron Brothers</p><p>Grim warriors of the line.</p>" +
            "<p>- 1x Captain: Leads the brothers into battle, sword in hand</p>" +
            "<p>- 6x Line Troopers – Steady soldiers with rifles</p>" +
            "<p>3x Greenskins</p><p>- 2x Lads</p>");

        CollectionAssert.AreEqual(
            new[] { new Line(1, "Captain", false, false), new Line(6, "Line Troopers", false, false),
                new Line(3, "Greenskins", false, false), new Line(2, "Lads", false, false) },
            lines.ToArray());
    }

    [TestMethod]
    public void Tidy_PluralBoards_IsNoModel()
    {
        Assert.IsNull(ContentLines.Tidy("30\" by 22.4\" double-sided gaming boards"));
        Assert.IsNull(ContentLines.Tidy("gaming board"));
    }

    [TestMethod]
    public void Parse_LinesUnderAnExcludedLineEndingInAColon_AreDropped()
    {
        var lines = ContentLines.Parse(
            "<p>- 5x Line Troopers</p>" +
            "<p>3x 12ml Citadel Colour paints – Colours for your first army, including:</p>" +
            "<p>- 1x Grime Grey</p><p>- 2x Rust Red</p>" +
            "<p>The four troopers can choose from these interchangeable parts:</p>" +
            "<p>• 8 heads</p><p>• 4 sets of grenades</p>" +
            "<p>Contents:</p><p>• 1 Citadel Starter Brush</p><p>• 1x Citadel Starter Clippers</p>" +
            "<p>• 1 construction guide</p><p>• 1x Hobby tools</p><p>• 2x Warlords</p>");

        CollectionAssert.AreEqual(new[] { "Line Troopers", "Warlords" }, lines.Select(l => l.Name).ToArray());
    }

    [TestMethod]
    public void Parse_TwoCountsJoinedByAnd_AreTwoLines()
    {
        var lines = ContentLines.Parse(
            "<p>– 10 Beast Lads and 1 Hog Rider non-player operatives</p><p>– 5 Greenskins und 2 Squigs</p>");

        CollectionAssert.AreEqual(
            new[] { new Line(10, "Beast Lads", true, false), new Line(1, "Hog Rider non-player operatives", true, false),
                new Line(5, "Greenskins", false, false), new Line(2, "Squigs", false, false) },
            lines.ToArray());
    }

    [TestMethod]
    public void Parse_WordsInTheDescription_NeitherDropNorSplitTheLine()
    {
        var lines = ContentLines.Parse(
            "<p>- 1x Captain – Guides the brothers with tools</p><p>- 1x Sergeant: leads the squad and 2 others</p>" +
            "<p>- 5x Troopers (with rifles and 2 grenades)</p>");

        CollectionAssert.AreEqual(
            new[] { new Line(1, "Captain", false, false), new Line(1, "Sergeant", false, false),
                new Line(5, "Troopers", false, false) },
            lines.ToArray());
    }

    [TestMethod]
    public void Parse_AndAfterAClosedParenthesisOrWithX_SplitsTheLine()
    {
        var lines = ContentLines.Parse("<p>- 2x Lads (heavy weapon) and 1 Boss</p><p>- 5 Greenskins and 2x Squigs</p>");

        CollectionAssert.AreEqual(
            new[] { "Lads:2", "Boss:1", "Greenskins:5", "Squigs:2" },
            lines.Select(l => $"{l.Name}:{l.Count}").ToArray());
    }

    [TestMethod]
    public void Parse_IndentedBullets_StayBulletsUnderTheirTotal()
    {
        var lines = ContentLines.Parse("<p>3x Iron Brothers</p><p>  - 1x Captain</p><p>  - 2x Line Troopers</p>");

        CollectionAssert.AreEqual(new[] { "Captain", "Line Troopers" }, lines.Select(l => l.Name).ToArray());
    }

    [TestMethod]
    public void Parse_AColonInsideATitle_IsNoDescription()
    {
        var lines = ContentLines.Parse(
            "<p>• 1x Warhammer 40,000: Battle Report Book</p><p>• 1x Kill Team: Murderwing Transfer Sheet</p>" +
            "<p>• 2x Warlords</p>");

        CollectionAssert.AreEqual(new[] { "Warlords" }, lines.Select(l => l.Name).ToArray());
    }

    [TestMethod]
    public void Parse_NonPlayerMarker_IsFlagged()
    {
        var lines = ContentLines.Parse(
            "<li>10x Beast Lads Nichtspieler-Operatives (Miniaturen)</li><li>5x Hog Riders non-player operatives</li>");

        Assert.IsTrue(lines.All(l => l.NonPlayer));
    }

    [TestMethod]
    public void Parse_NonAsciiDigits_AreNoCount()
    {
        var lines = ContentLines.Parse("<li>- " + char.ConvertFromUtf32(0x663) + "x Seer</li>");

        Assert.AreEqual(0, lines.Count);
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

    [TestMethod]
    public void Parse_KindOfKitOnly_IsNoModel()
    {
        Assert.AreEqual(0, ContentLines.Parse("<p>1x multi-option, plastic Citadel Miniature.</p>").Count);
        Assert.AreEqual(1, ContentLines.Parse("<p>10x multipart plastic Iron Brothers</p>").Count);
    }

    [TestMethod]
    public void Parse_PartsAndHeads_AreDropped()
    {
        Assert.AreEqual(0, ContentLines.Parse("<ul><li>24x Köpfe</li><li>16x shoulder pads</li><li>70 x Kunststoffteile</li></ul>").Count);
    }

    [TestMethod]
    public void Parse_HandWeaponsAndGear_AreDropped()
    {
        var lines = ContentLines.Parse(
            "<ul><li>2x heavy bolters</li><li>1x chainsword</li><li>8x Boltguns</li><li>8x Bolt pistols</li><li>6x Backpacks</li>" +
            "<li>5x combi-weapons</li><li>2x power fists</li><li>1x reaper chaincannon</li><li>1x shovel</li><li>1x flamer</li><li>5x Rust Terminators</li></ul>");

        CollectionAssert.AreEqual(new[] { "Rust Terminators" }, lines.Select(l => l.Name).ToArray());
    }

    [TestMethod]
    public void Parse_ModelsNamedLikeWeaponsOrFillers_AreKept()
    {
        var lines = ContentLines.Parse(
            "<ul><li>1x Captain with Thunder Hammer</li><li>1x Rust Gunner w/ Meltagun</li><li>3x Flamers of Rust</li><li>1x Grand Cannon</li>" +
            "<li>3x Plague Drones of Rust</li><li>1x Captain in Gravis Armour</li><li>1x Lord on Rust Steed</li></ul>");

        CollectionAssert.AreEqual(
            new[] { "Captain with Thunder Hammer", "Rust Gunner w/ Meltagun", "Flamers of Rust", "Grand Cannon", "Plague Drones of Rust", "Captain in Gravis Armour", "Lord on Rust Steed" },
            lines.Select(l => l.Name).ToArray());
    }

    [TestMethod]
    public void Parse_FillerNames_AreDropped()
    {
        var lines = ContentLines.Parse(
            "<ul><li>1x model</li><li>5x models</li><li>1x of the following miniatures</li><li>1x of the squad into</li>" +
            "<li>10x Easy</li><li>1x mounted</li><li>1x atop</li><li>3x crew</li><li>2x additional crew</li><li>2x Sergeants</li>" +
            "<li>2x Drones</li><li>1x can be assembled</li><li>1x of Rust</li><li>2x double-sided Killzone tiles</li><li>5x Line Troopers</li></ul>");

        CollectionAssert.AreEqual(new[] { "Line Troopers" }, lines.Select(l => l.Name).ToArray());
    }

    [TestMethod]
    public void Parse_AdditionsToAName_AreCut()
    {
        var lines = ContentLines.Parse(
            "<ul><li>1x Blade Warden model</li><li>10x Rust Wardens in</li><li>3x Hammerers wielding meteoric hammers</li>" +
            "<li>1x Lord-Seer with 1x Rust-crow</li><li>1x Autarch equipped</li><li>1x sinister Gaunt Seer</li>" +
            "<li>10x mounted Rust Knights</li><li>1x unit of Dark Reapers</li><li>5x Rust Guard in several different poses</li>" +
            "<li>3x Praetors wearing thunder armour</li><li>1x Archon clad in rust armour inscribed</li><li>3x Rust Wasps accompanied by</li>" +
            "<li>5x metal Rust Yeomen miniatures mounted</li><li>1x blade and cleaver for the Champion</li></ul>");

        CollectionAssert.AreEqual(
            new[] { "Blade Warden", "Rust Wardens", "Hammerers", "Lord-Seer", "Autarch", "Gaunt Seer", "Rust Knights", "Dark Reapers",
                "Rust Guard", "Praetors", "Archon", "Rust Wasps", "Rust Yeomen", "blade and cleaver for the Champion" },
            lines.Select(l => l.Name).ToArray());
    }
}
