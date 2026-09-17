# Owner Decision Brief

_Prepared by the autonomous product loop, 2026-07-25. Updated 2026-09-17 (Azure path added 2026-09-12; Decision 5 added; counts re-verified). All five decisions still open._

The build/validate/deploy work that can be justified **without owner strategy input or real usage
data** is done: multi-source matching (interests + intent + values), self-described + free-text
interests, cross-pool bridging, rarity weighting, evidence-recalibrated tiers, the shared-interest
reveal, the full trust-&-safety loop (report → operator review → reversible suspend), privacy-preserving
anti-sybil, token-gated measurement, and a containerized deploy pipeline on `main`. Three independent
Release Audits and two Opportunity Critics; 385 tests, 0 warnings (re-verified 2026-09-17).

What remains needs **you**. Each decision below has the evidence, the options, a recommendation, and
what it unblocks. None require reading code — the loop can execute whichever way you decide.

---

## Decision 1 — Promote the vision to "privacy-preserving compatibility matching"?

**Context.** The recorded vision is *interest* matching. What's shipped is multi-signal: interest
similarity **drives ranking**, with connection intent and a values/outlook bucket shown as **separate,
explainable** lines (never a blended score). The "Potential Better Vision" is now implemented under all
its recorded constraints — consent, data minimization, user control, explainability, no clinical claims,
no raw retention.

**Evidence.** All three signals are shipped, integration-tested and independently audited (no P0–P2);
the privacy constraints are asserted against the database. No real users yet, so adoption of the extra
signals is unmeasured — `/metrics` will show it once live.

**Options.** (a) Promote the framing to "compatibility matching." (b) Keep "interest matching" and treat
intent/values as optional add-ons.

**Recommendation: (a), with honesty.** The multi-signal product exists and is coherent. Promote the
framing — but keep interest similarity as the ranking core, and don't over-sell "values" (see Decision
2). Low risk: it's positioning that matches what's built.

**Unblocks.** Landing-page/positioning copy; whether further signal investment is on-vision.

---

## Decision 2 — The values signal: keep · hide-by-default · strengthen?

This is the most consequential call, because it's the one place the product collects **sensitive
worldview data**, and the evidence says the signal is weak.

**Evidence (from in-repo simulations, no real users):**
- **Low resolution.** Averaging four 5-point items is a central-tendency machine: **95% of people land in
  the −1..+1 buckets** (`ValuesSignalResolutionTests`). The coarse "Similar / Some overlap / Different"
  label was already recalibrated so "Similar" isn't vacuous, but the axis is inherently coarse.
- **High-impact, low-quality sort.** Its main user-facing use — the "Similar outlook first" sort —
  **reorders the match list heavily** (≈66% of viewers get a different top match; `ValuesSortImpactTests`).
  Because outlook and interest are independent, opting in lets a **weak signal heavily override the
  strong interest ranking**.
- **Cost.** It is the *most sensitive* data collected, for the *least validated* signal, which sits
  against the data-minimization north star.

**Options.** (a) Keep as-is. (b) **Hide by default** (reversible) until it earns its place. (c) Strengthen
with a second Schwartz axis (self-enhancement ↔ self-transcendence) — but that *increases* sensitive-data
collection and is only worth it with real adoption evidence.

**Recommendation: (b) hide by default.** Don't collect worldview data speculatively. It's reversible, it
tightens the privacy story, and it removes a sort that currently trades away interest quality for a noisy
signal. Revisit (c) only if real usage shows people want an outlook dimension. **On your "yes" the loop can
ship a one-setting switch** (`Signals:ValuesEnabled=false`) that hides the questionnaire, the card line,
and the sort — keeping the code and any stored buckets for a clean re-enable.

**Unblocks.** The data-minimization posture for launch; whether to build the second axis.

---

## Decision 3 — Turn on the registration guard for public launch?

**Context.** Sign-up is username + password only. A privacy-preserving anti-sybil layer is **built and
tested** (honeypot + signed single-use form ticket; no third-party CAPTCHA, no PII), **off by default** so
dev/tests are undisturbed.

**Evidence.** Six unit tests plus `deploy/smoke.sh` confirm the honeypot rejects and the ticket is enforced
when on; the per-IP register limit was seen returning 429 in the live compose run. Cost of leaving it off:
nothing stops a scripted sign-up loop beyond the rate limit.

**Options.** (a) Leave off (private cohort only). (b) Enable for public launch. (c) Enable plus a
privacy-respecting CAPTCHA in front for very-high-value protection.

**Recommendation: (b) — enable it for a public launch** — set `AntiAbuse:GuardRegistration=true` (optionally
`AntiAbuse:MinFormSeconds`). The per-IP register rate limit is the always-on cap regardless. Trivial flip.

**Unblocks.** Opening registration to the public.

---

## Decision 4 — Go live (unblocks everything data-gated)

**Context.** Every remaining *product* bet — connector-side rarity weighting, a second values axis, interest
**clusters**, a return channel — is gated on **real usage evidence**, which only a live deployment with
real users produces (surfaced privately via token-gated `/metrics`). The loop cannot generate that here.

**Go-live checklist (all documented in `README.md`):**
1. Repo → Settings → Actions → Workflow permissions → **Read and write** (so the deploy job can push to GHCR).
2. Set a real **`Fingerprint:Pepper`** (e.g. `openssl rand -base64 32`) and keep it for the deployment's life.
3. Run the image with a **persistent `/data` volume** (SQLite db + Data Protection keys).
4. Set **`Metrics:Token`** (operator/moderation access) and, for public launch, **`AntiAbuse:GuardRegistration=true`**.
5. Front it with **TLS** and set the **forwarded-headers** options so rate limiting / HTTPS see the real client.
6. Copy the app's rolling database snapshots (`/data/backups`, on Azure `/home/data/backups`) off the host
   now and then — the volume is the only copy (`README.md` "Back up and restore").
7. Start a **circle** for the cohort first (dashboard → "Start a circle") and share *its* invite link instead of
   the bare register link: members see each other flagged and can sort each other first.
8. Onboarding a group from one network (an office, a meetup's Wi-Fi)? The register limit is **5 per IP per
   hour** (`RateLimiting:RegisterPermitLimit`); the sixth person sees "Too many sign-up attempts" for an hour.
   Raise it for the session (`az webapp config appsettings set … RateLimiting__RegisterPermitLimit=50`, restart)
   and put it back afterwards.

**Evidence.** Compose deployment verified 2026-08-08: production image boots, migrations apply, smoke test
16/16, data and key ring survive a restart. The Azure workflow is present and skipped until the repo variable
exists, so it cannot fire by accident.

**Options.** (a) Azure App Service via the bootstrap script (chosen path, below). (b) Any Docker host running
the compose stack behind TLS. (c) Stay private/local and gather no evidence.

**Recommendation: (a), starting on the free F1 tier with a small private cohort.** Reversible: the plan
upgrades in place and the data lives on the persistent volume.

**Azure path (chosen 2026-09-12): App Service Linux container, publish-profile auth, GHCR image.**
Run `./deploy/azure-bootstrap.sh` once (it needs your `az login`); it creates everything, sets every
app setting, and prints the pepper, the operator token, and the publish profile. Then set the repo
variable `AZURE_WEBAPP_NAME` + secret `AZURE_WEBAPP_PUBLISH_PROFILE`, make the GHCR package public, and
the `deploy-azure` job in `.github/workflows/deploy.yml` ships every push to `main`. Starts on the free
F1 tier ($0; sleeps when idle, 60 CPU-min/day); `az appservice plan update -g <rg> -n <plan> --sku B1`
(~$13/mo) upgrades in place with no redeploy and no data loss.

**Unblocks.** Real adoption data → the evidence Decisions 2(c) and the data-gated bets depend on.

---

## Decision 5 — Position the product groups-first ("find your people inside communities you already belong to")?

_Updated: 2026-09-17_

**Context.** Opportunity Critic #3 (2026-09-17) found the vision framed as a standalone network of
individuals while every remaining bet waits on pool density, and Decision 4 assumes "a small private
cohort" with no mechanism to form one. The loop is building **circles** — a named group with a
shareable invite link; members see a "Same circle" chip and can sort by it; registration stays
open, matching stays global, nothing is filtered or blended (design: `docs/DESIGN_CIRCLES.md`).
That is additive and needs no decision. What needs you is the **positioning**: whether the landing
page, the pitch and the go-live plan lead with "host a circle for your meetup / course / team"
rather than "join and get matched".

**Evidence.** No real users. Structural: the self-described picker became the real funnel; the
invite link is the only growth lever; all density-gated bets are unchanged for a year of building.

**Options.** (a) Groups-first positioning: landing leads with "start a circle", go-live plan = one
organiser per cohort. (b) Keep individual positioning; circles stay a feature. (c) Wait for the first
cohort's `/metrics` to show whether circles are used.

**Recommendation: (a), after the increment ships.** It matches how the first cohort will actually
arrive (through someone), and it is copy, not code — reversible in an hour.

**Unblocks.** Landing/positioning copy; whether to build circle-scoped extras (member list to members,
"N in your circle" on the empty state, invite-only mode).

---

## Recommended sequence

1. **Decision 3 + Decision 4** — deploy privately (guard optional at first) to a small cohort; start
   gathering `/metrics`.
2. **Decision 2** — hide the values signal by default now (data-minimization); reconsider strengthening it
   only if usage shows demand.
3. **Decision 1** — promote the vision framing once the above settle.

Reply with any single decision (or "do 2(b)", "enable the guard", etc.) and the loop will implement it
immediately. Until then, further autonomous build would either overstep these calls or add low-value work.
