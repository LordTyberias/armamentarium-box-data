namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// Turns the listings of one product into entries. Every entry needs two confirming sources; one short and the box
/// waits (ADR 0003 §9, Armamentarium ADR 0118 §2). Disagreeing counts become candidates.
/// </summary>
public static class Agreement
{
    /// <param name="listings">The shops' listings of the product.</param>
    /// <param name="rootOf">
    /// The root entry a name names in the product's readings, or null (ADR 0003 addendum ARMAM-599): a build sentence whose
    /// name names one wins over the source's lines, and two names of one root entry confirm each other.
    /// </param>
    /// <param name="isMember">
    /// Whether the catalogue lists a name as a model of the unit a name names (ADR 0003 Addendum ARMAM-688): only then do the
    /// members of a built unit count.
    /// </param>
    public static AgreementResult Agree(
        IReadOnlyList<Listing> listings, Func<string, string?>? rootOf = null, Func<string, string, bool>? isMember = null)
    {
        string Key(string name) => rootOf?.Invoke(name) ?? ContentLines.Normalize(name);

        // A member keeps its own key under its unit, as an alias does (ADR 0004 Addendum ARMAM-688).
        string KeyOf(Line line) => line.MemberOf is { } unit ? $"{Key(unit)}|member:{ContentLines.Normalize(line.Name)}" : Key(line.Name);

        var effective = new Dictionary<Listing, IReadOnlyList<Line>>(ReferenceEqualityComparer.Instance);
        foreach (var listing in listings)
        {
            effective.TryAdd(listing, Effective(listing, rootOf, isMember, KeyOf));
        }

        var described = listings.Where(l => !l.German && effective[l].Count > 0).ToList();
        var keys = new Dictionary<Listing, HashSet<string>>(ReferenceEqualityComparer.Instance);
        foreach (var listing in described)
        {
            keys[listing] = effective[listing].Select(KeyOf).ToHashSet();
        }

        // A source that shares no entry with another describes another thing, or the same one in its own way (a split
        // by equipment, "10 Kasrkin elite infantry"): it does not count for the box, rather than hold it (ADR 0003
        // addendum ARMAM-599). Only where no two English sources share an entry may a German one vouch for a single
        // English source; else it would join an unrelated description to the agreeing ones.
        var overlapping = described
            .Where(l => described.Any(o => !ReferenceEquals(o, l) && keys[o].Overlaps(keys[l])))
            .ToList();
        var candidates = overlapping.Count > 0 ? overlapping : described;

        // A translated listing names the models differently but keeps the publisher's order: it confirms an English
        // listing line by line only when both hold the same sequence of counts, its lines or its build sentences.
        var confirmations = new List<(Listing German, Listing English, IReadOnlyList<Line> Lines)>();
        foreach (var german in listings.Where(l => l.German))
        {
            var own = new[] { german.Lines, Effective(german with { Lines = [] }, null, null, KeyOf) }.Where(l => l.Count > 0).ToList();
            var match = candidates
                .Select(e => (English: e, German: own.FirstOrDefault(g =>
                    effective[e].Select(l => l.Count).SequenceEqual(g.Select(l => l.Count)))))
                .FirstOrDefault(m => m.German is not null);
            if (match.German is not null)
            {
                confirmations.Add((german, match.English, match.German));
            }
        }

        var english = overlapping.Count > 0
            ? overlapping
            : described.Where(l => confirmations.Any(c => ReferenceEquals(c.English, l))).ToList();

        var order = new List<string>();
        var display = new Dictionary<string, Line>();
        var counts = new Dictionary<string, Dictionary<string, int>>();
        var sources = new List<string>();

        void Count(string key, Listing listing, Line line)
        {
            if (!counts.TryGetValue(key, out var byHost))
            {
                order.Add(key);
                display[key] = line;
                counts[key] = byHost = [];
            }
            else
            {
                var first = display[key];
                display[key] = first with
                {
                    NonPlayer = first.NonPlayer || line.NonPlayer,
                    AlternativeBuild = first.AlternativeBuild || line.AlternativeBuild,
                };
            }

            byHost.TryAdd(listing.Host, line.Count);
            if (!sources.Contains(listing.Url))
            {
                sources.Add(listing.Url);
            }
        }

        // Two lines of one listing under one name ("2x Sergeant (bolt)", "1x Sergeant (plasma)") would leave a count neither
        // source states; the box waits instead.
        foreach (var listing in english)
        {
            if (effective[listing].GroupBy(KeyOf).FirstOrDefault(g => g.Count() > 1) is { } twice)
            {
                return new AgreementResult(
                    null, sources, $"'{ContentLines.DisplayName(twice.First().Name)}' appears twice in {listing.Host}");
            }
        }

        foreach (var listing in english)
        {
            foreach (var line in effective[listing])
            {
                Count(KeyOf(line), listing, line);
            }
        }

        foreach (var (german, match, lines) in confirmations.Where(c => english.Contains(c.English)))
        {
            var englishLines = effective[match];
            for (var i = 0; i < englishLines.Count; i++)
            {
                Count(KeyOf(englishLines[i]), german, lines[i] with
                {
                    NonPlayer = false,
                    AlternativeBuild = false,
                });
            }
        }

        if (order.Count == 0)
        {
            return new AgreementResult(
                null, sources, described.Count > 0 ? "no two sources share an entry" : "no source states a model count");
        }

        var entries = new List<ProposedEntry>();
        foreach (var key in order)
        {
            var name = ContentLines.DisplayName(display[key].Name);
            if (counts[key].Count < 2)
            {
                return new AgreementResult(null, sources, $"'{name}' is confirmed by {counts[key].Count} source only");
            }

            entries.Add(new ProposedEntry(
                name,
                display[key].NonPlayer,
                counts[key].Values.Distinct().Order().ToList(),
                counts[key].Keys.ToList(),
                display[key].AlternativeBuild,
                display[key].MemberOf));
        }

        return new AgreementResult(entries, sources, null);
    }

    // What a source counts: its build sentences when their main build names a root entry ("builds a squad of 10 Imperial
    // Navy Breachers" over the shop's split by equipment) or when it lists no lines; else its lines (ADR 0003 addendum
    // ARMAM-599).
    private static IReadOnlyList<Line> Effective(
        Listing listing, Func<string, string?>? rootOf, Func<string, string, bool>? isMember, Func<Line, string> key)
    {
        string? Root(Line line) => rootOf?.Invoke(line.Name);

        // "a Darkoath Chieftain on Warsteed" names the mounted hero when that name hits (ADR 0003 Addendum ARMAM-688).
        var sentences = listing.Sentences.Select(s => s.Longer is { } longer && Root(longer) is not null ? s with { Build = longer } : s).ToList();

        // "builds a … Squad" by article states no model count; the unit's members do, when the catalogue lists each of them
        // under it (ADR 0003 Addendum ARMAM-688).
        bool Unit(BuildSentence s) => s.Article && !s.Names && CatalogueIndex.IsUnitName(s.Build.Name);
        bool Members(BuildSentence s) => s.Members.Count > 0 && s.Members.All(m => isMember?.Invoke(s.Build.Name, m.Name) == true);

        // A count by article only with a hit: prose builds "a unique boarding party" as readily as "a Leman Russ". A build of
        // names only when every name hits (ADR 0003 Addendum ARMAM-642).
        // ponytail: a hit does not tell a model from a unit ("builds Hellblasters and Eradicators" would count one each);
        // two agreeing sources are the guard, a check of the entry's type if such a box shows up.
        var usable = sentences.Where(s => s.Names
            ? Root(s.Build) is not null && s.Additions.All(a => Root(a.Build) is not null)
            : (!s.Article || Root(s.Build) is not null) && (!Unit(s) || Members(s))).ToList();
        if (usable.Count == 0)
        {
            return listing.Lines;
        }

        // The first build is the source's; a later one counts only when it names another root entry ("builds 10
        // customizable …" repeats the kit in other words).
        var main = usable[0];
        var mainRoot = Root(main.Build);
        if (listing.Lines.Count > 0 && mainRoot is null)
        {
            return listing.Lines;
        }

        // An addition by article follows the same rule: "… armed with a lasgun and a laspistol" adds no model.
        IEnumerable<Line> Counted(BuildSentence s) =>
            (Unit(s) ? s.Members.Select(m => m with { MemberOf = s.Build.Name }) : [s.Build])
                .Concat(s.Additions.Where(a => !a.Article || Root(a.Build) is not null).Select(a => a.Build));

        var chosen = usable.Where(s => ReferenceEquals(s, main) || (Root(s.Build) is { } r && r != mainRoot))
            .SelectMany(Counted)
            .ToList();

        // Two counts for one entry in one source ("a set of 10 …", "a squad of 9 …"): the source states neither, it drops
        // out for that entry; other sources still count.
        var stated = usable.SelectMany(Counted).GroupBy(key)
            .ToDictionary(g => g.Key, g => g.Select(l => l.Count).Distinct().Count());
        return chosen.Where(l => stated[key(l)] == 1).DistinctBy(key).ToList();
    }
}
