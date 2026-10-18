# ADR 0002 — Readings for every game system of the app

**Status:** Accepted (2026-10-17, Armamentarium ARMAM-591). Addendum ARMAM-601: a Kill Team product reads `kt` only.

## Context

Format 1 knows two readings, `kt` and `40k` (`docs/architecture.md` §3: a new reading is a format decision). The scraper
service (ADR 0003) watches every miniatures product of the game systems the Armamentarium app supports: Warhammer 40,000,
Kill Team, Age of Sigmar (Spearhead boxes are Age of Sigmar boxes), The Old World and the Horus Heresy. Their boxes need a
reading of their own.

The app's reader passes `reading` through as text and maps an edition to a reading; a reading it does not know never matches
an edition, so the box falls back to name-based candidates (Armamentarium ADR 0118 §8). Measured in
`EmbeddedBoxContentsProvider` on 2026-10-17.

## Decision

- Three further readings: `aos` (Age of Sigmar, Spearhead included), `tow` (The Old World), `hh` (Horus Heresy).
- `format` stays **1**. A new reading is an optional value a reader of format 1 ignores; it does not change the meaning of
  any existing field (§3 of `docs/architecture.md`: only a change a reader would misread raises the number).
- A reading names a game family, never an edition (`aos`, not `aos4`), as `kt` and `40k` do.
- Schema enum and README table follow in the same change. Which app edition reads which of the new readings is decided in
  the app, not here.

## Rejected

- **Format 2:** the bundled reader would treat the whole table as absent until the app follows, for a value it can ignore.
- **Edition ids as readings (`aos4`, `hh3`):** would put the edition into the key, the mistake `kt`/`40k` were named to
  avoid.
- **Only `kt` and `40k`, other systems later:** the service would watch boxes it could not propose.

## Addendum ARMAM-601 — a Kill Team product reads Kill Team only (2026-10-18)

- **The service gives a product whose catalogue systems include Kill Team the reading `kt` only**, even when the catalogue
  lists Warhammer 40,000 beside it. Its entries are the operatives the shops list ("Superior", "Denuncia"); one entry list
  stood in every reading, so under `40k` they were names no 40k unit carries (Kill Team: Celestian Insidiants, PR #19).
  Under the 10th and 11th edition such a box falls back to the app's name-based candidates (Armamentarium ADR 0118 §8).
- **Hand-made boxes keep their `40k` reading** where it names 40k entries with ids (Hivestorm, Tomb World, Exodite …). A
  `40k` reading without a single entry with an id or a `choice` is removed: Kill Team: Starter Set ("Angels of Death" × 7,
  "Plague Marines" × 7) and Kill Team: Celestian Insidiants.
- **Imperial Navy Breachers (99120108152)** is removed: the catalogue lists it under 40k only, but its rows are operatives.
  The service proposes it again in its next run with the same rows; that is accepted (maintainer's decision) and left to
  the follow-up on kits the shops describe per operative.
- Rejected: **`40k` only with entries that name exactly one root entry there** (a unique name can still mean another kit,
  README "a kit counts as a unit only when it holds exactly that unit's models", unchecked under `--auto-merge`);
  **unchanged** (every further Kill Team product would carry operatives under `40k`).
