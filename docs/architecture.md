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
| Scraper service | `src/Armamentarium.BoxData.Scraper/`, tests `tests/`, solution `armamentarium-box-data.slnx` | Opens pull requests with new boxes (ADR 0003); with `--auto-merge` sets ids by name and merges them itself (ADR 0004). See §4. |
| Operation | `deploy/` | systemd service and timer, deploy script. Runbook: Armamentarium `docs/server/betrieb.md`. `*.service` and `*.timer` are LF (`.gitattributes`, `.editorconfig`): systemd reads a trailing CR as part of a value. Every other file stays CRLF. |

The table, the schema and the README form **one contract**. A change to one that the other two do not follow is a defect.

## 3 — Format rules

- **Format number.** A change a reader of the current `format` would misread (a field removed, renamed, its meaning
  changed, a new required field) raises `format` by one, in the table and in the schema's `const` together. Adding an
  optional field a reader may ignore does not.
- **Keys** are full article numbers (`productCodes`), never the nine-digit remainder.
- **Readings** are `kt`, `40k`, `aos`, `tow`, `hh` (ADR 0002), a reading family, not an app id. A new reading is a format
  decision; ADR 0002 decided that an added reading value does not raise the number, because a reader of format 1 ignores
  a reading it does not know. A Kill Team product gets `kt` only from the service (ADR 0002, Addendum ARMAM-601).
- **No guessed data.** Catalogue ids come from the catalogues, never from a similar name; counts come from at least two
  sources, disagreement is recorded as `countCandidates`. One named exception (ADR 0004): while the service runs with
  `--auto-merge`, it sets ids where an entry's name names exactly one root entry of its reading; the same hit picks a
  build sentence over a source's lines and lets two names of one entry confirm each other (ADR 0003 Addendum ARMAM-599).

## 4 — Scraper service

Fixed by Armamentarium ADR 0118 §4 and its Addenda ARMAM-591 and ARMAM-593, built under ARMAM-591, decided in ADR 0003
and ADR 0004:

- Its own .NET 10 project **in this repository**, not part of `Armamentarium.slnx`, never shipped with the app.
- Runs on the 1st and the 15th of every month on the project's own server (systemd timer).
- Changes the table **only through a pull request**. With `--auto-merge` (ADR 0004) it merges each one itself until the
  maintainer removes the switch; without it, it never merges and the merge is the hand check.
- Write access through a fine-grained token scoped to this repository only (contents and pull requests), without expiry
  (ADR 0004), stored outside every repository, never logged.
- Catalogue ids from the catalogues of a compiled allowlist of repositories (ADR 0004 §3), downloaded per run.
- Candidates from warhub-catalog `products.json`, one request per run to a compiled address (ADR 0003 §4).
- Sources from a compiled allowlist, no open search; at most one request per five seconds and host (per minute where a
  host restricts faster clients, ADR 0003 §6), stretched up to ten seconds for a host that answers 429 three times and
  never before a 429's `Retry-After`; a product such a shop did not answer is asked once more at the end of the run (ADR
  0003 Addendum ARMAM-595); own user agent
  without a contact address (ADR 0003 §7). Captchas and challenges are never circumvented.
- Counts from a source's "N x Name" lines or from its build sentences ("builds a squad of 10 …"; ADR 0003 Addendum
  ARMAM-599), never from part counts.
- Proposes one pull request per box, every entry confirmed by two sources: a **draft** without ids, or with `--auto-merge`
  a ready one with the ids of exactly one name hit, merged at once.
- Every further dependency of the service is decided with a reason (ADR in `docs/adr/`).

## 5 — Not in this repository

- Code of the Armamentarium app, its reader of the table, its conversion script — they live in the main repository.
- Texts, images, prices or descriptions from any source. Facts only (which entry, how many models).
