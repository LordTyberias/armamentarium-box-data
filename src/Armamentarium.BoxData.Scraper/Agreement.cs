namespace Armamentarium.BoxData.Scraper;

/// <summary>
/// Turns the listings of one product into entries. Every entry needs two confirming sources; one short and the box
/// waits (ADR 0003 §9, Armamentarium ADR 0118 §2). Disagreeing counts become candidates.
/// </summary>
public static class Agreement
{
    public static AgreementResult Agree(IReadOnlyList<Listing> listings)
    {
        var english = listings.Where(l => !l.German && l.Lines.Count > 0).ToList();
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

        foreach (var listing in english)
        {
            foreach (var line in listing.Lines)
            {
                Count(ContentLines.Normalize(line.Name), listing, line);
            }
        }

        // A translated listing names the models differently but keeps the publisher's order: it confirms an English
        // listing line by line only when both hold the same sequence of counts.
        foreach (var german in listings.Where(l => l.German && l.Lines.Count > 0))
        {
            var match = english.FirstOrDefault(e =>
                e.Lines.Select(l => l.Count).SequenceEqual(german.Lines.Select(l => l.Count)));
            if (match is null)
            {
                continue;
            }

            for (var i = 0; i < match.Lines.Count; i++)
            {
                Count(ContentLines.Normalize(match.Lines[i].Name), german, german.Lines[i] with
                {
                    NonPlayer = false,
                    AlternativeBuild = false,
                });
            }
        }

        if (order.Count == 0)
        {
            return new AgreementResult(null, sources, "no source lists models line by line");
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
                display[key].AlternativeBuild));
        }

        return new AgreementResult(entries, sources, null);
    }
}
