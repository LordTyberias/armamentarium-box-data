# armamentarium-box-data

An unofficial table of what a boxed miniatures product contains: for each box, the catalogue entries it holds and how
many models of each. It feeds the box scan of the Armamentarium army list builder, which turns a scanned box into
showcase entries.

No open, machine-readable data set of this kind exists. The table is collected from public retailer, reviewer and
publisher information and checked by hand; see [Sources and checking](#sources-and-checking).

## Licence

The MIT licence in [`LICENSE`](LICENSE) covers this repository's own contributions: the structure, the selection and the
compiled entries. It claims no licence over any source the facts were collected from. Product, game and faction names
are used only to identify the products and catalogue entries they belong to; they belong to their owners. This project
is not affiliated with any publisher.

## Files

| File | Content |
|---|---|
| `box-contents.json` | The table |
| `box-contents.schema.json` | JSON Schema (draft 2020-12) of the table; the table points to it with `$schema` |

## Format 1

```json
{
  "$schema": "./box-contents.schema.json",
  "format": 1,
  "boxes": [
    {
      "name": "…",
      "productCodes": ["60010199084", "15010199084"],
      "sources": ["https://…", "https://…"],
      "readings": [
        { "reading": "kt", "entries": [ … ] },
        { "reading": "40k", "entries": [ … ] }
      ]
    }
  ]
}
```

`format` is the format number. A reader that does not know the number must treat the table as absent.

### Box

One object per box.

| Field | Meaning |
|---|---|
| `name` | The box's English product name, for human checking only. Never a key. |
| `productCodes` | The publisher's full article numbers of the box, **one per language variant**, each listed explicitly (`60…` English, `04…` German, `15…` Chinese, …). These are the keys. The nine digits after the language prefix are **not** a key: they collide between unrelated products. A code appears in at most one box. |
| `sources` | At least two URLs the box's contents were checked against. Links only; no text is copied. |
| `readings` | The box's contents, one block per reading (below). |
| `generation` | Combat Patrol boxes only, instead of `readings`: `current` or `legacy`, naming which patrol of the Combat Patrol catalogue the codes mean. The patrol's contents come from that catalogue; this table does not repeat them. |

### Reading

A kit can be read differently per game: as Kill Team operatives or as a Warhammer 40,000 unit. Each reading lists the
box's contents as that game's catalogues name them. The service reads a Kill Team product as Kill Team only; a `40k`
reading beside `kt` is made by hand and names 40k entries (ADR 0002, Addendum ARMAM-601).

| `reading` | Catalogues | Editions of the app that use it |
|---|---|---|
| `kt` | Kill Team (2024) | Kill Team |
| `40k` | Warhammer 40,000 10th and 11th edition | 10th, 11th |
| `aos` | Age of Sigmar, Spearhead boxes included | none yet |
| `tow` | The Old World | none yet |
| `hh` | Horus Heresy | none yet |

`reading` is neither a game system id nor an edition id of the app, and not the `.gst` id either (the two 40k editions
carry different ones). Entry ids are shared by 10th and 11th edition and resolve in the catalogue of whichever is chosen.
An edition without a reading falls back to the app's name-based candidates.

### Entry

| Field | Meaning |
|---|---|
| `role` | `team`: the box's playable team or unit in this reading; preselected. `npo`: a Kill Team non-player operative, a real model that is not playable in the box's teams; not preselected. `extra`: a model playable in this reading that is not the box's team (Kill Team NPOs read as 40k units); not preselected. |
| `reading` | Optional. The catalogue family `catalogueId` belongs to, when it differs from the block's (a Kill Team NPO falling back to a 40k entry). |
| `catalogueId` | `id` of the catalogue (`.cat`) holding the entry. |
| `entryId` | `id` of the root `selectionEntry`: an operative in Kill Team, a unit in 40k. |
| `choice` | Instead of `entryId`, when the box's models build freely chosen operatives: the operatives the kit builds, each `entryId` + `name`. `count` is then the number of models across all of them; the user distributes them. |
| `name` | The entry's catalogue name, for checking only. |
| `count` | Number of **models** in the box for this entry, as the box is sold. |
| `countCandidates` | Instead of `count`, when the sources disagree and the number could not be settled: the numbers in question. The entry is not preselected; the user sets the number. |

An entry carries either `catalogueId` with `entryId`, or `catalogueId` with `choice`, or neither. An entry with neither
is a name and a model count only: the box holds these models, but no catalogue entry of this reading matches them. It is
shown, not addable. Ids are never guessed from a similar name. The one exception is the scraper service while it merges
by itself (`docs/adr/0004-auto-merge-and-ids-by-name.md`): it sets ids where an entry's name names exactly one catalogue
root entry of its reading.

A `choice` option may share its `entryId` with a fixed entry of the same reading (two Aspirants fixed, a third model
free): the fixed entry counts its own models, the choice counts the remaining freely built ones.

A kit counts as a unit of a reading only when the box holds exactly that unit's models. A box whose models differ from
the unit (the Sanctifier kit holds a Confessor and a Cherub the 40k unit has no place for) is name and count only, even
when an entry of the same name exists. Rows the scraper merges with `--auto-merge` are not checked for this: a name that
names exactly one entry gets its ids whatever the model count (ADR 0004, Consequences).

### What goes in

- **Models only.** Terrain, datacards, tokens and markers never.
- **The build the box is sold as.** Alternative builds of the same parts are left out.
- **NPOs** point to the Kill Team non-player operative entry **without** a points suffix (`Necron Warrior`, not
  `Necron warrior (Pts 2)`): showcase entries are models, points belong to lists. Without such an entry, the regular 40k
  unit (`"reading": "40k"`); without that, name and count only.

## Sources and checking

Every number is checked against at least two independent sources (publisher articles, retailer listings carrying the
publisher's text, reviews). Numbers the sources disagree on go in as `countCandidates` until settled.

The table changes only through a merged pull request. Each pull request by hand passes this repository's review loop
first; the merge by the maintainer is the hand check.

A scraper service (`src/`, decisions in `docs/adr/0003-scraper-service.md` and `docs/adr/0004-auto-merge-and-ids-by-name.md`)
opens pull requests for boxes it finds in allowlisted shops, one per box: article numbers, sources, and each model line
confirmed by two sources. Until the table is filled it runs with `--auto-merge`: it sets the ids of exactly one name hit
in the catalogues of the reading and merges each pull request itself, unchecked. Without the switch it opens drafts
without ids, and the maintainer adds ids, readings and catalogue names and checks the preset roles before merging.
