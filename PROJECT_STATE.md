# Project State

> This file is the authoritative operational memory for the autonomous product-development loop.
> Keep it concise, factual, and current. Update it after every meaningful iteration.

## Product Idea

**Profiler** — a privacy-preserving social matching web app. You connect the online
platforms you already use; Profiler extracts your interests, reduces them to an anonymous
MinHash fingerprint, discards the raw data, and matches you with people whose fingerprints
are similar.

Stack: ASP.NET Core MVC (.NET 9), EF Core, SQLite, Razor views, hand-written CSS. No JS
framework. Single implementation in `Profiler.Web/` (an earlier Python prototype was removed).

Full product handbook and changelog: `PRODUCT_LOG.md` (must be updated with every change).

## Product Vision

Let people find others who genuinely share their interests **without surrendering their
personal data**. The product only earns the right to exist if it is more private than the
alternatives — "raw data is never stored" is the north star, not a slogan.

## Target User

A privacy-conscious person who wants to meet like-minded people around niche interests
(software, reading, music, film, gaming) but does not want to hand their data to yet
another social network.

## Core Problem

Interest-based matching normally requires giving a platform your raw activity. Profiler
matches on an anonymized fingerprint, so no raw activity is retained.

## Core Value Proposition

"Find your people without sharing your data." Matching is computed from a MinHash
fingerprint; the underlying interests are discarded once the fingerprint is built.

## Core User Journey

1. Register (auto sign-in) → land on the Connect page.
2. Connect at least one source (GitHub username, RSS URLs, or a Goodreads/Netflix CSV need
   no credentials; the rest accept OAuth tokens / API keys).
3. Profiler fetches interests, builds the fingerprint, discards the raw data.
4. View ranked matches with a qualitative tier and shared source types.
5. Read a match's optional bio/contact and reach out off-platform.
6. Manage yourself from the dashboard: signal counts, disconnect a source, edit public
   profile, change password, hide from matches, hide a person, export data, delete account.

## Success Criteria

- A new user can go from registration to a real match list without credentials they cannot
  obtain, and without any raw interest data being persisted.
- Every failure mode in that journey (connector failure, empty fingerprint, no matches yet,
  rate limit, deleted account) is explained in the UI rather than surfacing as an error.
- The privacy promise is asserted by tests against the database, not by construction.
- Build, tests, and security posture stay green.

## Explicit Mission Completion Criteria

The autonomous mission is complete only when a fresh Product Owner review confirms all of the following:

- No unresolved P0, P1, P2, or P3 product improvements remain.
- The complete core user journey works end to end and has been verified.
- All relevant builds, type checks, lint checks, and tests pass.
- No known meaningful defects or regressions remain.
- Important error, empty, loading, validation, permission, and persistence states are handled.
- Security and data-integrity risks relevant to the product have been reviewed.
- A fresh independent product review finds no additional meaningful work whose expected value justifies implementation.

Finishing a single task, feature, milestone, sprint, or initial backlog does not satisfy these criteria.

## Current Product Assessment

The product is mature and the core journey is implemented end to end: accounts, 18
connectors, per-source incremental fingerprints, matching with a noise floor and
qualitative tiers, opt-in public profile, discoverability toggle, per-person hiding, data
transparency + JSON export, account deletion.

Security has had several passes: BCrypt + password screening, antiforgery on every POST
(reflection-asserted), deny-by-default authorization, login rate limiting, SSRF guard with
DNS-rebinding pinning on RSS, 5 MB response cap, 10 MB CSV cap, HSTS/HTTPS outside
Development, persistent Data Protection key ring, text policy rejecting invisible and
bidirectional characters in usernames/bios/contact lines.

Account recovery now exists (one-time code, no email). Known standing limitations
(documented in `PRODUCT_LOG.md` §10): no auto-refresh of sources (intentional — tokens are
never stored), no in-app messaging, matches capped at 20 with no pagination, and matching is
O(all users) in memory (needs LSH banding past a few thousand users).

## Current Phase

Product Owner review. Completion gate (a fresh *independent* review) is blocked — see below.

## Completion Gate Status — blocked by an external resource limit

The mission's formal completion criterion is "a fresh independent product review finds no
additional meaningful work." Two independent (subagent) reviews were run this run and each
found a real defect the previous missed — the second a **P0** (reversible fingerprints). All
their findings are shipped. A **third** independent review was commissioned as the gate and
**failed to run: the account hit its monthly spend limit**, which also prevents launching
further review subagents. Per `CLAUDE.md`, an external resource limit preventing further work
is a legitimate stopping condition.

In place of the blocked independent review, an **inline** review of the newest, least-scrutinised
code was performed (pepper + scheme purge, session cutoff, cancellation threading, reciprocal
discoverability, the connect transaction). It found no P0/P1/P2. This is *not* a substitute for
an independent review — it is the same author checking their own work — so the gate is recorded
as **blocked, not satisfied**.

## Previous Active Task (complete)

**Account recovery without email.** A forgotten password permanently locks the account *and*
makes the data undeletable (deletion is password-confirmed), so the locked-out user's
fingerprint stays in the matching pool forever and their username is squatted. This
contradicts the "delete everything, any time" promise the product sells on.

Design: a one-time recovery code generated at registration, shown once, stored BCrypt-hashed,
single-use. It resets the password (and therefore restores the ability to delete). No email is
collected, so the privacy stance holds.

### Definition of Done — all met

- Code issued at registration and shown exactly once. ✅
- `/account/recover` (anonymous, rate-limited), single post, generic failure message. ✅
- Single-use; using it issues a replacement. ✅
- Signed-in users can regenerate, password-confirmed. ✅
- Accounts with no code on file are nudged on the dashboard. ✅
- Migration `AddRecoveryCode`; 14 tests; README and `PRODUCT_LOG.md` updated. ✅

## Current Execution Notes

Baseline command set:

```bash
dotnet build            # expect 0 warnings, 0 errors
dotnet test             # expect 185/185 passing, ~6s, fully offline
dotnet run --project Profiler.Web   # http://localhost:5000
```

Working branch: `rebuild/dotnet-profiler`. Commit continuously in focused units.
QA server config: `.claude/launch.json` → `profiler-web-qa` on port 5241, pinned to the
Development environment so the app boots without a production `Fingerprint:Pepper`. Port 5240
belongs to another session.

## Backlog

### P0 — Critical

- None identified.

### P1 — Core Product

- **Contact is broadcast to every match with no mutual consent.** A contact line goes to up to
  20 strangers at once with no per-person choice. Proposed: a mutual "connect request" — either
  side can request, contacts are exchanged only when both accept. The largest remaining product
  bet, and a design change rather than a defect (the field is opt-in, labelled, and now reciprocal
  with discoverability). **Not started** — it is a genuine product decision worth an owner's steer
  before building, and it touches the data model and the matching view materially, so it is the
  kind of consequential change that should have independent review, which is currently blocked.

### P2 — Quality / Reliability / UX

- None open. (Connector cancellation shipped — commit `ed42817`.)

### P3 — Enhancement (all reviewed; each deferred with a reason)

- **Username uniqueness folds case only for ASCII** (SQLite `NOCASE`), so `André` and `ANDRÉ` are
  two accounts that read as one. The correct fix is a normalised-username column with a unique
  index and a backfill — a **data migration**, and shipping a consequential data-integrity
  migration while independent review is unavailable is exactly what should wait for a review. Low
  severity meanwhile (both are real names; invisible-character and mixed-alphabet impersonation,
  the sharp cases, are already blocked). Take together with the confusable-skeleton item below.
- **Pure-lookalike usernames** (a name written *entirely* in Cyrillic that reads as Latin, e.g.
  `сор` vs `cop`) remain possible. Same normalised/skeleton column, same reason to defer.
- **Containment-based scoring** so a wide-ranging profile is not diluted by the union. Jaccard of
  the union is a defensible definition of overall similarity, and the per-source line already
  makes the dilution legible — deferred as a judgement call, not a defect.
- **Match list pagination** beyond the top 20. Genuinely low value at current scale (matching is
  already O(all users); pagination matters only once there are far more than 20 plausible matches,
  which is the same regime that needs the P4 LSH work first).

### P4 — Polish / Optional

- LSH banding for matching (only matters past a few thousand users).

## Known Bugs / Risks

- **Operational:** `Fingerprint:Pepper` is now required outside Development, and it must be kept
  for the life of the deployment. If it is lost or changed, every stored signature becomes
  meaningless — the app detects this at startup and clears them, so everyone must reconnect their
  sources. The raw interests needed to rebuild them are deliberately gone.
- Deploying the session cutoff signs everyone out once: cookies issued before it carry no
  issue-time claim and are treated as expired.
- Otherwise none open. Fixed this session: a stored fingerprint that could be reversed by anyone
  holding the database; sessions surviving a password change; blocks outliving account deletion;
  sequential connector fetches; rate limiting collapsing to one global bucket behind a proxy;
  a non-atomic connect path; one-way discoverability.

## Last Completed Iteration

**Iteration:** 9 (this session)

**Completed:** connector cancellation (commit `ed42817`) — `IConnector.FetchAsync` takes a
`CancellationToken`, the aggregator links the 30s budget with the caller's `RequestAborted`, every
connector and the SSRF DNS lookup forward it, and cancellation is not swallowed by the catch-alls.
Followed by an inline review of the newest code and coverage for the scheme verifier
(commit `02d1525`).

Earlier in the session (iterations 1–8): baseline re-established and the state file reconstructed;
blocks no longer outlive a deleted account; concurrent connector fan-out under a 30s budget;
match cards state a missing contact, fingerprint freshness, and the strongest shared source;
registration/connect rate-limited and proxy-aware; account recovery codes; usernames may not mix
alphabets; the reversible-fingerprint **P0** fixed with a per-deployment pepper + scheme-change
purge; session invalidation on password change/recovery plus "sign out everywhere"; reciprocal
discoverability; atomic connect; per-policy 429 wording; trimmed-username length; friendly
oversized-upload page.

**Validation:** `dotnet build` 0 warnings; `dotnet test` **185/185** passing, stable across three
consecutive runs. All 9 migrations apply cleanly to a fresh database, and to the dev database
(the pre-pepper signature was detected and cleared on first boot). Full core journey re-verified
end to end against the running server this iteration — register → one-time recovery code →
connect real GitHub → 128-dim fingerprint, no raw interests in any table → matched pair, contact
visible → symmetric hide → password change signs out the other device but not the acting one →
password-confirmed delete leaves no orphan blocks.

**Result:** Every P0–P2 finding from all three review attempts is shipped. Remaining backlog is
P1 (mutual-contact-consent — a product bet needing an owner's steer) and P3s, each deferred with a
recorded reason. See the completion-gate note above: the formal independent-review gate is blocked
by the account's monthly spend limit.

## Next Mandatory Action

The independent-review completion gate cannot be run until the account spend limit is lifted
(raise at claude.ai settings, or resume the failed review agent once budget is available). When it
is: run a fresh independent review; if clean, the mission's criteria are met. Until then there is
no P0/P1/P2 defect work outstanding, and the remaining P1/P3 items are consequential or
product-judgement calls deliberately held for review/owner input rather than shipped blind. The
repository is in a coherent, fully green state at commit `02d1525`.
