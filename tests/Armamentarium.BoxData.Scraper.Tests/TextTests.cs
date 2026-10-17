namespace Armamentarium.BoxData.Scraper.Tests;

[TestClass]
public sealed class TextTests
{
    [TestMethod]
    public void Clean_DropsControlAndFormatCharactersAndFoldsWhitespace()
    {
        Assert.AreEqual("Kill Team: Exo", Text.Clean("  Kill\tTeam:\r\n Exo\u202e\u200b "));
    }

    [TestMethod]
    public void Clean_CapsTheLength()
    {
        Assert.AreEqual(Text.MaxLength, Text.Clean(new string('a', 500)).Length);
    }

    [TestMethod]
    public void Clean_DropsInvisibleCharactersOutsideTheBasicPlane()
    {
        Assert.AreEqual("ab", Text.Clean("a" + char.ConvertFromUtf32(0xE0041) + "b"));
    }

    [TestMethod]
    public void Clean_NeverCutsACharacterInHalf()
    {
        var cleaned = Text.Clean(new string('a', Text.MaxLength - 1) + char.ConvertFromUtf32(0x1F600));

        Assert.AreEqual(Text.MaxLength - 1, cleaned.Length);
    }

    [TestMethod]
    public void Code_WrapsInACodeSpanWithoutBackticksOrPipes()
    {
        Assert.AreEqual("`a'b/c @d`", Text.Code("a`b|c @d"));
    }
}
