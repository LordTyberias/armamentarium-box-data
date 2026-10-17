---
description: Mandatory review before every pull request — five local reviewers in parallel, fixes in a loop until all approve.
argument-hint: "[max-iterations=5]"
---

# Review loop

Binding before every pull request of this repository (`CLAUDE.md`, `docs/adr/0001-review-loop.md`). Maximum iterations:
`$1`, default **5**.

## Precedence

**Authority — who wins when two reviewers contradict each other:**

```text
security  >  architect  >  requirements  >  code  >  performance
```

A security finding overrides convenience, correctness and elegance and is never waived. Architecture outranks line-level
opinion because it decides where the line belongs. Requirements outrank code style because unspecified polish is worth
less than specified content. Performance ranks last: a faster wrong, insecure or misplaced implementation is no
improvement. A lower-ranked finding that can only be satisfied by violating a higher-ranked one is **dropped and named in
the summary**, never split into a half-fix.

**Application — the order fixes are applied in one iteration:**

```text
architect  ->  requirements  ->  security  ->  performance  ->  code
```

Mechanical, not moral: a structural fix can move or delete what a later fix would touch, and missing specified content
must exist before it is worth securing, optimising or polishing.

## Steps

1. Default branch is `main`. Inspect the complete diff:
   `git diff main...HEAD`, `git diff HEAD`, `git diff --cached`, `git status --porcelain`.
2. No diff: report `Nothing to review.` and stop.
3. Spawn **all five reviewers in parallel, in one message**, every iteration: `security-reviewer`, `architect-reviewer`,
   `requirements-reviewer`, `code-reviewer`, `performance-reviewer`. Give each the diff scope and, for a data change, the
   pull request context (which boxes, which sources). Model: Sonnet, reasoning high.
4. Parse each reviewer's fenced JSON. A reviewer without parsable JSON is re-run; it never counts as approval.
5. **Blocking set:** security `critical`/`high`; every architect finding; requirements `must-fix`; code `must-fix`;
   performance `must-fix`. An architect `package-decision` or `needs-decision` already closed by a user decision **in this
   run** no longer blocks (the reviewer re-reports it while the code still shows it). A security `critical`/`high` can
   **never** be closed by a user decision; `APPROVED` is never printed while one is open.
6. For each blocker: explain it and verify it against the actual files before editing.
7. Apply only justified fixes within the current diff, in the **application order**. Contradictions resolved by the
   **authority order**, the dropped finding recorded.
8. Verify after fixes:
   ```powershell
   pwsh -NoProfile -Command 'if (-not (Test-Json -Path box-contents.json -SchemaFile box-contents.schema.json)) { exit 1 }'
   pwsh -NoProfile -Command '$c = (Get-Content box-contents.json -Raw | ConvertFrom-Json).boxes.productCodes; $d = $c | Group-Object | Where-Object Count -gt 1; if ($d) { $d.Name; exit 1 }'
   ```
   Once the scraper service exists: `dotnet build` and `dotnet test` of its solution as well.
9. Repeat until all five approve, the same blocker repeats twice, or the iteration limit is reached.
10. After approval: list the non-blocking findings, ask the user (`AskUserQuestion`) which to fix, apply only the
    confirmed ones, re-run step 8. Skipped in the `STOPPED` case.

## User decisions

`package-decision` and `needs-decision` are never fixed automatically. Pause and ask via `AskUserQuestion`: apply now /
defer with a recorded note / decline with a reason. The answer closes the finding for the rest of the run and appears in
the summary. An accepted dependency gets an ADR in `docs/adr/` with its `INDEX.md` row and the `docs/architecture.md`
update in the same run.

## Never

- Reviewers ask nobody and modify nothing; asking and writing stay with this loop.
- Never commit, push, merge, publish or open a pull request inside the loop.
- Never claim approval when JSON parsing failed or step 8 did not run.
- A documentation-only diff (only `*.md`, none of `README.md`, `docs/architecture.md`, `docs/adr/**`, `CLAUDE.md`,
  `.claude/**`) may skip step 8; it still gets reviewed.

## Summary — exactly one

```text
APPROVED after N iterations
Security reviewer: ...
Architect reviewer: ...
Requirements reviewer: ...
Code reviewer: ...
Performance reviewer: ...
User decisions: ... (or none)
Conflicts resolved by precedence: ... (dropped finding · the higher-ranked one that overrode it, or none)
Open non-blockers: ... (declined by the user)
```

```text
STOPPED after N iterations
Remaining blockers:
- path:line · reviewer · title
User decisions: ... (or none)
```
