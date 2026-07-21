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

Known standing limitations (documented in `PRODUCT_LOG.md` §10): no auto-refresh of sources
(intentional — tokens are never stored), no in-app messaging, **no password reset /
account recovery**, matches are capped at 20 with no pagination or filtering, and matching
is O(all users) in memory (needs LSH banding past a few thousand users).

## Current Phase

QA/Validation — last P2 (oversized-upload error page) in flight; then Product Owner review.

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
dotnet test             # expect 135/135 passing, ~4s, fully offline
dotnet run --project Profiler.Web   # http://localhost:5000
```

Working branch: `rebuild/dotnet-profiler`. Commit continuously in focused units.
QA server config: `.claude/launch.json` → `profiler-web-qa` on port 5241 (port 5240 belongs to
another session).

## Backlog

### P0 — Critical

- None identified.

### P1 — Core Product

- **Contact is broadcast to every match with no mutual consent.** A contact line goes to up to
  20 strangers at once with no per-person choice. Proposed: a mutual "connect request" — either
  side can request, contacts are exchanged only when both accept. (The smaller half — saying
  when a match has no contact, and nudging users who set none — is done.) Judged the largest
  remaining product bet; it is a design change, not a defect, since the field is opt-in and
  labelled.

### P2 — Quality / Reliability / UX

- Oversized submissions fail unhandled: `[RequestSizeLimit(25MB)]` fires before the friendly
  per-file check, so a >25 MB body yields a raw framework error page. **In flight.**

### P3 — Enhancement

- Consider containment-based scoring so a wide-ranging profile is not diluted by the union.
  Reviewed and deliberately deferred: Jaccard of the union is a defensible definition of overall
  similarity, and the new per-source line makes the effect legible rather than mysterious.
- Pure-lookalike usernames (a name written *entirely* in Cyrillic that reads as Latin, e.g.
  `сор` vs `cop`) are still possible now that mixed-alphabet names are refused. Closing it needs
  a confusable-skeleton column with a unique index and a backfill.
- Match list pagination / filtering beyond the top 20.

### P4 — Polish / Optional

- LSH banding for matching (only matters past a few thousand users).

## Known Bugs / Risks

- None open. Fixed this session: blocks outliving account deletion; sequential connector
  fetches; rate limiting collapsing to one global bucket behind a proxy.

## Last Completed Iteration

**Iteration:** 8 (this session)

**Completed, in order:** baseline re-established and the state file reconstructed from the
repository; blocks no longer outlive a deleted account (schema cascade + migration purge, and
the transparency page now lists who you hid); connectors and RSS feeds fan out concurrently
under a 30s budget; match cards state when there is no way to reach someone; registration and
connect rate-limited; rate limiting made proxy-aware (`ForwardedHeaders`, refused if enabled
without a trust list); match cards show fingerprint freshness; **account recovery codes**;
per-source overlap on match cards ("Closest on RSS/Blogs"); usernames may no longer mix
alphabets.

**Validation:** `dotnet build` 0 warnings; `dotnet test` 158/158 passing. All 7 migrations apply
cleanly to a fresh database. Core journey verified end to end against a running server at each
step — register → recovery code shown once → connect GitHub/RSS (real network) → fingerprint 128
dims with raw interests absent from every table → matched pair with bio/contact and per-source
overlap → hide, symmetric → data export → password reset by recovery code, old password dead →
password-confirmed deletion with cascade confirmed in SQLite.

**Result:** Every P0–P3 finding from the independent review is either shipped or explicitly
deferred with a reason. One P1 (mutual contact consent) remains as a deliberate product bet.

## Next Mandatory Action

Land the oversized-upload error page, re-run validation, then perform a fresh independent
Product Owner review against the current code — not against this file — and either take the
mutual-consent P1 or record why it should not be built.
