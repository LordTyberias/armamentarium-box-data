# ADR 0002 — Readings for every game system of the app

**Status:** Accepted (2026-10-17, Armamentarium ARMAM-591).

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
