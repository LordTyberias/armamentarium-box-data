using System.Net;
using System.Net.Mail;
using System.Text;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// The service's mail to the maintainer (ADR 0003 Addendum ARMAM-600): a failed run, through the unit
/// armamentarium-box-data-failure.service that systemd starts on failure, and the notices of a run at its end. It sends
/// through the mailbox the Armamentarium web services already use; the password is the one systemd credential they share,
/// everything else is fixed here. Nothing from the journal goes into a mail.
/// </summary>
public static class Mailer
{
    public const string Host = "smtp.ionos.de";
    public const int Port = 587;
    public const string User = "support@armamentarium.app";
    public const string From = "boxdata@armamentarium.app";
    public const string To = "info@strecker-it.net";

    private const string Prefix = "Armamentarium box data: ";
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The failure mail from what systemd hands an OnFailure= unit (MONITOR_*). systemd sets those on every start, also by
    /// hand, from the last state of the scraper unit (measured 2026-10-18): a result of <c>success</c> or none means the
    /// unit was started by hand, the check of ADR 0003 Addendum ARMAM-600; a failure never ends in <c>success</c>.
    /// </summary>
    public static (string Subject, string Body) FailureMessage(Func<string, string?> environment)
    {
        string? Value(string name) => environment(name) is { Length: > 0 } value ? Text.Clean(value) : null;
        var result = Value("MONITOR_SERVICE_RESULT");
        var invocation = Value("MONITOR_INVOCATION_ID");
        var byHand = result is null or "success";
        var body = new StringBuilder();
        if (byHand)
        {
            body.AppendLine("This notification was started by hand, not by a failed run: the mail way works.")
                .AppendLine($"Last result of the scraper unit: {result ?? "unknown"}");
        }
        else
        {
            body.AppendLine("A run of the box contents service failed.")
                .AppendLine()
                .AppendLine($"Unit: {Value("MONITOR_UNIT") ?? "unknown"}")
                .AppendLine($"Result: {result}")
                .AppendLine($"Exit: {Value("MONITOR_EXIT_CODE") ?? "-"} {Value("MONITOR_EXIT_STATUS") ?? "-"}");
        }

        body.AppendLine()
            .AppendLine("On the server:");
        if (invocation is not null)
        {
            body.AppendLine($"  journalctl _SYSTEMD_INVOCATION_ID={invocation} --no-pager");
        }

        body.AppendLine("  journalctl -u armamentarium-box-data -n 100 --no-pager");
        return (byHand ? Prefix + "notification started by hand" : $"{Prefix}run failed ({result})", body.ToString());
    }

    /// <summary>One mail per run for its notices (Runner, CatalogueIndex.Unavailable).</summary>
    public static (string Subject, string Body) FindingsMessage(IReadOnlyList<string> findings)
    {
        var body = new StringBuilder()
            .AppendLine("The last run of the box contents service has notices; the journal holds the whole run.")
            .AppendLine();
        foreach (var finding in findings)
        {
            body.AppendLine($"- {finding}");
        }

        body.AppendLine()
            .AppendLine("On the server: journalctl -u armamentarium-box-data -n 200 --no-pager");
        return ($"{Prefix}{findings.Count} finding(s) in the run", body.ToString());
    }

    /// <summary>STARTTLS on 587 (EnableSsl): a server that does not offer it fails the send, nothing goes in clear text.</summary>
    public static async Task SendAsync((string Subject, string Body) message, string password, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendTimeout);
        using var client = new SmtpClient(Host, Port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = new NetworkCredential(User, password),
        };
        using var mail = new MailMessage(From, To, message.Subject, message.Body)
        {
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
        };
        await client.SendMailAsync(mail, timeout.Token);
    }
}
