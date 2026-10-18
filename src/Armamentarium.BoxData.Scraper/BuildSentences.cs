using System.Text.RegularExpressions;

namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// Reads the model count a single kit's prose states: "This kit builds a squad of 10 …", "builds six …", "builds a …",
/// "kannst du 10 … bauen", "den Bau von 10 …" (ADR 0003 addendum ARMAM-599). Only a count tied to a build verb is read, a
/// number word or an article right before the name included; parts, bases, heads and transfers never count. Models added
/// with "as well as three …" or "sowie einen …" are additions of the build; an alternative build is never read. Which
/// sentences count for a source decides <see cref="Agreement"/>.
/// </summary>
public static partial class BuildSentences
{
    private static readonly Dictionary<string, int> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13,
        ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
        ["twenty"] = 20, ["ein"] = 1, ["eine"] = 1, ["einen"] = 1, ["eins"] = 1, ["zwei"] = 2, ["drei"] = 3, ["vier"] = 4,
        ["fünf"] = 5, ["sechs"] = 6, ["sieben"] = 7, ["acht"] = 8, ["neun"] = 9, ["zehn"] = 10, ["elf"] = 11,
        ["zwölf"] = 12, ["dreizehn"] = 13, ["vierzehn"] = 14, ["fünfzehn"] = 15, ["sechzehn"] = 16, ["siebzehn"] = 17,
        ["achtzehn"] = 18, ["neunzehn"] = 19, ["zwanzig"] = 20,
    };

    public static IReadOnlyList<BuildSentence> Parse(string html)
    {
        var sentences = new List<BuildSentence>();
        foreach (var sentence in SentenceBreak().Split(ContentLines.PlainText(html)))
        {
            // The first build of the sentence whose name is a model. A start inside the parts ("70 Teile, aus denen du zehn
            // … bauen") is tried again further on, so the parts never hide the build behind them.
            var build = Build().Match(sentence);
            while (build.Success && Read(build) is null)
            {
                build = Build().Match(sentence, build.Index + 1);
            }

            // "Alternatively, you can build a …" names a second way to build the same kit, not more models.
            if (!build.Success || AlternativeStart().IsMatch(sentence[..build.Index]))
            {
                continue;
            }

            // An alternative ends the additions ("or two units of 10"); an "or" between two weapons does not.
            var rest = sentence[(build.Index + build.Length)..];
            var alternative = AlternativeStart().Match(rest);
            var additions = Extra().Matches(alternative.Success ? rest[..alternative.Index] : rest)
                .Select(Read)
                .OfType<Line>()
                .ToList();

            // The same sentence twice (a summary and the description) is one finding.
            var found = new BuildSentence(Read(build)!, Articles.Contains(build.Groups["count"].Value), additions);
            if (!sentences.Any(s => s.Build.Count == found.Build.Count
                && ContentLines.Normalize(s.Build.Name) == ContentLines.Normalize(found.Build.Name)))
            {
                sentences.Add(found);
            }
        }

        return sentences;
    }

    // An article counts one, but prose builds "a unique boarding party" as readily as "a Leman Russ"; "one" is a number.
    private static readonly HashSet<string> Articles = new(ArticleWords.Split('|'), StringComparer.OrdinalIgnoreCase);

    private static Line? Read(Match match)
    {
        var name = Text.Clean(match.Groups["name"].Value);
        var raw = match.Groups["count"].Value;
        var count = Words.TryGetValue(raw, out var word) ? word : int.Parse(raw);
        return !ContentLines.IsModelName(name) || ContentLines.IsExcluded(name)
            ? null
            : new Line(count, name, ContentLines.IsNonPlayer(name), false);
    }

    private const string NumberWords =
        "one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|" +
        "eighteen|nineteen|twenty|eins|zwei|drei|vier|fünf|sechs|sieben|acht|neun|zehn|elf|zwölf|dreizehn|vierzehn|" +
        "fünfzehn|sechzehn|siebzehn|achtzehn|neunzehn|zwanzig";

    private const string ArticleWords = "a|an|ein|eine|einen";

    private const string Number = @"(?<![\p{L}\p{N}])(?<count>[0-9]{1,3}(?![0-9])|" + NumberWords + "|" + ArticleWords + ")";

    // Words that end a name: the next clause ("with …", "which …"), a further count, the German verb.
    private const string Stop =
        "(?:with|which|that|from|for|or|and|plus|as|along|using|including|to|on|each|armed|mit|und|sowie|oder|aus|" +
        "für|bauen|baust|kannst|du|als|" + NumberWords + "|" + ArticleWords + ")";

    // A name of letters, taken whole: followed by a count it was an adjective ("a whopping 20 …" names no model called
    // "whopping"), and no shorter part of it is tried instead.
    private const string Name =
        @"(?<name>(?>(?!" + Stop + @"\b)\p{L}[\p{L}'’-]*(?:\s+(?!" + Stop + @"\b)\p{L}[\p{L}'’-]*)*))(?!\s*[0-9])";

    [GeneratedRegex(
        @"\b(?:builds?|assembles?|makes?|construct)\s+(?:(?:a|an)\s+(?:squad|set|unit|team|kit|group)\s+of\s+)?" + Number + @"\s+" + Name +
        @"|\bBau\s+von\s+" + Number + @"\s+" + Name +
        @"|(?:(?:einen|eine|ein)\s+(?:Trupp|Einheit|Satz)\s+(?:aus|von)\s+)?" + Number + @"\s+" + Name + @"\s+bauen\b",
        RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex Build();

    [GeneratedRegex(@"\b(?:and|plus|as\s+well\s+as|along\s+with|und|sowie)\s+" + Number + @"\s+" + Name, RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex Extra();

    // "or" starts an alternative build only before a number ("or two units of 10"), not between two weapons ("a gauss
    // flayer or a gauss reaper").
    [GeneratedRegex(@"\b(?:or|oder)\s+(?:[0-9]{1,3}|" + NumberWords + @")\b|\balternativ\w*|can be built as", RegexOptions.IgnoreCase, Page.RegexTimeout)]
    private static partial Regex AlternativeStart();

    [GeneratedRegex(@"(?<=[.!?;])\s+|\n", RegexOptions.None, Page.RegexTimeout)]
    private static partial Regex SentenceBreak();
}
