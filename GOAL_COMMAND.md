# Start the Autonomous Run

After placing these files in the repository and updating `PROJECT_STATE.md`, start Claude Code and run:

```text
/goal Continue the autonomous product-development loop for this repository.

Use `CLAUDE.md` as persistent operating rules and `PROJECT_STATE.md` as authoritative execution state.

Loop: Product Owner → Prioritize → Plan/Architect → Delegate → Implement → Validate/QA → Commit → Product Review → Opportunity Discovery → Repeat.

Completing a task, feature, milestone, release, sprint, plan, or known backlog does not complete the overall mission.

After every completed coherent unit:
1. Validate it and fix relevant failures.
2. Commit it without waiting for user review.
3. Update `PROJECT_STATE.md`.
4. Return to Product Owner mode.
5. Review the product against its vision, underlying user problem, and user outcomes.
6. Identify the highest-value next implementation, experiment, investigation, redesign, or product bet.
7. Record it as active and begin immediately.

Use subagents actively. Use strong reasoning models for product vision, strategy, architecture, security, privacy, difficult debugging, and opportunity discovery. Use cost-effective capable models for well-defined execution. Independently verify consequential work.

Before marking a release complete, require:
1. A Release Auditor review of correctness, regressions, tests, security, privacy, data integrity, core journeys, and important UI states.
2. A separate Product Opportunity Critic review that assumes the vision may be too narrow and generates at least five materially different opportunities: a core-outcome improvement, vision expansion/revision, adjacent user need, differentiation/defensibility opportunity, and simplification/removal/redesign.

Evaluate opportunities by user value, alignment, evidence, effort, risk, privacy/safety, differentiation, and learning value.

A clean release audit or empty backlog does not complete the mission. When a valuable next bet exists, update the vision if justified, reduce it to the smallest useful validated increment or experiment, record it, and begin.

Do not invent low-value features merely to continue. When implementation is not justified, continue with product discovery, assumption testing, prototyping, measurement, research, user-flow review, simplification, or blocker removal.

The run may stop only when the user explicitly says stop; an external token, context, compute, time, execution, or spending limit prevents continuation; essential access, authorization, credentials, or unavailable information blocks progress; the environment prevents further work; or a safety/policy constraint applies.

Do not terminate because “all criteria are met.” Those criteria may complete a release, not the overall product mission.

After context compaction, reload `CLAUDE.md` and `PROJECT_STATE.md`, inspect the worktree, and resume.

Always work in caveman mode and make sure all the subagents work in caveman mode too.
```

## Recommended first message

```text
Read CLAUDE.md, PROJECT_STATE.md, and docs/PRODUCT_AGENT.md. Inspect the repository, establish or verify the baseline, correct stale state, and resume autonomous execution. Do not stop after assessment, planning, a clean release audit, or completion of the known backlog.
```
