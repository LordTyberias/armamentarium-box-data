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

        var lines = new List<(Line Line, bool Bullet)>();
        var underExcludedHeading = false;
        foreach (var raw in text.Split('\n'))
        {
            var match = LineShape().Match(raw);
            var bullet = match.Success && match.Groups["bullet"].Success;
            var colon = raw.TrimEnd().EndsWith(':');
            if (!match.Success || (!bullet && !match.Groups["x"].Success))
            {
                // "15 plastic terrain pieces" or "these interchangeable parts:" heads the lines below it; a heading
                // without an excluded word ends that run.
                if (Heading().IsMatch(raw) || colon)
                {
                    underExcludedHeading = Excluded().IsMatch(raw);
                }

                continue;
            }

            // A counted line without a bullet heads the bulleted lines below it: "5x Citadel Colour paints, including:"
            // drops the paints listed under it.
            if (!bullet)
            {
                underExcludedHeading = colon && Excluded().IsMatch(raw);
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

            // "10 Termagants and 1 Ripper Swarm non-player operatives" is two lines; the marker belongs to both.
            var nonPlayer = NonPlayer().IsMatch(name);
            var count = int.Parse(match.Groups["count"].Value);
            var and = AndCount().Match(name);
            while (true)
            {
                var part = and.Success ? name[..and.Index] : name;
                var alternative = AlternativeBuild().IsMatch(part);
                var cut = Text.Clean(Cut(part, alternative));
                if (cut.Length > 0 && Normalize(cut).Length > 0)
                {
                    lines.Add((new Line(count, cut, nonPlayer, alternative), bullet));
                }

                if (!and.Success)
                {
                    break;
                }

                count = int.Parse(and.Groups["count"].Value);
                name = name[(and.Index + and.Length)..];
                and = AndCount().Match(name);
            }
        }

        return lines.Where((l, i) => !IsGroupTotal(lines, i)).Select(l => l.Line).ToList();
    }

    // "11x Space Marines" over "- 1x Captain", "- 5x Terminators", "- 5x Infernus Marines" counts its members again.
    private static bool IsGroupTotal(List<(Line Line, bool Bullet)> lines, int index)
    {
        if (lines[index].Bullet)
        {
            return false;
        }

        var members = lines.Skip(index + 1).TakeWhile(l => l.Bullet).Sum(l => l.Line.Count);
        return members == lines[index].Line.Count;
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
        // "Captain: Leads the brothers" and "Captain – Leads the brothers" end the name at the description.
        var dash = DescriptionDash().Match(name);
        foreach (var stop in new[] { name.IndexOf('('), name.IndexOf(','), name.IndexOf(':'), dash.Success ? dash.Index : -1 })
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

    // "Inhalt: 1x Seer - 5x Guards": the colon and every dash before a further count start a new line. A dash at the start
    // of a line is its bullet, not a separator.
    [GeneratedRegex(@"(?:[ \t][-–—•][ \t]|:[ \t])(?=[0-9]{1,3}\s?[x×]\s)", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex InlineSeparator();

    [GeneratedRegex(@"^\s*(?:(?<bullet>[-–—•*·])\s*)?(?<count>[0-9]{1,3})\s*(?<x>[x×](?=\s))?\s+(?<name>\S.*?)\s*$", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex LineShape();

    [GeneratedRegex(@"^\s*[0-9]{1,3}\s+\S", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex Heading();

    [GeneratedRegex(
        @"\b(terrain|scenery|gel(ä|ae|a)nde\w*|data ?cards?|\w*cards?|\w*karten|dice|d6|w(ü|ue)rfel|tokens?|\w*bogen|\w*bögen|" +
        @"markers?|mats?|board|spielfeld|\w*books?|\w*buch|dossier|rules?|regel\w*|rulers?|transfers?|decals?|sheets?|" +
        @"bases?|frames?|sprues?|components?|parts|bauteile|reference|referenz\w*|poster|citadel miniatures|" +
        @"miniatures total|seiten|pages?|paints?|farben|brush(es)?|pinsel|clippers?|tools?|werkzeug\w*|guides?|" +
        @"anleitung\w*|handbook|handbuch)\b",
        RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex Excluded();

    [GeneratedRegex(@"alternativ|can be built as|\bor\b|\boder\b", RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex AlternativeBuild();

    [GeneratedRegex(@"\s(and|und)\s(?<count>[0-9]{1,3})\s+(?=\S)", RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex AndCount();

    [GeneratedRegex(@"\s[-–—]\s", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex DescriptionDash();

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
