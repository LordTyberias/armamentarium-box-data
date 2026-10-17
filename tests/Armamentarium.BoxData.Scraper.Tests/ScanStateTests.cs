namespace Armamentarium.BoxData.Scraper.Tests;

[TestClass]
public sealed class ScanStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 3, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void IsDue_UnknownCode_IsDue()
    {
        Assert.IsTrue(new ScanState().IsDue("60010199001", Now));
    }

    [TestMethod]
    public void IsDue_CheckedWithinThirtyDays_IsNotDue()
    {
        var state = new ScanState();
        state.Mark("60010199001", Now);

        Assert.IsFalse(state.IsDue("60010199001", Now.AddDays(29)));
        Assert.IsTrue(state.IsDue("60010199001", Now.AddDays(30)));
    }

    [TestMethod]
    public void Retry_IsDueAgainAfterThreeDays()
    {
        var state = new ScanState();
        state.Retry("60010199001", Now);

        Assert.IsFalse(state.IsDue("60010199001", Now.AddDays(2)));
        Assert.IsTrue(state.IsDue("60010199001", Now.AddDays(3)));
    }

    [TestMethod]
    public void SaveAndLoad_KeepTheMarks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scan-state-{Guid.NewGuid():N}.json");
        try
        {
            var state = new ScanState();
            state.Mark("60010199001", Now);
            state.Save(path);

            Assert.IsFalse(ScanState.Load(path).IsDue("60010199001", Now.AddDays(1)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Load_MissingFile_IsEmpty()
    {
        Assert.IsTrue(ScanState.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")).IsDue("1", Now));
    }

    [TestMethod]
    public void Load_CorruptFile_NamesTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scan-state-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not json");

            var error = Assert.ThrowsExactly<InvalidDataException>(() => ScanState.Load(path));

            StringAssert.Contains(error.Message, path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
