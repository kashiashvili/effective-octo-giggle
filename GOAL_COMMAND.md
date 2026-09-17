# Start the Autonomous Run

Rules live in `CLAUDE.md` (loaded automatically). State lives in `PROJECT_STATE.md`. This prompt only points at them, so it cannot drift.

Kickoff prompt:

```text
Continue the autonomous product-development loop for this repository.

Read CLAUDE.md, then PROJECT_STATE.md, then docs/PRODUCT_AGENT.md. Run git status and git log --oneline -10. Verify the baseline (dotnet build -warnaserror, dotnet test) and correct any stale state before doing anything else.

Resume the Active Task in PROJECT_STATE.md. If it is done, follow CLAUDE.md §1: validate, commit, persist state, Product Owner review, Opportunity Discovery, next bet. Open owner-gated decisions are in docs/OWNER_DECISIONS.md; answer any of them here and the loop implements it immediately.

Stop only under CLAUDE.md §10. Do not stop after assessment, planning, a clean release audit, or completion of the known backlog.

Always work in caveman mode and make sure all the subagents work in caveman mode too.
```

Answering an owner decision: reply with the decision number and choice, e.g. `Decision 2: (b)` or `enable the guard`. The loop implements it, logs it in `PRODUCT_LOG.md`, and closes the brief entry.
