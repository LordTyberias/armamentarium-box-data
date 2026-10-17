# ADR 0001 — Review loop before every pull request

**Status:** Accepted (2026-10-17, Armamentarium ARMAM-588).

## Context

The table changes only through merged pull requests; the maintainer's merge is the hand check (Armamentarium ADR 0118 §4).
A hand check of a large data diff misses guessed ids, single-source numbers and schema drift. A scraper service will open
pull requests daily (ARMAM-591), which raises the volume further.

## Decision

- `/review-loop` (`.claude/commands/review-loop.md`) runs before every pull request; all blocking findings are closed
  before it is opened.
- Five repository-local reviewers in `.claude/agents/`: `security-reviewer`, `architect-reviewer`, `requirements-reviewer`,
  `code-reviewer`, `performance-reviewer`. No global or generic reviewer replaces them.
- Authority on conflict: security > architect > requirements > code > performance.
- Blocking: every architect finding; `must-fix` of requirements, code and performance; security `critical`/`high`
  unconditionally, never waived by a user decision. A new dependency (`package-decision`) and a fix beyond the diff
  (`needs-decision`) are decided by the maintainer.
- After the loop approves, the maintainer's merge remains the hand check. The loop never commits, pushes, merges or opens
  a pull request.

## Rejected

- **Hand check only:** misses what the reviewers catch mechanically.
- **Reusing the Armamentarium reviewers:** they cite the app's rule sources, not this repository's contract.
