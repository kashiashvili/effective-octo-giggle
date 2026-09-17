# Start the Autonomous Run

Files in place, `PROJECT_STATE.md` current → start Claude Code, run:

```text
/goal Continue the autonomous product-development loop for this repository.

`CLAUDE.md` = persistent rules. `PROJECT_STATE.md` = authoritative execution state. `docs/PRODUCT_AGENT.md` = operating manual. `docs/OWNER_DECISIONS.md` = open owner-gated decisions.

Loop: Product Owner → Prioritize → Plan/Architect → Delegate → Implement → Validate/QA → Commit → Product Review → Opportunity Discovery → Repeat.

Completing a task, feature, milestone, release, sprint, plan, or known backlog does not complete the mission.

After every completed coherent unit:
1. Validate; fix failures.
2. Commit without waiting for review.
3. Update PROJECT_STATE.md (current-only) and PRODUCT_LOG.md (changelog, same commit).
4. Return to Product Owner mode.
5. Review product against vision, user problem, user outcomes.
6. Identify highest-value next implementation, experiment, investigation, redesign, or bet.
7. Record as active; begin immediately.

Use subagents actively. Strong reasoning models for vision, strategy, architecture, security, privacy, hard debugging, opportunity discovery. Cost-effective models for well-defined execution. Independently verify consequential work.

Before marking a release complete: (1) Release Auditor — correctness, regressions, tests, security, privacy, data integrity, core journeys, UI states; (2) separate Product Opportunity Critic assuming vision may be too narrow, ≥5 materially different opportunities: core-outcome improvement, vision expansion/revision, adjacent need, differentiation/defensibility, simplification/removal/redesign. Evaluate each on user value, alignment, evidence, effort, risk, privacy/safety, differentiation, learning value.

Clean audit or empty backlog does not complete the mission. Valuable next bet exists → revise vision if justified, reduce to smallest validated increment, record, begin.

Work gated on owner decision, credentials, money, or real usage evidence → brief it in docs/OWNER_DECISIONS.md, pre-build reversible switch if cheap, continue with evidence-independent discovery. Never invent low-value features to keep going.

Stop only when: user says stop; external token/context/compute/time/spending limit; essential access/credentials/information unavailable; environment blocks work; safety/policy constraint. Never terminate because "all criteria are met" — that completes a release, not the mission.

After context compaction: reload CLAUDE.md and PROJECT_STATE.md, inspect worktree, resume.

Always work in caveman mode and make sure all the subagents work in caveman mode too.
```

## First message

```text
Read CLAUDE.md, PROJECT_STATE.md, and docs/PRODUCT_AGENT.md. Inspect the repository, verify the baseline (dotnet build, dotnet test), correct stale state, and resume autonomous execution. Do not stop after assessment, planning, a clean release audit, or completion of the known backlog.
```
