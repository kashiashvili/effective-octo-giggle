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

Developer — implementing the top remaining P1 from the independent review.

## Active Task

**Account recovery without email.** A forgotten password permanently locks the account *and*
makes the data undeletable (deletion is password-confirmed), so the locked-out user's
fingerprint stays in the matching pool forever and their username is squatted. This
contradicts the "delete everything, any time" promise the product sells on.

Design: a one-time recovery code generated at registration, shown once, stored BCrypt-hashed,
single-use. It resets the password (and therefore restores the ability to delete). No email is
collected, so the privacy stance holds.

### Definition of Done

- Code issued at registration and shown exactly once, with a page that makes clear it cannot
  be shown again.
- `/account/recover` (anonymous, rate-limited) takes username + code + new password in one
  post; generic failure message that does not reveal whether a username exists.
- Using a code invalidates it and issues a new one.
- Signed-in users can regenerate a code, password-confirmed.
- Existing accounts (no code on file) are nudged to generate one.
- Migration; tests covering issue / use / single-use / wrong-code; docs updated.

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

- **Account recovery** — active task above.
- **Contact is broadcast to every match with no mutual consent.** A contact line goes to up to
  20 strangers at once with no per-person choice. Proposed: a mutual "connect request" — either
  side can request, contacts are exchanged only when both accept. (The smaller half — saying
  when a match has no contact, and nudging users who set none — is done.)
- **Matching says how much you overlap but never what kind.** The per-source signatures are
  already stored and never used for comparison, so users get "Strong match, ~41%" with no
  conversation starter. Also, because the combined fingerprint is a union, users who connect
  many sources score systematically lower against everyone — the app punishes engagement.

### P2 — Quality / Reliability / UX

- Oversized submissions fail unhandled: `[RequestSizeLimit(25MB)]` fires before the friendly
  per-file check, so a >25 MB body yields a raw framework error page.

### P3 — Enhancement

- Username homoglyph impersonation (`аdmin` with a Cyrillic а) is still accepted; `TextPolicy`
  rejects only invisible/bidi characters. Proposed: reject mixed-script usernames (single
  script + Common), which still admits the Cyrillic and Japanese names the tests pin.
- Match list pagination / filtering beyond the top 20.

### P4 — Polish / Optional

- LSH banding for matching (only matters past a few thousand users).

## Known Bugs / Risks

- None open. Fixed this session: blocks outliving account deletion; sequential connector
  fetches; rate limiting collapsing to one global bucket behind a proxy.

## Last Completed Iteration

**Iteration:** 5 (this session)

**Completed:** Baseline re-established and state file reconstructed; blocks no longer outlive a
deleted account (schema cascade + migration purge + transparency page lists who you hid);
connectors and RSS feeds fan out concurrently under a 30s budget; match cards state when there
is no way to reach someone and how fresh the other fingerprint is; registration and connect are
rate-limited; rate limiting made proxy-aware.

**Validation:** `dotnet build` 0 warnings; `dotnet test` 135/135 passing. Core journey verified
end to end against a running server (register → connect GitHub/RSS → fingerprint 128 dims, raw
interests absent from the database → matched pair with bio/contact → hide, symmetric → data
export → password-confirmed deletion, cascade confirmed in SQLite).

**Result:** All findings from the independent review are triaged; three P1/P2 items shipped.

## Next Mandatory Action

Implement account recovery, validate, update `PRODUCT_LOG.md`, then return to Product Owner
mode and take the next P1 from the backlog.
