# Project State

> This file is the authoritative operational memory for the autonomous product-development loop.
> Keep it concise, factual, and current. Update it after every meaningful iteration.
>
> A clean release is a milestone, not termination of the overall product mission.

## Product Idea

**Profiler** — a privacy-preserving social compatibility and matching web app.

Today, users connect online platforms they already use. Profiler extracts interest signals, reduces them to an anonymous MinHash fingerprint, discards the raw data, and matches people whose fingerprints are similar.

The next product-discovery phase is evaluating whether the product should evolve from **interest similarity only** toward **multi-signal compatibility**, potentially including an optional evidence-based values/worldview questionnaire while preserving the privacy north star.

Stack: ASP.NET Core MVC (.NET 9), EF Core, SQLite, Razor views, hand-written CSS. No JavaScript framework. The implementation is in `Profiler.Web/`.

Full product handbook and changelog: `PRODUCT_LOG.md`, which must be updated with every product or implementation change.

## Current Product Vision

Help privacy-conscious people find others who genuinely share their interests **without surrendering raw personal data**.

The product earns the right to exist only if it is more private than the alternatives. “Raw data is never stored” is a product constraint, not merely a slogan.

## Potential Better Vision Under Evaluation

Help people find unusually compatible relationships using multiple **consented, user-controlled signals**—such as interests, values, worldview, and intent—while minimizing retained personal data and making matching understandable.

This is a hypothesis, not an approved final direction.

Any vision expansion must preserve:

- Explicit consent
- Data minimization
- Clear user control
- Explainable matching
- No clinical or deterministic claims about personality
- No unnecessary retention of raw activity or questionnaire responses

## Target User

A privacy-conscious person who wants to meet like-minded people around niche interests, values, or worldview but does not want to surrender raw activity or intimate profile data to another social network.

## Core Problem

Most compatibility and interest-based matching products require users to expose raw behavior, detailed profiles, or sensitive answers.

Profiler should produce useful matching while retaining only the minimum derived information needed to operate.

## Core Value Proposition

**Find your people without surrendering your raw data.**

The current implementation matches from anonymized interest fingerprints. Product discovery is evaluating whether optional privacy-preserving compatibility signals can improve match quality and differentiation.

## Core User Journey — Current Release

1. Register and sign in automatically.
2. Connect at least one source.
3. Profiler fetches interests, builds a fingerprint, and discards raw data.
4. View ranked matches with qualitative tiers and shared source types.
5. Read optional bio/contact details and reach out off-platform.
6. Manage privacy, sources, profile, password, visibility, blocks, export, and account deletion.

## Success Criteria — Current Release

- A new user can reach a real match list without credentials they cannot reasonably obtain.
- Raw interest data is not persisted.
- Important connector, fingerprint, empty-match, rate-limit, auth, and deletion failures are explained in the UI.
- Privacy claims are asserted with non-vacuous database tests.
- Build, tests, migrations, and security posture remain healthy.

## Product-Mission Continuation Rule

The overall product mission is intentionally open-ended.

A stable release, passing tests, completed known backlog, clean release audit, or implemented current vision establishes **release completion only**.

After release completion:

1. Run a separate Product Opportunity Critic.
2. Challenge the current vision and assumptions.
3. Generate and evaluate materially different product bets.
4. Select the strongest justified next experiment or increment.
5. Continue the autonomous loop.

The run may stop only when:

1. The user explicitly instructs it to stop.
2. An external execution, token, context, compute, time, or spending limit prevents continued work.
3. Progress requires unavailable credentials, access, legal authority, destructive approval, or essential information that cannot reasonably be inferred.
4. The environment prevents further implementation, investigation, or validation.
5. A safety or policy constraint prevents further work.

## Release 1 Status

**Release 1 is complete and stable based on the recorded historical reviews.**

This does **not** complete the overall product mission.

Recorded completed work includes:

- Privacy-preserving fingerprint hardening
- Session invalidation and account recovery
- Reciprocal discoverability and atomic connect behavior
- Connector cancellation and rate-limit improvements
- Username hardening
- SSRF transition-address hardening
- Empty-state and cold-start improvements
- Invite link, match-refresh messaging, and interest-lens improvements
- Core journey and important privacy behavior reviewed end to end

The opt-in contact model remains an explicit owner decision unless new evidence justifies reopening it.

## Current Phase

**Building — multi-signal compatibility, slice 1 (connection intent).** Product Opportunity
Discovery produced a Product Owner decision (see Discovery Decision below): expand toward
privacy-preserving multi-signal compatibility incrementally, lowest-risk signal first. Values
questionnaire deferred behind the same infrastructure.

## Discovery Decision (2026-07-22) — multi-signal compatibility

Investigated the optional values/worldview questionnaire and ran an independent Opportunity Critic
(≥5 bets). **Decision:**

- **Adopt the vision-expansion direction** (interest-only → privacy-preserving *multi-signal*
  compatibility) **incrementally**, lowest-risk signal first.
- **First slice: optional "connection intent"** — a user-controlled, low-sensitivity self-report of
  what kind of connection they want. It is a recorded P1 opportunity, needs no licensed instrument,
  works even with zero connected sources (cold-start), and targets the real gap: *sharing interests
  is not the same as being worth reaching out to.*
- **Defer the values/worldview questionnaire.** Schwartz's 2-axis structure is the strongest
  evidence-based candidate, but validated item wordings carry licensing/attribution norms and the
  signal is moderately sensitive. Build it behind the same infrastructure **after** the intent slice
  validates the pattern and the privacy model. **Reject** Big Five (clinical/personality overclaim)
  and Moral-Foundations/political items (filter-bubble + protected-class risk) for now.
- **Reject** any verbatim copyrighted psychometric instrument and any single blended
  "compatibility %". Signals stay **separate and explainable**.

### Privacy model (applies to every added signal)
Opt-in, skippable, independently deletable, included in export. Store only a **small fixed derived
representation**; for a future questionnaire, discard raw answers after deriving the vector (retake =
re-answer), matching the interest-fingerprint mental model. Never show another user your raw answers
— only coarse alignment. A values dimension, when added, must be hideable and must not be a hard
filter (avoid filter bubbles).

### Matching model
Keep dimensions **separate and explainable** ("Strong on interests · Both here to collaborate").
Do not blend into one opaque score. Interest ranking is unchanged for now; new signals are displayed
(and optionally filterable) dimensions, not a hidden re-ranking.

## Signal 1 (connection intent) — COMPLETE

Shipped as a coherent feature across: field + closed-set validation (`2c20033`), mutual-shared
highlight (`a308506`), dashboard self-preview (`6b3d73a`), adoption nudge (`49a7199`). Opt-in,
shown to matches as a separate explainable line, mutual emphasis, withheld while hidden, in export,
cleared via profile, removed on deletion, never re-ranks or blends into a score. **242 tests, build
0 warnings.** (Deferred, low value now: filter-by-intent — the mutual highlight already surfaces
same-intent matches, and filtering only matters at scale like the source filter.)

## Active Task — Signal 2: optional values/worldview (the next real bet)

The intent pattern validated the infrastructure; now build the higher-differentiation signal behind
the same model. **Decided constraints (see Discovery Decision):** Schwartz's public 2-axis structure,
**our own plainly-worded items** (no licensed instrument), **discard raw answers** after deriving a
small vector (retake = re-answer), opt-in/skippable/hideable/deletable, in export, shown as **coarse
alignment** on match cards (never raw answers, never a blended score, not a hard filter → avoid
filter bubbles). Reject Big Five and political/moral items.

### Smallest useful validated increment (start here)
1. Pick ONE axis first: **openness-to-change ↔ conservation** (least sensitive of the Schwartz axes).
2. Author 3–4 own Likert items for it; derive to a single signed bucket (e.g. −2..+2). Discard raw.
3. Store `AppUser.ValuesOpenness` (nullable small int) + a version tag. Migration.
4. Consent-gated opt-in page (`/account/values`), skippable, with clear "what this is / isn't"
   copy (no clinical/personality claims). Retake overwrites; a delete-values control.
5. Match card: coarse alignment line when both set ("Similar outlook" / "Different outlook"),
   separate from interests and intent; hideable; withheld while hidden.
6. Export includes the derived bucket; account deletion removes it.
7. Tests: derivation buckets, opt-in/skip/retake/delete round-trip, coarse-alignment display,
   privacy (raw answers never persisted — assert against the DB like the fingerprint test).
8. Update `PRODUCT_LOG.md` + this file. Then evaluate adding the second axis.

## Completed — Slice 2: mutual connection-intent highlight (`a308506`)

Match cards emphasise a shared intent ("You're both here for: X") and show the plain line otherwise.
Separate explainable line; no score; no re-ranking; withheld while hidden. Integration-tested.

## Completed — Slice 1 of multi-signal compatibility: optional "connection intent."

Smallest validated vertical slice, reusing the existing opt-in-profile pattern (bio/contact):

### Definition of Done

- `AppUser.ConnectionIntent` (nullable, small closed set: unspecified / friends / collaborators /
  discussion / open). Migration.
- Profile page: optional selector, clearly labeled optional; blank stays private.
- Match card: show the match's intent as a separate explainable line when set (like contact); never
  a score, never a blended tier.
- Included in `/account/data` + JSON export; cleared via profile; removed on account deletion.
- No change to interest ranking; no blended "compatibility score".
- Tests: intent round-trips on profile, appears on the match card, appears in export.
- `PRODUCT_LOG.md` + this file updated.

Follow-ups (recorded, not this slice): make intent filterable (reuse the shared-source chip
pattern); add interaction-style preferences; then the Schwartz-grounded values vector.

## Current Execution Notes

Baseline commands recorded in the previous state:

```bash
dotnet build
dotnet test
dotnet run --project Profiler.Web
```

Baseline re-established 2026-07-22: **225 tests pass, build 0 warnings** (before slice 1). The
match-filter feature ("filter by shared interest area") landed at commit `d67d140`.

Working branch recorded previously: `rebuild/dotnet-profiler`.

Commit continuously in focused logical units. Do not wait for owner review before committing coherent validated work.

## Backlog

### P0 — Critical

- None currently recorded. Re-establish baseline before assuming the state remains clean.

### P1 — Core Product / Product Bets

- **Active:** Evaluate optional values/worldview questionnaire as a privacy-preserving compatibility signal.
- Evaluate a hybrid matching model that keeps interest similarity and values/worldview compatibility as understandable separate dimensions.
- Evaluate relationship intent or desired connection type as a user-controlled matching constraint.
- Reassess whether the current vision should expand from “interest matching” to “privacy-preserving compatibility matching.”

### P2 — Quality / Reliability / UX

- Re-run build, tests, migrations, and the core journey because historical test counts conflict.
- Ensure any new questionnaire or compatibility flow has explicit consent, skip, retake, export, and deletion behavior.
- Ensure match explanations do not overstate scientific certainty or imply clinical/personality diagnosis.

### P3 — Enhancement / Experiments

- Prototype an explainable match card showing separate interest and values dimensions.
- Explore user-adjustable weighting rather than a hidden universal scoring formula.
- Explore a short onboarding questionnaire versus a deeper optional questionnaire.
- Explore local or ephemeral scoring approaches that minimize retained sensitive derived data.
- Revisit referral tracking only when network scale makes attribution useful.
- Revisit pagination and containment-based scoring when actual usage justifies them.

### P4 — Scale / Optional

- LSH banding when the number of users makes exhaustive comparison impractical.
- Advanced privacy-preserving computation only if the simpler data-minimizing design becomes insufficient.

## Product Opportunities Under Evaluation

### Core Outcome Improvements

- Improve match quality by combining multiple complementary signals.
- Keep signal dimensions separate enough that users understand why a match exists.
- Let users express what kind of similarity matters to them.

### Vision Expansion

- Evolve from interest-fingerprint matching toward privacy-preserving compatibility matching.
- Add optional values/worldview signals without requiring connected-platform data.
- Support useful matching for users with sparse or unavailable platform history.

### Adjacent User Needs

- Help users articulate their own values and preferences.
- Help users distinguish “shares my interests” from “likely compatible for friendship, collaboration, or discussion.”
- Support different connection intents without becoming a conventional dating/social network.

### Differentiation / Defensibility

- Privacy-preserving multi-signal matching.
- Explainable compatibility dimensions instead of an opaque single score.
- User-controlled weighting and selective disclosure.
- Data minimization as a product capability rather than only a policy statement.

### Simplification / Redesign

- Avoid pretending that one aggregate compatibility score is objectively correct.
- Remove signals that add sensitivity without meaningful predictive value.
- Prefer short, optional, progressive profiling over a mandatory long questionnaire.

### Experiments and Product Bets

- Prototype questionnaire completion and derived-dimension deletion behavior.
- Test whether users understand separate interest and values match explanations.
- Compare interest-only, values-only, and hybrid matching on synthetic or consented test profiles.
- Evaluate whether a questionnaire improves the empty-network/cold-start experience.

## Current Product Assumptions

- Interest similarity is useful enough to motivate connection.
- Privacy-conscious users will connect external sources when raw data is discarded.
- Derived fingerprints are acceptable if they are meaningfully protected and deletable.
- Off-platform contact is sufficient for the product’s current relationship model.
- More matching signals will improve outcomes only if users understand and control them.

## Assumptions That Should Be Challenged Next

- Interest similarity alone is the best proxy for compatibility.
- A MinHash interest fingerprint is sufficient differentiation.
- Users prefer passive data connection over explicit self-report.
- A single combined score is more useful than separate explainable dimensions.
- Values/worldview questions can be added without creating excessive sensitivity, bias, or false scientific authority.
- The product can claim better matching without a measurement strategy.

## Known Bugs / Risks

- `Fingerprint:Pepper` must remain stable for the lifetime of a deployment; loss or rotation invalidates stored signatures and requires reconnection.
- Historical state records conflict on the test total. Establish the current baseline.
- Questionnaire-derived values/worldview data may be more sensitive than interest fingerprints.
- Psychometric content may have licensing, validity, cultural-bias, and interpretation constraints.
- Matching on values/worldview may create filter bubbles or amplify exclusion if designed carelessly.
- Product language must not overclaim scientific accuracy or deterministically label users.

## Last Completed Release

### Release 1

Recorded as stable after multiple reviews and cold-start improvements.

The prior statement that “the autonomous mission’s completion criteria are satisfied” is retired. It is preserved only as a historical release-completion decision, not as authorization to stop the product mission.

## Last Completed Iteration

**Iteration:** Release 1 closeout

**Completed:** Known P0–P3 delivery and defect findings recorded at the time were shipped, deferred with rationale, or closed by explicit owner decision.

**Validation:** Historical state reports a successful release build, repeated tests, migrations, privacy checks, and a live core-journey review. The exact test total is inconsistent and must be re-established.

**Result:** Release 1 may be treated as complete. Product Opportunity Discovery is now active.

## Vision Evolution Log

### Previous Vision

Find people with similar interests from privacy-preserving fingerprints derived from connected online activity.

### Potential Better Vision

Help people find unusually compatible relationships using multiple consented, explainable, privacy-preserving signals such as interests, values, worldview, and connection intent.

### Evidence Needed

- Evidence that the added signal improves match usefulness or cold-start value.
- Evidence that users understand and want the signal.
- A data-minimizing design that does not undermine trust.
- Appropriate licensing and scientific framing.
- A clear explanation model that avoids false precision.
- Evidence that complexity is justified compared with improving interest-only matching.

## Resume Point (2026-07-24)

Signal 1 (connection intent) is complete and committed (HEAD `49a7199`). Baseline: **242 tests,
build 0 warnings, 12 migrations clean on a fresh DB.** Next: begin Signal 2 (values/worldview),
smallest increment above — one axis (openness↔conservation), own items, discard raw. This is a
larger slice; start it fresh. Nothing is uncommitted.

## Next Mandatory Action

1. Read `CLAUDE.md` and `docs/PRODUCT_AGENT.md`.
2. Re-run the current repository baseline and record the actual build/test state.
3. Use a strong Product Owner/research subagent to investigate the optional values/worldview questionnaire opportunity.
4. Use an independent Product Opportunity Critic to generate at least four additional materially different bets.
5. Compare the bets using user value, alignment, evidence, effort, risk, privacy, differentiation, and learning value.
6. Select the strongest justified next experiment or increment.
7. Update this file and `PRODUCT_LOG.md`.
8. Commit the coherent planning/discovery unit.
9. Begin implementation or prototyping immediately if justified.

Do not hand control back merely because Release 1 is clean.
