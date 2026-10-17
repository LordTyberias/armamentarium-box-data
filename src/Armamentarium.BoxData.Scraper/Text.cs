using System.Globalization;
using System.Text;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// Shop and catalogue text on its way into the table, a commit or a pull request. Text from outside never carries a line
/// break into a commit message, a bidi or zero-width mark into the table, or Markdown into the review page.
/// </summary>
public static class Text
{
    public const int MaxLength = 120;

    /// <summary>One line: control and format characters dropped, whitespace folded to single spaces, at most MaxLength.</summary>
    public static string Clean(string text)
    {
        // Per Rune, not per char: invisible characters outside the basic plane (the tag block) come as surrogate pairs.
        // A lone surrogate arrives as the replacement character and goes too.
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                space = builder.Length > 0;
                continue;
            }

            if (rune == Rune.ReplacementChar || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control
                or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned)
            {
                continue;
            }

            var length = rune.Utf16SequenceLength + (space ? 1 : 0);
            if (builder.Length + length > MaxLength)
            {
                break;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    /// <summary>A Markdown code span: no link, image, mention or reference renders inside it, nor does it break a table row.</summary>
    public static string Code(string text) => $"`{Clean(text).Replace('`', '\'').Replace('|', '/')}`";
}

/// <summary>A shop that did not answer: a network failure, a timeout, 429 or a server error. Not a "not listed".</summary>
public sealed class FetchFailedException(string message) : Exception(message);
