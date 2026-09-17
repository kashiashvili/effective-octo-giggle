# Project State

> Authoritative operational memory for the autonomous loop. **Current state only, ≤150 lines.**
> History → `PRODUCT_LOG.md` §11. Rules → `CLAUDE.md`. How-to → `docs/PRODUCT_AGENT.md`.
> Clean release = milestone, not mission end.

## 1. Product

**Profiler** — privacy-preserving social compatibility matching. User connects platforms or self-describes interests; app derives MinHash fingerprint, discards raw data, matches similar fingerprints. Optional separate, explainable signals: connection intent, coarse values/outlook, opt-in shown interests.

**Vision (current, owner-approved):** help privacy-conscious people find others who genuinely share their interests without surrendering raw personal data. "Raw data never stored" = product constraint.

**Vision hypothesis (built, awaiting owner promotion — Decision 1):** privacy-preserving *multi-signal compatibility* — interests + intent + values, consented, user-controlled, explainable, data-minimizing, no clinical claims, no raw retention. Implemented under all constraints; framing change is owner's call.

**Target user:** privacy-conscious person seeking like-minded people around niche interests, values, or intent; refuses to hand raw activity to another network.

**Core problem:** matching products demand raw behavior, detailed profiles, sensitive answers. Profiler retains only minimum derived data.

**Value prop:** find your people without surrendering your raw data.

**Core journey:**
1. Register (username + password, recovery code shown once) → auto sign-in.
2. Pick/type interests at `/sources/interests` (zero accounts; recovery-code continue and landing lead here) **or** connect a source (GitHub username, Goodreads/Netflix/YouTube-Takeout export, RSS; the rest need self-made tokens).
3. Fingerprint built; raw data + picks + tokens discarded (DB-asserted).
4. Ranked matches: tier (Good ≥15%, Strong ≥35%), shared source types, closest source, freshness, "in their top matches too" badge, "Same circle" chip, shared-interest reveal if opted in, intent line, outlook line. Filter by theme, sort Best / Same circle / Same intent / Similar outlook. Never a blended score.
5. Optional: set intent, take the values & worldview questionnaire (ten values + two world beliefs, consent-gated, answers discarded, own profile shown back in words), choose interests to show.
6. Read bio/contact, reach out off-platform. Hide or report a match.
7. Manage sources, profile, password, sessions, visibility, blocks, signals, circles (start/invite/leave; circle page shows every member), export, deletion.

**Success criteria:** new user reaches real match list with credentials they actually have; raw interests never persisted; connector/fingerprint/empty/rate-limit/auth/deletion failures explained; privacy asserted by DB tests; build/tests/migrations/security healthy.

## 2. Baseline (2026-09-17)

- Branch `rebuild/dotnet-profiler` (all work). `origin/main` = `bb4e70b`, promoted by owner fast-forward only (push to `main` deploys). Local `main` is stale and unused. Last product commit: values audit fixes (2026-09-17; `0fbc35c` the feature, `bde7957`+`9a8fd3b` accessibility).
- `dotnet build -warnaserror` clean in Debug and Release (CI uses the flag). `dotnet test`: **400 passed, 0 failed** — baseline; a lower count blocks commit unless explained in `PRODUCT_LOG.md`. 18 migrations, auto-applied; integration suite boots on fresh DB.
- Commands: `dotnet build -warnaserror`, `dotnet test`, `dotnet run --project Profiler.Web`; QA server `profiler-web-qa` (:5241) via `.claude/launch.json`; deploy check `BASE=<url> MT=<Metrics:Token> ./deploy/smoke.sh` against a running container.
- Config knobs: `Fingerprint:Pepper` (required, permanent), `Metrics:Token`, `AntiAbuse:GuardRegistration` (+`MinFormSeconds`), `Signals:ValuesEnabled` (default true), `Signals:CirclesEnabled` (default true), `Backup:Directory` (+`Keep` 7, `IntervalHours` 24; image + Azure set it, dev/tests off), `Build:Sha` (CI-stamped, footer), `ForwardedHeaders:*`, `RateLimiting:*` (incl. `CirclesPermitLimit`).

## 3. Phase

**Release 2 complete + owner-gated.** Shipped and independently reviewed: multi-signal compatibility (intent, values, sort), funnel unblock (self-described + free-text interests, cross-pool language bridge, rarity weighting), tier recalibration, shared-interest reveal, trust & safety loop (report → operator review → reversible suspend), privacy-preserving anti-sybil, token-gated `/metrics`, a11y pass, containerized deploy (compose verified; Azure App Service workflow ready). Reviewer passes: Release Auditor ×3, Opportunity Critic ×2, all clean or findings fixed.

## 4. Active Task

**Values audit fixes — built, tests green (400), committing (2026-09-17).** Release Auditor on the values rebuild returned seven P2s (unique-top-priority guard, per-breakdown metrics gating, legend/aria on the questionnaire, answers kept on a validation error, flag gating on the profile and landing surfaces, the primals-and-politics overclaim, the promised database scan) and P3s; all closed.

**Next mandatory action:** Product Owner review of the values work, then a QA re-walk of the questionnaire (legend and answer-retention changes are untested in a browser) and the next bet from §7 — item 4, try-before-register preview, needs a design note first. Owner replies to decisions 1, 3, 4, 5 pre-empt everything.

## 5. Owner-Gated Decisions → `docs/OWNER_DECISIONS.md`

1. Promote vision to "privacy-preserving compatibility matching" (recommend yes).
2. ~~Values signal~~ **answered 2026-09-17 by the owner: strengthen.** Shipped as `schwartz-v2` (four centred priorities + two world beliefs, accessible surfaces).
3. Enable `AntiAbuse:GuardRegistration=true` for public launch (recommend yes).
4. ~~Go live on Azure~~ **DONE 2026-09-17** — live on B1 (owner approved the upgrade after F1 hit its daily CPU quota). `/metrics` now has a real deployment behind it; adoption evidence starts accruing, which unblocks the gated signal bets.
5. Groups-first positioning once circles ship (copy only; recommend yes). Design: `docs/DESIGN_CIRCLES.md`.

Also owner-only (standing): opt-in contact model stays final; culling paste-a-token connectors (13/18) — security + honesty win but removes advertised capability.

## 6. Backlog

- **P0** — none.
- **P1** — none evidence-independent. Gated: second values axis (evidence + owner); connector-side rarity weighting (needs fingerprint-scheme versioning design first — `SchemeVerifier` covers pepper only, so old/new connector signatures would silently stop matching; plus privacy-safe common-feature source); community-scoped pools (needs real community partner).
- **P2** — shared single-use ticket cache if ever multi-instance (in-process today; single worker on App Service anyway); off-host snapshot shipping (needs a destination = owner credentials → gated).
- **P3** — interest clusters / community formation (density-gated); opt-in Web Push return channel (needs pool); privacy-respecting CAPTCHA option (provider); client-side fingerprinting for self-describe path (pepper-on-client problem, premature).
- **P4** — LSH banding past few thousand users (a page now makes ≤21 passes: the list plus one counting pass per match); pagination; match-card redesign closed as not warranted after worst-case walk.

## 7. Opportunities Under Evaluation (ranked; Critic #4 2026-09-17 evening — "day coherent, nothing to revert")

1. **Circle view** — shipped `394fbb5` (members see every discoverable member, honest labels below the floor, no top-20 cut; global list untouched).
2. **Vision revision: introductions inside groups/gatherings you already belong to** — Decision 5 (owner, copy). Increments 1, 3, 4 are ungated.
3. **Organiser circle-health card** — shipped (members · with fingerprint · good-match pairs from five members, counts only).
4. **Try-before-register preview** — anonymous picker → lens + coarse band → register to see who. Gate: none, design for the oracle (band, pool ≥10, per-IP limit). M.
5. **Fold token connectors** — shipped (five no-token cards up front on the landing; token groups folded on both pages).
6. Retire "Same circle first" sort — deferred: harmless, cheap, and removing shipped UI without usage evidence buys nothing; revisit with `/metrics`.
7. Persist 9-theme lens counts ("your interest shape", circle themes ≥5) — new stored derived data → new-data checklist. Gate: design.
8. Web Push return channel — gate: pool + owner (push relays vs "no trackers").
9. Real usage evidence via `/metrics` — gate: deployment (Decision 4). Further values axes — gate: real adoption evidence (Decision 2 closed: strengthened). Connector-side weighting — scheme versioning.

## 8. Assumptions

| Assumption | Status |
|---|---|
| 128-hash MinHash tracks true Jaccard | Tested: MAE 0.006 (`InterestSignalResolutionTests`) |
| Tier cut-offs reachable + discriminating | Tested; recalibrated Good ≥15 / Strong ≥35 (same tests) |
| Values profile discriminates (v2) | Tested: all five levels used per dimension; strangers 16.7% "similar" vs 69% for shared-priority pairs (`ValuesSignalResolutionTests`) |
| Centring removes scale use | Tested: generous vs stingy rater, same priorities, read similar 76.1% (`ValuesSignalResolutionTests`) |
| Outlook sort is worth its reorder | Tested: ~91% top-1 change, now from a signal that discriminates (`ValuesSortImpactTests`) |
| Rarity weighting separates niche from common | Tested: 6.4× (`InterestWeightingExperimentTests`) → shipped self-described side |
| Self-described + connector users can match | Tested for languages and twelve common genres (bridge integration tests); niche genres unbridged by design |
| Interest similarity motivates real outreach | Untested — proxy live via `/metrics` `returnedAfterFirstDay`, `withContact` |
| Privacy-conscious users will connect sources / self-describe | Untested — live via `/metrics` `withFingerprint`, `fingerprintsBySource` |
| Users understand separate signals vs one score | Untested — needs live users |

## 9. Known Risks

- `Fingerprint:Pepper` rotation wipes every signature (detected at startup, users must reconnect). Back it up.
- Database snapshots live on the same volume as the database: they cover bad migration/release/data damage, not loss of the volume. Off-host copy is a manual owner step (README "Back up and restore"). Any snapshot older than Keep×Interval (7) days is deleted on every service pass (disclosed in-product); pre-migration copy fails closed.
- SQLite on App Service `/home` is an SMB share: keep the default rollback journal (never WAL — needs shared memory, breaks on network filesystems); single worker already pinned.
- SQLite: App Service must stay at one worker; bootstrap pins it. Scaling out corrupts.
- Registration ticket single-use cache is in-process (fine single-instance).
- Values & worldview = the most sensitive data collected. Now the validated-construct version (`schwartz-v2`), answers discarded, six integers stored, one flip from hidden. Item wording is ours and unvalidated: it is not a psychometric assessment and must never be described as one.
- F1 tier sleeps + 60 CPU-min/day; `az appservice plan update ... --sku B1` upgrades in place.
- 13/18 connectors require self-minted tokens (phishing-shaped UX); mitigated by self-described path + no-token exports (Goodreads, Netflix, YouTube Takeout).

## 10. Last Completed Iteration

`d48979e` organiser health card, 2026-09-17. Same day, newest first: `e5efb6f` audit fixes (join copy, column-level inventory, one member count, red fold assertion); `8a1ab42` token fold; `394fbb5` circle page; `227d268` privacy inventory; `8dd315c`+`96429b7` mutual badge; `9231952` circle invite follow-through; `d7002bc`+`6dc5527`+`ff03acc` circles; `d1bbe1c` genre bridge; `58baf17` YouTube Takeout; `9e0f578`+`9c9ff96`+`e6a0fcb`+`ab80929` go-live safety, metrics, onboarding.

## 11. Deploy Status

- **Compose (local/VPS):** re-verified 2026-09-17 — rebuilt image boots, container reports `healthy` (new bash `/dev/tcp` probe on `/health`), startup snapshot written to `/data/backups` as `app`, `deploy/smoke.sh` 16/16, snapshot copied off via `docker cp` passes `PRAGMA integrity_check` with all 16 migrations, restart adds no duplicate snapshot, privacy page shows the 7-day retention line; restore drill with the README command verbatim rolled 1 user back to 0, `/health` 200. Secrets in gitignored `deploy/.env.production`.
- **GHCR:** every push to `main` / `v*` tag builds + publishes `ghcr.io/kashiashvili/effective-octo-giggle`. Needs repo Actions permission "Read and write".
- **Azure: LIVE (2026-09-17).** https://profiler-demo-f146591f.azurewebsites.net — `profiler-rg` / plan `profiler-plan` / app `profiler-demo-f146591f`, **B1 Basic** (owner-approved, ~$13.14/mo), single worker, Always On, health check `/health`, DB + key ring + snapshots on persistent `/home/data`. `deploy/smoke.sh` **16/16 against the live URL**. Two traps hit and fixed, both in `deploy/azure-bootstrap.sh`: (1) F1 free tier died of `QuotaExceeded` (60 CPU-min/day) with a per-minute `/health` probe against it — health check is now paid-tier-only, and F1 proved unusable for this container; (2) publish-profile deploys failed with `Failed to get app runtime OS` because Azure creates apps with `basicPublishingCredentialsPolicies` disabled — bootstrap now enables SCM basic auth (FTP stays off; revert command documented if moving to OIDC). Bootstrap is also now safe to re-run: it reuses an existing pepper/token instead of rotating (rotating would invalidate every fingerprint).
