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
        var text = InlineSeparator().Replace(PlainText(html), "\n");

        var lines = new List<(Line Line, bool Bullet)>();
        var underExcludedHeading = false;
        foreach (var raw in text.Split('\n'))
        {
            var match = LineShape().Match(raw);
            var bullet = match.Success && match.Groups["bullet"].Success;
            var colon = raw.TrimEnd().EndsWith(':');

            // "1 Ciaphas Cain miniature" names its model without bullet or x (ADR 0003 Addendum ARMAM-642).
            var miniature = match.Success && MiniatureEnd().IsMatch(match.Groups["name"].Value);
            if (!match.Success || (!bullet && !match.Groups["x"].Success && !miniature))
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

            // "Captain: Leads the brothers" and "Captain – Guides the brothers" end the name at the shop's description,
            // so neither an excluded word nor an "and 2" in the description reaches the checks below.
            var nonPlayer = NonPlayer().IsMatch(match.Groups["name"].Value);
            var name = WithoutDescription(match.Groups["name"].Value);
            if (Excluded().IsMatch(name))
            {
                continue;
            }

            // "10 Termagants and 1 Ripper Swarm non-player operatives" is two lines; the marker belongs to both.
            var count = int.Parse(match.Groups["count"].Value);
            var and = NextAnd(name);
            while (true)
            {
                var part = and.Success ? name[..and.Index] : name;
                var alternative = AlternativeBuild().IsMatch(part);
                if (Tidy(Text.Clean(Cut(part, alternative))) is { } cut)
                {
                    lines.Add((new Line(count, cut, nonPlayer, alternative), bullet));
                }

                if (!and.Success)
                {
                    break;
                }

                count = int.Parse(and.Groups["count"].Value);
                name = name[(and.Index + and.Length)..];
                and = NextAnd(name);
            }
        }

        return lines.Where((l, i) => !IsGroupTotal(lines, i)).Select(l => l.Line).ToList();
    }

    /// <summary>The description as text: block tags become line breaks, entities are decoded.</summary>
    internal static string PlainText(string html) =>
        WebUtility.HtmlDecode(AnyTag().Replace(BlockTag().Replace(html, "\n"), string.Empty)).Replace('\u00A0', ' ');

    /// <summary>
    /// A read name as a model name, or null when it names none (ADR 0003 Addendum ARMAM-610): prose around the name is cut
    /// ("sinister Gaunt Summoner", "Blade Champion model", "Annihilators wielding …", "Lord-Veritant with 1x Gryph-crow"),
    /// and what is left must be a model, not a part, a weapon or a filler word. The build sentences and the repair of
    /// stored names use it too.
    /// </summary>
    internal static string? Tidy(string name)
    {
        // "of the following miniatures" is the rest of a sentence; "Lord of Change" has its "of" inside.
        if (OfStart().IsMatch(name))
        {
            return null;
        }

        name = Addition().Replace(Prose().Replace(name, string.Empty), string.Empty).Trim();

        // "1x multi-option, plastic Citadel Miniature" cut at its comma names a kind of kit, not a model.
        return Normalize(name).Length == 0 || DescriptionOnly().IsMatch(name) || Excluded().IsMatch(name)
            || FillerOnly().IsMatch(name) || Gear().IsMatch(name)
            ? null
            : name;
    }

    internal static bool IsNonPlayer(string name) => NonPlayer().IsMatch(name);

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

    private static string WithoutDescription(string name)
    {
        var dash = DescriptionDash().Match(name);
        if (dash.Success)
        {
            name = name[..dash.Index];
        }

        // A colon in a title ("Warhammer 40,000: Battle Report Book") keeps its few words; a description runs longer.
        var colon = name.IndexOf(':');
        var described = colon >= 0 && name[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 4;
        return described ? name[..colon] : name;
    }

    // An "and 2" inside a parenthesis ("with rifles and 2 grenades") is prose, not a second line.
    private static Match NextAnd(string name)
    {
        var and = AndCount().Match(name);
        if (!and.Success)
        {
            return and;
        }

        var before = name[..and.Index];
        return before.Count(c => c == '(') == before.Count(c => c == ')') ? and : Match.Empty;
    }

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

    // "Inhalt: 1x Seer - 5x Guards": the colon and every dash before a further count start a new line. A dash at the start
    // of a line is its bullet, not a separator.
    [GeneratedRegex(@"(?:(?<=\S)[ \t][-–—•][ \t]|:[ \t])(?=[0-9]{1,3}\s?[x×]\s)", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex InlineSeparator();

    [GeneratedRegex(@"^\s*(?:(?<bullet>[-–—•*·])\s*)?(?<count>[0-9]{1,3})\s*(?<x>[x×](?=\s))?\s+(?<name>\S.*?)\s*$", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex LineShape();

    [GeneratedRegex(@"^\s*[0-9]{1,3}\s+\S", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\bminiatures?\s*$", RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex MiniatureEnd();

    [GeneratedRegex(
        @"\b(terrain|scenery|gel(ä|ae|a)nde\w*|data ?cards?|\w*cards?|\w*karten|dice|d6|w(ü|ue)rfel|tokens?|\w*bogen|\w*bögen|" +
        @"markers?|mats?|boards?|tiles?|spielfeld|\w*books?|\w*buch|dossier|rules?|regel\w*|rulers?|transfers?|decals?|sheets?|" +
        @"bases?|frames?|sprues?|components?|parts|\w*teile|heads?|k(ö|oe)pfe|shoulder ?pads?|schulter\w*|reference|" +
        @"referenz\w*|poster|citadel miniatures|" +
        @"miniatures total|seiten|pages?|paints?|farben|brush(es)?|pinsel|clippers?|tools?|werkzeug\w*|guides?|" +
        @"anleitung\w*|handbook|handbuch)\b",
        RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex Excluded();

    [GeneratedRegex(@"^(?:\s|multi-?options?|multi-?part|push-?fit|plastic|kunststoff)*$", RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex DescriptionOnly();

    [GeneratedRegex(@"^of\b", RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex OfStart();

    // Up to three lower-case words before the first capital are the shop's prose: "mounted Chaos Knights", "unit of Dark
    // Reapers", "varying towers of Nurglings". A longer run is a part named for its bearer ("blade and cleaver for the Champion").
    [GeneratedRegex(@"^(?:\p{Ll}[\p{L}'’-]*\s+){1,3}(?=\p{Lu})", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex Prose();

    // What follows a model's name and says how it comes: "… wielding meteoric hammers", "… with 1x Gryph-crow", "… in several
    // different poses", "… accompanied by", "… usable by …", and trailing words such as "… miniatures mounted" or a dangling "in".
    [GeneratedRegex(
        @"(?:\s+(?:(?:wielding|wearing|clad|equipped|armed|inscribed|accompanied|usable)\b.*|with\s+[0-9]{1,3}\s?[x×]\s.*|" +
        @"in\s+several\b.*)|(?:\s+(?:in|models?|miniatures?|mounted))+)$",
        RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex Addition();

    // A name of nothing but filler: rests of a sentence and the crew or sergeants a unit carries (plural only: a Kill Team
    // lists a single "Sergeant" as its operative).
    [GeneratedRegex(
        @"^(?:\s|\b(?:of|the|following|squad|into|models?|miniatures?|easy|mounted|atop|riding|huge|sinister|unhooded|can|be|" +
        @"assembled|additional|crew|drones|sergeants|commanders|plastic|metal)\b)*$",
        RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex FillerOnly();

    // A name that ends in a hand weapon or a piece of kit is a part; "Captain with Thunder Hammer" and "Gunner w/ Meltagun" name their bearer, and guns
    // that are models ("Grand Cannon", "Organ Gun", "Flamers of Tzeentch") are not in the list.
    [GeneratedRegex(
        @"^(?!.*\b(?:with|w)\b).*\b(?:chain ?swords?|chain ?axes?|power (?:swords?|axes?|mauls?|fists?|lances?)|thunder hammers?|" +
        @"lightning claws?|chainfists?|meltaguns?|plasma (?:guns?|pistols?)|boltguns?|bolters?|bolt pistols?|missile launchers?|" +
        @"lascannons?|autocannons?|flamers?|combi-\w+|reaper chaincannons?|shotguns?|backpacks?|helmets?|ropes?|" +
        @"crates?|shovels?|bayonets?)$",
        RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex Gear();

    [GeneratedRegex(@"alternativ|can be built as|\bor\b|\boder\b", RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex AlternativeBuild();

    [GeneratedRegex(@"\s(and|und)\s(?<count>[0-9]{1,3})\s?[x×]?\s+(?=\S)", RegexOptions.IgnoreCase, Page.RegexTimeout)]
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
