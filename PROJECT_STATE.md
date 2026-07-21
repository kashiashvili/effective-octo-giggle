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

Product Owner review (fresh independent review in progress).

## Active Task

Establish baseline, refresh this file, and select the next highest-value improvement from a
fresh independent product review.

### Definition of Done

- Baseline verified: build clean, full test suite green. ✅ (0 warnings, 116/116 passing)
- `PROJECT_STATE.md` reflects the real product state. ✅
- Next active task selected from the independent review and recorded here.

## Current Execution Notes

Baseline command set:

```bash
dotnet build            # expect 0 warnings, 0 errors
dotnet test             # expect 116/116 passing, ~3s, fully offline
dotnet run --project Profiler.Web   # http://localhost:5000
```

Working branch: `rebuild/dotnet-profiler`. Commit continuously in focused units.

## Backlog

### P0 — Critical

- None identified.

### P1 — Core Product

- **Account recovery.** Forgetting a password currently means losing the account outright;
  there is no reset path. Needs a design that does not require an email address (a one-time
  recovery code issued at registration is the privacy-consistent option).

### P2 — Quality / Reliability / UX

- Pending the fresh independent product review.

### P3 — Enhancement

- Match list pagination / filtering beyond the top 20.

### P4 — Polish / Optional

- LSH banding for matching (only matters past a few thousand users).

## Known Bugs / Risks

- None confirmed at baseline; the independent review may surface some.

## Last Completed Iteration

**Iteration:** baseline re-establishment (this session)

**Completed:** Reconstructed product state from the repository after the state file was
found still holding template placeholders.

**Validation:** `dotnet build` clean; `dotnet test` 116/116 passing.

**Result:** Baseline green; independent Product Owner review dispatched.

## Next Mandatory Action

Take the findings of the independent product review, record the highest-value item as the
active task, and implement it immediately.
