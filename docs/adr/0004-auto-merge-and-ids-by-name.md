# ADR 0004 — Auto-merge and catalogue ids by name, until the table is filled

**Status:** Accepted (2026-10-17, Armamentarium ARMAM-593). Founded on Armamentarium ADR 0118 Addendum ARMAM-593 and the
named exception to "Datenintegrität vor Bequemlichkeit" in Armamentarium's `CLAUDE.md`. Overrides parts of ADR 0003
(§2 daily, §10 drafts without ids, §12 five open while `--auto-merge` runs, Rejected "Ids by exact name match").

## Context

The service runs on the server since 2026-10-17 (ARMAM-593). Its drafts wait for the maintainer to add catalogue ids,
readings and roles before merging. The maintainer does not know catalogue ids and cannot add them by hand; with five
open drafts every run stops at once, so the table would not grow. The maintainer wants the table filled first and to
merge by hand again afterwards.

Measured on 2026-10-17: the catalogues of every reading are public on GitHub (archives of 0.2 to 4 MB). Exact name hits are
few, but where a shop line names a catalogue root entry exactly, the ids equal those checked by hand for the first data
(ARMAM-588): Murderwing's Champion and Shrieker, Celestian Insidiants' Denuncia and Cremators, the Ripper Swarm of Terror
on Devlan. A name found in more than one catalogue (`Superior`) gets none.

## Decision

1. **Switch:** `--auto-merge` on the command line (the unit's `ExecStart`). With it, each pull request is opened ready,
   not as a draft, merged at once through the REST API (`PUT pulls/{n}/merge`) and its branch deleted; the next box is
   built on the merged table. Without it, the service behaves as ADR 0003 says: drafts, at most five open. The maintainer
   removes the switch when the table holds enough; no rule in the code decides that.
2. **Ids by name:** at the start of a run the service downloads the catalogues of every reading from a compiled allowlist
   and reads their root entries (a root `selectionEntry` of type unit or model, a root `entryLink` to a `selectionEntry`
   with its own name and its target; libraries and `sharedSelectionEntries` are no root offer). An entry gets
   `catalogueId` and `entryId` when its normalised name (ADR 0003 §9) names **exactly one** pair of catalogue id and entry
   id within its reading; one name in two catalogues, or none, gets no ids. The 10th and the 11th edition share their ids,
   so the same pair from both counts once.
3. **Catalogue sources** (compiled, matching the app's `BsDataEditionSources`; a change there is a change here):

   | Reading | Repository | Reference |
   |---|---|---|
   | `40k` | `BSData/wh40k-10e` (XML), `BSData/wh40k-11e` (JSON) | `main` |
   | `kt` | `BSData/wh40k-killteam` | latest release |
   | `aos` | `BSData/age-of-sigmar-4th`, `LordTyberias/armamentarium-spearhead-data` | latest release, `main` |
   | `tow` | `vflam/Warhammer-The-Old-World` | latest release |
   | `hh` | `BSData/horus-heresy-3rd-edition` (JSON) | `main` |

   Archives come from `codeload.github.com` (release tags from `api.github.com`), with the run's client: no redirect, at
   most 64 MB, each catalogue file at most 32 MB, read in memory and never unpacked. BattleScribe JSON is read with
   `System.Text.Json`, XML with `System.Xml.Linq` and no document type declaration; no package. A source that cannot be
   read, or holds a catalogue file that is too large, not well-formed, or carries an id outside the forms the sources use
   (letters, digits, `-`, `_`, `.`, `:`, at most 128 characters: BSData's hex groups, the Spearhead repository's
   `sph::box::01-name`), leaves its whole reading without ids for this run (a missing file could hide a twin of a name),
   logged; the run goes on.
4. **Gaps are merged:** a box whose entries do not all get ids is merged anyway; an entry without ids is a name and a
   count, shown, not addable (README, Entry).
5. **Roles and readings** stay as ADR 0003 §10 sets them (`team`; `npo` in `kt` and `extra` elsewhere where the line says
   non-player; readings from the product's game systems), now unchecked.
6. **Pace:** the timer runs on the 1st and the 15th of every month at 03:00. Candidates are only products without a row,
   so a run adds only what is new.
7. **Token:** without expiry (maintainer decision), replacing "at most 90 days". Contents and pull requests, this repository
   only, as before.
8. **Combat Patrols** keep `generation` only (Armamentarium ADR 0118 §5); whether they get entries with ids from the
   Combat Patrol repository is decided with Armamentarium ARMAM-594.

## Consequences

- No hand check while the switch is on. A name that is unique but means another kit (Battleclade: 10 models in the box,
  9 in the unit) gets the unit's ids unchecked; the README rule "a kit counts as a unit only when it holds exactly that
  unit's models" holds only for hand-made rows meanwhile.
- A stolen token writes to the table unchecked while the switch is on and does not expire.
- Many entries stay without ids: shop names seldom equal catalogue names.
- The app bundles the table at a fixed commit (Armamentarium ADR 0118 Addendum ARMAM-589); new rows reach it with the next
  pin.

## Rejected

- **Ids by the most similar name:** more ids, but wrong ones would be merged unchecked.
- **Merging only complete boxes:** incomplete ones would block the five open, and nobody can complete them by hand.
- **A local script on the maintainer's computer, or the app's catalogue cache:** the maintainer wants it all on the
  server, nothing from his computer or the app.
- **GraphQL `markPullRequestReadyForReview`:** a ready pull request from the start needs no second API.
- **Ids for 40k from the 10th edition only:** units new in the 11th edition would get none.
