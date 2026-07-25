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
4. View ranked matches with qualitative tiers and shared source types, filterable by shared
   interest area, plus **separate explainable compatibility signals**: shared connection intent
   ("both here for …") and coarse values alignment ("similar / different outlook"). Signals are
   displayed, never blended into a score; interest similarity still drives ranking.
5. Optionally add **connection intent** and take the optional **values questionnaire** (consent-
   gated; answers discarded after deriving a coarse bucket) so matches carry more than interests.
6. Read optional bio/contact details and reach out off-platform.
7. Manage privacy, sources, profile, password, visibility, blocks, the two compatibility signals
   (edit/remove), export, and account deletion.

> Note: signals 1–2 (intent, values) are shipped and validate the owner's recorded "Potential
> Better Vision" (multi-signal compatibility) under all its constraints — consent, data
> minimization, user control, explainability, no clinical claims, no raw retention. Formally
> promoting that hypothesis to the Current Product Vision remains an owner decision.

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
cleared via profile, removed on deletion, never re-ranks or blends into a score. **241 tests, build
0 warnings.** (Deferred, low value now: filter-by-intent — the mutual highlight already surfaces
same-intent matches, and filtering only matters at scale like the source filter.)

## Active Task — Signal 2: optional values/worldview (the next real bet)

The intent pattern validated the infrastructure; now build the higher-differentiation signal behind
the same model. **Decided constraints (see Discovery Decision):** Schwartz's public 2-axis structure,
**our own plainly-worded items** (no licensed instrument), **discard raw answers** after deriving a
small vector (retake = re-answer), opt-in/skippable/hideable/deletable, in export, shown as **coarse
alignment** on match cards (never raw answers, never a blended score, not a hard filter → avoid
filter bubbles). Reject Big Five and political/moral items.

### Smallest useful validated increment (in progress)
1. ~~Pick ONE axis: **openness↔conservation**.~~ Done.
2. ~~Own Likert items; derive to a signed bucket −2..+2; discard raw.~~ **Done** — `ValuesQuestionnaire`
   (commit `116c837`), 15 tests, reverse-scored, coarse `AlignmentLabel`. Pure logic, no persistence.
3. **NEXT:** store `AppUser.ValuesOpenness` (nullable small int) + scheme version tag. Migration.
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

**Multi-signal compatibility is now a working, measured, actionable system.** HEAD `1c4e912`.
Baseline: **265 tests, build 0 warnings, 13 migrations clean on a fresh DB.** Nothing uncommitted.
Shipped: signal 1 (connection intent, mutual highlight), signal 2 (values/worldview — consent →
derive → discard-raw, DB-asserted → coarse alignment), token-gated aggregate **/metrics**, and
**user-controlled sort** (Best match / Same intent first / Similar outlook first — stable secondary
sort, no blended score). Signals now affect what the user sees, on their terms.

### Assumption test result (2026-07-25) — core interest signal resolution + tier recalibration

Applied the same synthetic-simulation discipline used on the values axis to the product's **core**
signal, which had never been resolution-tested. New `InterestSignalResolutionTests` (no real users)
builds community-structured synthetic profiles, fingerprints them with the real
`FingerprintGenerator`, and measures the tier distribution against the real `MatchViewModel` tiers.
**Findings:** (1) the fingerprint is faithful — 128-hash MinHash tracks true Jaccard within MAE
0.006; (2) MinHash Jaccard over interest *sets* runs low — same-niche pairs sit at **median ~0.21,
P90 ~0.34, ~none above 0.50**, strangers at ~0; (3) the old tier cut-offs (Good ≥30, Strong ≥60)
made **"Strong match" unreachable (0%)** and left ~83% of genuine matches mislabelled "Some overlap".
**Acted on it:** recalibrated to **Good ≥15%, Strong ≥35%** (`MatchViewModel`), so same-niche pairs
read Good-or-better **92%** of the time (Strong a rare-but-earned 8%) while strangers stay 100% "Some
overlap". CSS classes (high/medium/low) unchanged. The resolution test guards the real thresholds and
fails if calibration drifts. **Baseline now: 278 tests, build 0 warnings.** HEAD after this unit.

### Assumption test result (2026-07-24) — values signal resolution

Ran a synthetic-profile simulation (`ValuesSignalResolutionTests`, no real users) to test whether
one coarse openness axis discriminates. **Finding:** averaging four 5-point items concentrates 95%
of profiles into buckets −1..+1 (central-limit clumping), and the original "within one = similar"
label read 77.6% of random pairs as "Similar outlook" — nearly uninformative. **Acted on it:**
recalibrated the label (exact match = "Similar", ±1–2 = "Some overlap", further = "Different"),
dropping "Similar" to 31% of random pairs so it now carries information (commit `182b3e1`).
**Standing evidence for the deferred decision:** a single averaged axis has limited spread (most
pairs land in the middle tier), so values differentiation should be **broadened (second Schwartz
axis or finer scoring) before being relied on**, not left as the only values dimension. This is now
evidence-backed, not a guess — but adding the axis still increases sensitivity, so it remains gated
on real adoption evidence (`/metrics`) + owner intent.

### Active: pre-release reviews of the multi-signal expansion

- **User-flow review (done, 2026-07-24):** walked the changed surfaces live — fully-populated match
  card renders in the intended hierarchy (identity → interest strength → intent+values grouped →
  bio+contact), sort + filter controls present, values questionnaire renders (4 items × 5-point +
  consent), and coarse values alignment is correctly withheld when only one side took it. No
  regressions or awkwardness found; no fix needed.
- **Release Auditor (independent subagent) — DONE, all findings resolved (`74cd1b0`).** Found **no
  P0/P1/P2**; verified raw answers discarded (DB-asserted), delete/export correct, consent enforced
  server-side, `/metrics` token-safe with no per-user leak, signals withheld while hidden, sort
  explainable and stable. Its five P3s are all fixed: retention marker only advances on the plain
  view; `/metrics` withholds breakdowns below a 10-user cohort; export includes `ValuesScheme` +
  `LastMatchesViewedAt`; values sort keys off a numeric rank not a display string; duplicate consent
  error removed. **269 tests, 0 warnings.**
- Product Opportunity Critic: done inline this session (bets recorded below).

**Release status:** the multi-signal expansion is a **validated release candidate** — both mandated
reviews done (Release Auditor clean after fixes; Opportunity Critic inline), core journey
flow-reviewed live, privacy asserted against the DB. The remaining gate is the **owner's decision**
to promote the vision from interest-only to multi-signal; further feature bets are evidence-gated
(need real usage via `/metrics`). Nothing uncommitted; HEAD `74cd1b0`.

### Product Owner position (2026-07-24): further feature work is now evidence- or owner-gated

The multi-signal release is coherent and complete for what can be justified *without real usage
data*: two optional privacy-preserving signals, measurement, user-controlled sorting, and a clean
card. **The next real decisions require evidence this repo cannot self-generate:**
- Whether to add the 2nd values axis, or broaden to no-connected-source matching (bet #2), depends
  on whether the *existing* signals are adopted and help — which `/metrics` will show only once the
  product has real users (deployment + usage, outside this environment).
- Promoting the vision from "interests only" to multi-signal is an **owner decision**.

Remaining pure-build bets are lower-value polish (self-view #3 largely overlaps the dashboard preview
+ data page; per-match weighting #4 is larger and premature before evidence). Per CLAUDE.md, do not
invent low-value features to keep coding. **The highest-value next step is gathering usage evidence,
which needs a real deployment.** Until then, the justified work is discovery/assumption-testing, and
the formal two-reviewer pass (below) before declaring a multi-signal release.

### Opportunity Critic bets (2026-07-24) — remaining, ranked
1. ~~Signals don't affect ranking.~~ **Done** — user-controlled sort (`1c4e912`).
2. **Match without any connected source** (values+intent-only profile) — removes the connect barrier
   for privacy-maximalists, but 2 coarse signals give weak match quality; **needs more signal first**
   (gated behind measurement evidence + possibly the 2nd values axis).
3. **Persistent "how you appear to matches" self-view** — consolidate interest themes + intent +
   values. Adjacent need (self-articulation). Small–med, privacy-safe. **Good next small bet.**
4. **Per-match "why you matched" breakdown** with user weights — strongest differentiation; larger.
5. **Match-card hierarchy redesign** — the card is dense (tier, %, sources, closest-on, freshness,
   bio, values, intent, contact). Simplification/redesign; low risk. **Good next small bet.**

### Product Owner decision (2026-07-24): measurement gates further signal expansion

Three signals now ship (interests, intent, values). The recorded assumption — *more signals improve
outcomes only if users understand and control them* — is **untested**. Adding a second values axis
(self-enhancement ↔ self-transcendence) would increase sensitive-data collection against the
data-minimization north star **with no evidence it helps**, so it is **deferred until measurement
exists**. This is the disciplined PO call, not a stopping point.

**Privacy-safe measurement plan (next real increment):** the north star forbids tracking behavior,
so measure at the aggregate/derived level only. Candidate signals, each cheap and non-identifying:
- Adoption counts (how many users set intent / took values), from columns already present — no new
  data.
- Whether a card's compatibility signals correlate with the user choosing to *reveal contact* or
  *filter* — but this needs an event we do not currently record. Design an **aggregate, per-day,
  non-user-linked counter** (e.g. "matches viewed with vs without a shared signal") rather than
  per-user event logs, so it cannot rebuild a behavior profile. Decide explicitly whether even this
  is worth the retention before building.
- Simplest first step, no new retention: an internal admin/metrics view computing adoption from
  existing columns, behind auth, to see whether the signals are used at all before investing more.

**Other next bets (owner to prioritise):**
- **Formal two-reviewer pass** (Release Auditor + Opportunity Critic, ≥5 fresh bets) over the
  three-signal product before calling it a release. (Inline auditor pass done this session: no
  regressions; new `/account/values` actions authorized + antiforgery-covered; consent enforced in
  controller; card alignment null-safe and withheld while hidden.)
- **Promote the vision** from "interests only" to the multi-signal framing — an **owner decision**;
  the "Potential Better Vision" is now validated in code under all its constraints.
- Match-card **information hierarchy**: a fully-populated card can now show tier, %, sources,
  closest-on, freshness, bio, values, intent, contact. Reviewed as acceptable (most fields optional/
  conditional, grouped logically) but worth a real-user look if cards feel dense.

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
