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
2. Connect a source (GitHub username, CSV, RSS, tokens) **or** pick/type interests at `/sources/interests` — zero accounts needed.
3. Fingerprint built; raw data + picks + tokens discarded (DB-asserted).
4. Ranked matches: tier (Good ≥15%, Strong ≥35%), shared source types, closest source, freshness, shared-interest reveal if opted in, intent line, outlook line. Filter by theme, sort Best / Same intent / Similar outlook. Never a blended score.
5. Optional: set intent, take values questionnaire (consent-gated, answers discarded), choose interests to show.
6. Read bio/contact, reach out off-platform. Hide or report a match.
7. Manage sources, profile, password, sessions, visibility, blocks, signals, export, deletion.

**Success criteria:** new user reaches real match list with credentials they actually have; raw interests never persisted; connector/fingerprint/empty/rate-limit/auth/deletion failures explained; privacy asserted by DB tests; build/tests/migrations/security healthy.

## 2. Baseline (2026-09-17)

- Branch `rebuild/dotnet-profiler` (all work). `origin/main` = `bb4e70b`, promoted by owner fast-forward only (push to `main` deploys). Local `main` is stale and unused. Last non-docs commit `bb4e70b` (2026-09-12, Azure deploy path); later commits are docs/config.
- `dotnet build -warnaserror` clean in Debug and Release (CI uses the flag). `dotnet test`: **349 passed, 0 failed** — baseline; a lower count blocks commit unless explained in `PRODUCT_LOG.md`. 16 migrations, auto-applied; integration suite boots on fresh DB.
- Commands: `dotnet build -warnaserror`, `dotnet test`, `dotnet run --project Profiler.Web`; QA server `profiler-web-qa` (:5241) via `.claude/launch.json`; deploy check `BASE=<url> MT=<Metrics:Token> ./deploy/smoke.sh` against a running container.
- Config knobs: `Fingerprint:Pepper` (required, permanent), `Metrics:Token`, `AntiAbuse:GuardRegistration` (+`MinFormSeconds`), `Signals:ValuesEnabled` (default true), `ForwardedHeaders:*`, `RateLimiting:*`.

## 3. Phase

**Release 2 complete + owner-gated.** Shipped and independently reviewed: multi-signal compatibility (intent, values, sort), funnel unblock (self-described + free-text interests, cross-pool language bridge, rarity weighting), tier recalibration, shared-interest reveal, trust & safety loop (report → operator review → reversible suspend), privacy-preserving anti-sybil, token-gated `/metrics`, a11y pass, containerized deploy (compose verified; Azure App Service workflow ready). Reviewer passes: Release Auditor ×3, Opportunity Critic ×2, all clean or findings fixed.

## 4. Active Task

**None active. Stopped under `CLAUDE.md` §10 condition 3** — every remaining bet is owner-gated (§5, §6) and evidence-independent work is exhausted. Docs consolidation + optimisation completed 2026-09-17 (`fca4d0c`, `1feb8bf`, `ebb481c`, and the optimisation commit). Product Owner review after it: last evidence-independent candidate (music/film genre bridge) rejected as low value before a pool exists — fuzzy mapping with no density to validate against. No new assumption test or structural inspection identified that real usage would not answer better.

**Exact resume action:** on any owner reply to `docs/OWNER_DECISIONS.md` (even one decision), implement it immediately, log it in `PRODUCT_LOG.md`, close the brief entry, resume the loop. Recommended first reply: Decision 4 (go live) — it unblocks the evidence every other bet needs. Owner-only steps in that decision (`az login`, bootstrap, repo secrets, `main` promotion) are the owner's to run; the loop prepares and verifies everything else.

## 5. Owner-Gated Decisions → `docs/OWNER_DECISIONS.md`

1. Promote vision to "privacy-preserving compatibility matching" (recommend yes).
2. Values signal: keep / **hide by default** (recommended; one flip `Signals:ValuesEnabled=false`) / strengthen with 2nd Schwartz axis (only with real adoption evidence).
3. Enable `AntiAbuse:GuardRegistration=true` for public launch (recommend yes).
4. Go live on Azure: owner runs `deploy/azure-bootstrap.sh` under own `az login`, sets repo variable `AZURE_WEBAPP_NAME` + secret `AZURE_WEBAPP_PUBLISH_PROFILE`, makes GHCR package public. Unblocks real `/metrics` evidence, which every further signal bet depends on.

Also owner-only (standing): opt-in contact model stays final; culling paste-a-token connectors (14/18) — security + honesty win but removes advertised capability.

## 6. Backlog

- **P0** — none.
- **P1** — none evidence-independent. Gated: second values axis (evidence + owner); connector-side rarity weighting (needs fingerprint-scheme versioning design first — `SchemeVerifier` covers pepper only, so old/new connector signatures would silently stop matching; plus privacy-safe common-feature source); community-scoped pools (needs real community partner).
- **P2** — extend canonical bridge to music/film genres (fuzzy multi-vocabulary mapping); shared single-use ticket cache if ever multi-instance (in-process today; single worker on App Service anyway).
- **P3** — interest clusters / community formation (density-gated); opt-in Web Push return channel (needs pool); privacy-respecting CAPTCHA option (provider); client-side fingerprinting for self-describe path (pepper-on-client problem, premature).
- **P4** — LSH banding past few thousand users; pagination; match-card redesign closed as not warranted after worst-case walk.

## 7. Opportunities Under Evaluation (ranked)

1. Real usage evidence via `/metrics` — gate: deployment (Decision 4).
2. Hide values signal by default — gate: owner (Decision 2); mechanism built.
3. Connector-side weighting — gate: scheme-versioning design; consequential migration.
4. Interest clusters — gate: pool density.
5. Provable "raw discarded" (re-derive-your-own-fingerprint tool; long-term client-side compute) — right moat, premature before pool exists.

## 8. Assumptions

| Assumption | Status |
|---|---|
| 128-hash MinHash tracks true Jaccard | Tested: MAE 0.006 (`InterestSignalResolutionTests`) |
| Tier cut-offs reachable + discriminating | Tested; recalibrated Good ≥15 / Strong ≥35 (same tests) |
| Single openness axis discriminates | Tested: weak, 95% in −1..+1 (`ValuesSignalResolutionTests`) |
| Outlook sort is worth its reorder | Tested: ~66% top-1 change from weak signal (`ValuesSortImpactTests`) → owner brief |
| Rarity weighting separates niche from common | Tested: 6.4× (`InterestWeightingExperimentTests`) → shipped self-described side |
| Self-described + connector users can match | Tested for languages (bridge integration test); genres unbridged |
| Interest similarity motivates real outreach | Untested — needs live users |
| Privacy-conscious users will connect sources / self-describe | Untested — needs `/metrics` adoption counts |
| Users understand separate signals vs one score | Untested — needs live users |

## 9. Known Risks

- `Fingerprint:Pepper` rotation wipes every signature (detected at startup, users must reconnect). Back it up.
- SQLite: App Service must stay at one worker; bootstrap pins it. Scaling out corrupts.
- Registration ticket single-use cache is in-process (fine single-instance).
- Values signal = most sensitive data, least validated signal; default still on pending Decision 2.
- F1 tier sleeps + 60 CPU-min/day; `az appservice plan update ... --sku B1` upgrades in place.
- 14/18 connectors require self-minted tokens (phishing-shaped UX); mitigated by self-described path.

## 10. Last Completed Iteration

`bb4e70b` 2026-09-12 — Azure App Service deploy path: `deploy/azure-bootstrap.sh` (RG, Linux plan F1, container app, persistent `/home` storage, forwarded-headers with platform networks, single worker, prints pepper/token/publish profile) + `deploy-azure` job in `deploy.yml` (sha-pinned image, polls URL until 200, skipped until `AZURE_WEBAPP_NAME` set).

## 11. Deploy Status

- **Compose (local/VPS):** verified 2026-08-08 — production image boots, migrations apply, `deploy/smoke.sh` 16/16, data + key ring survive restart, register rate limit fires. Secrets in gitignored `deploy/.env.production`.
- **GHCR:** every push to `main` / `v*` tag builds + publishes `ghcr.io/kashiashvili/effective-octo-giggle`. Needs repo Actions permission "Read and write".
- **Azure:** workflow present, skipped until owner completes Decision 4 steps. Not yet live.
