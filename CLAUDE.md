# CLAUDE.md

Data repository of the Armamentarium army list builder: one table, `box-contents.json`, of what a box contains. Structure:
`docs/architecture.md`. Table contract: `README.md` + `box-contents.schema.json`. Decisions: `docs/adr/` (index
`docs/adr/INDEX.md`); the founding decision is Armamentarium ADR 0118 in the main repository.

## Hard operating rules

1. **`/review-loop` is mandatory before every pull request**; all blocking findings are resolved before the pull request
   is opened. The binding loop is `.claude/commands/review-loop.md` with the five local agents in `.claude/agents/`:
   `security-reviewer`, `architect-reviewer`, `requirements-reviewer`, `code-reviewer`, `performance-reviewer`. No
   generic or global reviewer replaces any of them — a reviewer that does not know this repository's rule sources
   approves what it cannot see.
2. **Security `critical` and `high` findings block the pull request unconditionally** and can never be waived by a user
   decision.
3. **No commit, push, merge or pull request without explicit instruction**, and never from inside the loop. The table
   changes only through a merged pull request; the maintainer's merge is the hand check.
4. **No guessed data.** Ids come from the catalogues, counts from at least two sources; disagreement goes in as
   `countCandidates`.
5. **Trademarks.** No foreign trademark in the repository name, file names, project or namespace names. Product, game and
   catalogue entry names appear only as data and prose identifying what they name.
6. **`robots.txt` is deliberately ignored** for sources this project uses (rule of the Armamentarium project).
7. **Files** UTF-8 without BOM, LF. **Commits** conventional-commit style, no AI or tool markers.
