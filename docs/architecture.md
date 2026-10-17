# Architecture

Binding target structure of this repository. Derived from the state at creation (2026-10-17) and from Armamentarium
ADR 0118 (main repository, `docs/adr/0118-box-contents-table.md`), which decided that this repository exists.

## 1 — What this repository is

A data repository. Its product is **one table**, `box-contents.json`, read by the Armamentarium app at build time (the
app bundles a fixed commit of it; it never fetches it at runtime). Everything else exists to keep that table correct.

## 2 — Units

| Unit | Path | Role |
|---|---|---|
| Table | `box-contents.json` | The data. Single file, `format` number at the top. |
| Schema | `box-contents.schema.json` | JSON Schema draft 2020-12 of the table. The table references it with `$schema`. |
| Contract prose | `README.md` | The rules the schema cannot express (key uniqueness across boxes, what goes in, NPO fallback, sources). |
| Decisions | `docs/adr/` | This repository's decisions, index `docs/adr/INDEX.md`. |
| Scraper service | not yet present | Planned (Armamentarium ticket ARMAM-591). See §4. |

The table, the schema and the README form **one contract**. A change to one that the other two do not follow is a defect.

## 3 — Format rules

- **Format number.** A change a reader of the current `format` would misread (a field removed, renamed, its meaning
  changed, a new required field) raises `format` by one, in the table and in the schema's `const` together. Adding an
  optional field a reader may ignore does not.
- **Keys** are full article numbers (`productCodes`), never the nine-digit remainder.
- **Readings** are `kt` and `40k` only, a reading family, not an app id. A new reading is a format decision.
- **No guessed data.** Catalogue ids come from the catalogues, never from a similar name; counts come from at least two
  sources, disagreement is recorded as `countCandidates`.

## 4 — Scraper service (planned)

Fixed by Armamentarium ADR 0118 §4, to be built under ARMAM-591:

- Its own .NET 10 project **in this repository**, not part of `Armamentarium.slnx`, never shipped with the app.
- Runs once a day on the project's own server (systemd timer).
- Changes the table **only through a pull request**; it never merges. The merge is the hand check.
- Write access through a fine-grained token scoped to this repository only (contents and pull requests), at most 90 days,
  stored outside every repository, never logged.
- Sources from a compiled allowlist, no open search; at most one request per five seconds and host; own user agent with
  the project's contact address.
- Every further dependency of the service is decided with a reason (ADR in `docs/adr/`).

## 5 — Not in this repository

- Code of the Armamentarium app, its reader of the table, its conversion script — they live in the main repository.
- Texts, images, prices or descriptions from any source. Facts only (which entry, how many models).
