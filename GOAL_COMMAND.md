# Start the Autonomous Run

After placing this package in the repository and replacing the placeholders in `PROJECT_STATE.md`, start Claude Code and run the following command.

```text
/goal Continue autonomously executing the Product Owner → Prioritize → Plan/Architect → Delegate → Implement → Validate/QA → Product Owner Review → Repeat cycle for this product.

Use CLAUDE.md as persistent operating instructions and PROJECT_STATE.md as the authoritative operational memory.

The goal is NOT complete when a task, feature, milestone, sprint, implementation plan, or initial backlog is complete.

After every completed task:
1. Fully validate the implementation and fix failures.
2. Update PROJECT_STATE.md with the exact current state.
3. Return to Product Owner mode.
4. Perform a fresh product review against the product vision and actual user needs.
5. Identify the highest-value remaining improvement.
6. Record it as the active task.
7. Immediately begin the next iteration.

Continue automatically rather than returning control merely because one unit of work has finished.

Use subagents actively. Delegate independent work in parallel where safe. Route consequential product, planning, architecture, security, and difficult reasoning to strong models. Route well-defined mechanical execution to the most cost-effective capable models. Independently verify consequential work.

The goal is complete only when a fresh Product Owner review confirms ALL of the following:
- no unresolved P0, P1, P2, or P3 improvements remain;
- the complete core user journey has been verified end to end;
- all relevant builds, type checks, lint checks, and tests pass;
- no known meaningful defects or regressions remain;
- important validation, error, empty, loading, permission, and persistence states are handled;
- relevant security and data-integrity concerns have been reviewed;
- and a fresh independent product review identifies no additional meaningful work whose expected value justifies implementation.

Finishing the currently active task is never, by itself, sufficient evidence that the goal is complete.

If context is compacted, immediately reload CLAUDE.md and PROJECT_STATE.md and resume from the recorded active state.

Continue until the completion criteria above are genuinely satisfied or an external execution/resource limit prevents further work.
```

## Recommended first message after `/goal`

```text
Read CLAUDE.md, PROJECT_STATE.md, and docs/PRODUCT_AGENT.md. Inspect the repository, establish a working baseline, update PROJECT_STATE.md, and begin autonomous execution. Do not stop after assessment or planning.
```
