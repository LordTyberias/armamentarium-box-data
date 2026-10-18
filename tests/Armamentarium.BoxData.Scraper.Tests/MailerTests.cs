namespace Armamentarium.BoxData.Scraper.Tests;

[TestClass]
public sealed class MailerTests
{
    [TestMethod]
    public void FailureMessage_NamesWhatSystemdHandsOver()
    {
        var env = new Dictionary<string, string>
        {
            ["MONITOR_UNIT"] = "armamentarium-box-data.service",
            ["MONITOR_SERVICE_RESULT"] = "timeout",
            ["MONITOR_EXIT_CODE"] = "killed",
            ["MONITOR_EXIT_STATUS"] = "TERM",
            ["MONITOR_INVOCATION_ID"] = "0123abcd",
        };

        var (subject, body) = Mailer.FailureMessage(env.GetValueOrDefault);

        Assert.AreEqual("Armamentarium box data: run failed (timeout)", subject);
        StringAssert.Contains(body, "Unit: armamentarium-box-data.service");
        StringAssert.Contains(body, "Exit: killed TERM");
        StringAssert.Contains(body, "journalctl _SYSTEMD_INVOCATION_ID=0123abcd --no-pager");
    }

    [TestMethod]
    public void FailureMessage_WithoutSystemd_SaysItWasStartedByHand()
    {
        var (subject, body) = Mailer.FailureMessage(_ => null);

        Assert.AreEqual("Armamentarium box data: notification started by hand", subject);
        StringAssert.Contains(body, "journalctl -u armamentarium-box-data -n 100 --no-pager");
    }

    [TestMethod]
    public void FailureMessage_CleansWhatItQuotes()
    {
        var (subject, _) = Mailer.FailureMessage(name => name == "MONITOR_SERVICE_RESULT" ? "exit-code\r\nBcc: x@y.test" : null);

        Assert.AreEqual("Armamentarium box data: run failed (exit-code Bcc: x@y.test)", subject);
    }

    [TestMethod]
    public void FindingsMessage_CountsAndListsThem()
    {
        var (subject, body) = Mailer.FindingsMessage(["scraper/1 not merged: answered 405", "kt: catalogue unreadable"]);

        Assert.AreEqual("Armamentarium box data: 2 finding(s) in the run", subject);
        StringAssert.Contains(body, "- scraper/1 not merged: answered 405");
        StringAssert.Contains(body, "- kt: catalogue unreadable");
    }
}
