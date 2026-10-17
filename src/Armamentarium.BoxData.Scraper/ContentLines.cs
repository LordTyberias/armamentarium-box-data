using System.Net;
using System.Text.RegularExpressions;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// Reads "N x Name" lines out of a shop description. Keeps a count and a model name, nothing else (ADR 0003 §8):
/// prose, totals and everything that is not a model are dropped.
/// </summary>
public static partial class ContentLines
{
    public static IReadOnlyList<Line> Parse(string html)
    {
        var text = BlockTag().Replace(html, "\n");
        text = WebUtility.HtmlDecode(AnyTag().Replace(text, string.Empty)).Replace('\u00A0', ' ');
        text = InlineSeparator().Replace(text, "\n");

        var lines = new List<Line>();
        var underExcludedHeading = false;
        foreach (var raw in text.Split('\n'))
        {
            var match = LineShape().Match(raw);
            if (!match.Success || (!match.Groups["bullet"].Success && !match.Groups["x"].Success))
            {
                // "15 plastic terrain pieces" heads the lines below it; a heading without an excluded word ends that run.
                if (Heading().IsMatch(raw))
                {
                    underExcludedHeading = Excluded().IsMatch(raw);
                }

                continue;
            }

            if (underExcludedHeading)
            {
                continue;
            }

            var name = match.Groups["name"].Value;
            if (Excluded().IsMatch(name))
            {
                continue;
            }

            var alternative = AlternativeBuild().IsMatch(name);
            var cut = Text.Clean(Cut(name, alternative));
            if (cut.Length == 0 || Normalize(cut).Length == 0)
            {
                continue;
            }

            lines.Add(new Line(int.Parse(match.Groups["count"].Value), cut, NonPlayer().IsMatch(name), alternative));
        }

        return lines;
    }

    /// <summary>The comparison key of a model name: lower case, without operative words, plural s and punctuation.</summary>
    public static string Normalize(string name)
    {
        var text = Parenthesis().Replace(name.ToLowerInvariant(), " ").Replace('-', ' ');
        text = Filler().Replace(text, " ");
        text = NonLetter().Replace(text, " ");
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 3 && w.EndsWith('s') ? w[..^1] : w);
        return string.Join(' ', words);
    }

    /// <summary>The name shown in a pull request: the shop's name without operative words.</summary>
    public static string DisplayName(string name) =>
        Spaces().Replace(DisplayFiller().Replace(name, " "), " ").Trim();

    private static string Cut(string name, bool alternative)
    {
        var end = name.Length;
        foreach (var stop in new[] { name.IndexOf('('), name.IndexOf(',') })
        {
            if (stop >= 0 && stop < end)
            {
                end = stop;
            }
        }

        if (alternative)
        {
            var or = OrWord().Match(name);
            if (or.Success && or.Index < end)
            {
                end = or.Index;
            }
        }

        return name[..end].Trim().TrimEnd('.', ':', ';', '-', '–').Trim();
    }

    [GeneratedRegex(@"<\s*/?\s*(br|li|p|div|ul|ol|tr|td|h[1-6])\b[^>]*>", RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex BlockTag();

    [GeneratedRegex("<[^>]*>", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex AnyTag();

    // "Inhalt: 1x Seer - 5x Guards": the colon and every dash before a further count start a new line.
    [GeneratedRegex(@"(?:\s[-–—•]\s|:\s)(?=[0-9]{1,3}\s?[x×]\s)", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex InlineSeparator();

    [GeneratedRegex(@"^\s*(?:(?<bullet>[-–—•*·])\s*)?(?<count>[0-9]{1,3})\s*(?<x>[x×](?=\s))?\s+(?<name>\S.*?)\s*$", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex LineShape();

    [GeneratedRegex(@"^\s*[0-9]{1,3}\s+\S", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex Heading();

    [GeneratedRegex(
        @"\b(terrain|scenery|gel(ä|ae|a)nde\w*|data ?cards?|\w*cards?|\w*karten|dice|d6|w(ü|ue)rfel|tokens?|\w*bogen|\w*bögen|" +
        @"markers?|mats?|board|spielfeld|\w*books?|\w*buch|dossier|rules?|regel\w*|rulers?|transfers?|decals?|sheets?|" +
        @"bases?|frames?|sprues?|components?|parts|bauteile|reference|referenz\w*|poster|citadel miniatures|" +
        @"miniatures total|seiten|pages?)\b",
        RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex Excluded();

    [GeneratedRegex(@"alternativ|can be built as|\bor\b|\boder\b", RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex AlternativeBuild();

    [GeneratedRegex(@"\s(or|oder)\s", RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex OrWord();

    [GeneratedRegex(@"non-?player|nichtspieler|\bNPOs?\b", RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex NonPlayer();

    [GeneratedRegex(@"\([^)]*\)?", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex Parenthesis();

    [GeneratedRegex(
        @"\b(kill team|non player|nichtspieler|operatives?|miniatures?|miniatur(en)?|npos?|citadel)\b", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex Filler();

    [GeneratedRegex(
        @"\b(kill team operatives?|non-?player operatives?|nichtspieler-operatives?|operatives?)\b",
        RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex DisplayFiller();

    [GeneratedRegex(@"[^\p{L}\p{N} ]", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex NonLetter();

    [GeneratedRegex(@"\s+", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex Spaces();
}
