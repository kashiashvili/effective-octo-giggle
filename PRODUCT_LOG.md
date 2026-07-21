# Profiler — Product Log & Owner's Handbook

> **Purpose of this file.** You (the product owner) are not always in the room while
> agents build. This document is the single place that keeps you as informed as if you
> had written every line yourself: what the product is, why it exists, what decisions
> were made and *why*, how it is built, and a running changelog of every change.
>
> **Maintenance rule (for any agent working here):** whenever you add or change a
> feature, decision, data shape, config knob, or dependency, update this file in the
> same change — add a dated changelog entry at the top of the Changelog section and
> revise the relevant section above it. Keep it truthful and current. This is not
> optional; it is how the owner stays in control.

_Last updated: 2026-07-21_

---

## 1. What the product is

**Profiler** (repository `effective-octo-giggle`) is a **privacy-preserving social
matching** web app. A person connects the online platforms they already use; Profiler
extracts their interests, turns them into an anonymous mathematical **fingerprint**,
throws the raw interest data away, and then connects them with other people whose
fingerprints are similar.

- **Stack:** ASP.NET Core MVC (.NET 9), Entity Framework Core, SQLite, Razor views,
  hand-written CSS. No JavaScript framework.
- **One implementation:** an earlier Python/Flask prototype was removed. The `.NET`
  app in `Profiler.Web/` is the only codebase.

### Vision
Let people find others who genuinely share their interests **without surrendering their
personal data**. The whole product only earns the right to exist if it is more private
than the alternatives — so "raw data is never stored" is the north star, not a slogan.

### Target user
A privacy-conscious person who wants to meet like-minded people around niche interests
(software, reading, music, film, gaming, etc.) but does not want to hand their data to
yet another social network.

### Core problem
Interest-based matching normally requires giving a platform your raw activity. Profiler
does the matching on an anonymized fingerprint, so no raw activity is retained.

### Value proposition
"Find your people without sharing your data." Matches are computed from a MinHash
fingerprint; the underlying interests are discarded after the fingerprint is built.

---

## 2. Core user journey

1. **Register** → account is created and you are **signed in automatically**, landing
   on the Connect page.
2. **Connect at least one source.** The quickest need no tokens: a GitHub username, RSS
   feed URLs, or a Goodreads/Netflix CSV export. Others accept OAuth tokens / API keys.
3. Profiler **fetches your interests, builds the fingerprint, and discards the raw data.**
4. **View your matches** — ranked, with a qualitative tier and the source types you share.
5. **See each match's optional bio and contact** and reach out off-platform.
6. **Manage yourself** from the dashboard: see per-source signal counts, disconnect a
   source, edit your public profile, change your password, or delete your account.

---

## 3. Feature inventory (everything that exists today)

### Accounts & auth
- Register (with auto sign-in), login, logout.
- **Persistent cookie authentication** (30-day sliding expiry) — you stay signed in
  across server restarts and redeploys.
- **Change password** (verifies current password, requires a different new one).
- **Delete account** — password-confirmed; removes the user, their fingerprint, and all
  per-source signatures, then signs out.

### Data sources (18 connectors)
GitHub (username; optional token), Goodreads (CSV), Netflix (CSV), Google, Facebook,
Pinterest, Spotify, Twitter/X, LinkedIn, Reddit, Last.fm (key + username), Steam (key +
Steam ID), TikTok, Instagram, Twitch (token + client ID), RSS/Blogs (feed URLs),
SoundCloud, YouTube. Most API connectors need an OAuth token or API key; GitHub, RSS,
and the two CSV uploads need no account credentials.

### Fingerprint & sources
- **Per-source incremental fingerprints:** each connected source is stored as its own
  full-width MinHash signature. Your overall fingerprint is the element-wise minimum of
  them. This means you can **add, refresh, or disconnect one source without re-supplying
  the others** — nothing else is disturbed.
- **Signal counts:** each source shows how many distinct interest signals it contributed
  (a count only — the signals themselves are discarded). The dashboard shows the total
  and nudges you to connect more if the fingerprint is thin (< 15 signals).
- **Staleness:** a source not refreshed in over 90 days is flagged "stale" with a prompt to
  reconnect, so you aren't matched on long-outdated interests.

### Matching
- Similarity is the **estimated Jaccard similarity** of two MinHash fingerprints.
- Matches below a **5% floor** are hidden (below that they are indistinguishable from
  noise).
- Results use **qualitative tiers** — "Strong match", "Good match", "Some overlap" — with
  the percentage shown as approximate (`~N% shared`), never as false precision.
- Up to 20 matches, ranked. Each card shows the **source types you have in common**.
- Empty state distinguishes "you're the first user" from "others exist but none are close
  enough yet".

### Public profile (opt-in)
- Optional **bio** (≤ 280 chars) and **contact** line (≤ 120 chars) shown to people
  you match with, and previewed on your own dashboard.
- Rendered as **plain text** (HTML-escaped by Razor, verified XSS-safe) and **never as a
  clickable link**, to avoid phishing/open-redirect. Blank fields stay private.

### Privacy controls
- **Discoverability toggle** — "Hide me from matches" keeps your fingerprint and your own
  match view, but removes you from everyone else's results. Defaults to discoverable.
- **Data transparency** — `/account/data` shows exactly what is stored about you, with a
  "what we never store" section and a JSON download (`/account/data.json`).
- **Hide a specific person** — a "Hide" button on any match card removes that person from your
  matches, symmetrically (neither sees the other). Manage/undo at `/matches/hidden`.

### Robustness & failure handling
- Per-connector failures are surfaced to the user; a connector that returns no data is
  treated as a failure (many APIs return an error body with a 200-ish shape).
- A fingerprint is **never saved from zero features**; a partial failure keeps the
  sources that succeeded and leaves your existing data intact.

### Security hardening
- Passwords hashed with **BCrypt**, screened against common passwords / username containment /
  near-single-character strings. Usernames, bios and contact lines reject invisible and
  bidirectional-override characters. **Antiforgery** tokens on every POST (asserted by a reflection test).
- **Login rate limiting** (5 attempts/min per IP, configurable).
- **15s timeout** on all connector HTTP calls; **10 MB cap** per CSV upload, plus a 25 MB
  `RequestSizeLimit` on the connect endpoint so oversized bodies are rejected before buffering.
- **Ghost-session rejection**: a cookie whose user no longer exists is rejected.
- **HTTPS redirect + HSTS** in non-Development; auth cookie marked Secure over HTTPS.
- **Persistent Data Protection key ring** so auth cookies survive restarts/redeploys.
- **SSRF protection** on user-supplied RSS feed URLs: hosts resolving to loopback, private,
  link-local (incl. cloud metadata), CGNAT, unique-local, multicast or reserved addresses are
  refused before any request is made (`SsrfGuard`, fail-closed). RSS uses a dedicated client with
  auto-redirect disabled and follows redirects manually (max 3), re-validating each hop, and its
  connections are **pinned to the addresses just validated** (`SocketsHttpHandler.ConnectCallback`),
  which closes the DNS-rebinding window between checking a name and connecting to it.
- **5 MB response cap** on connector HTTP clients, so a hostile endpoint cannot exhaust memory.

---

## 4. Architecture

```
Profiler.Web/
├── Controllers/     Account, Sources, Matches, Home
├── Connectors/      IConnector + 18 implementations, ProfileData, ConnectorException
├── Profile/         ProfileAggregator, FingerprintGenerator, ProfileFingerprint
├── Matching/        UserMatcher, MatchResult
├── Data/            AppDbContext, Models (AppUser, FingerprintRecord, SourceFingerprintRecord, UserBlock)
├── Migrations/      EF Core migrations (applied automatically on startup)
├── ViewModels/      Login, Register, ChangePassword, Profile, ConnectSources, Match, SourceStatus
├── Views/           Razor views
├── wwwroot/css/     style.css (hand-written; the source of truth for CSS class names)
└── Program.cs       DI, auth, rate limiting, data protection, pipeline
Profiler.Web.Tests/
├── *Tests.cs        Unit tests (fingerprint, matcher, aggregator, connectors, viewmodels)
└── Integration/     WebApplicationFactory HTTP/auth-flow tests
```

**Request flow for matching:** `MatchesController` loads every user's combined
`FingerprintRecord`, feeds them to `UserMatcher`, applies the 5% floor, and maps results
to `MatchViewModel` (adding the matched user's bio/contact and shared source types).

---

## 5. Data model

| Entity | Key fields | Notes |
|--------|-----------|-------|
| `AppUser` | Id, Username, PasswordHash, CreatedAt, Bio?, Contact?, IsDiscoverable | Username is case-insensitive unique (NOCASE); Bio/Contact are the opt-in public profile |
| `FingerprintRecord` | UserId (PK), FingerprintJson, SourcesJson, UpdatedAt | The **combined** (truncated) signature used for matching |
| `SourceFingerprintRecord` | Id, UserId, Source, RawSignatureJson, FeatureCount, UpdatedAt | One per (user, source); **raw 64-bit** signature; unique index on (UserId, Source) |

Schema changes are made with EF migrations and applied on startup via
`db.Database.Migrate()`.

---

## 6. Privacy & security model (the promise, precisely)

1. Each interest is a short string (e.g. `language:python`, `genre:sci-fi`).
2. Strings are hashed (SHA-256) and reduced to a **128-dimension MinHash signature**.
3. **Only the signature is stored. The raw interest strings are discarded.**
4. Access tokens / API keys are used once to fetch data and **never persisted**.
5. Signal counts are aggregate numbers only — not the underlying interests.
6. Other users only ever see your **username, a match tier/approximate %, the source
   types you share, and whatever bio/contact you chose to make public.**

Two signatures are compared to estimate how much two people's interests overlap
(Jaccard similarity) without either side revealing what those interests are.

---

## 7. Configuration

All settings come from `appsettings.json` or environment variables.

| Key | Default | Purpose |
|-----|---------|---------|
| `ConnectionStrings:Default` | `Data Source=profiler.db` | SQLite database location |
| `RateLimiting:LoginPermitLimit` | `5` | Login attempts per IP per minute |
| `DataProtection:KeyPath` | `<contentRoot>/keys` | Where the auth-cookie key ring is stored (git-ignored secret) |

---

## 8. Running & deploying

```bash
dotnet run --project Profiler.Web     # dev, http://localhost:5000 (see launchSettings)
dotnet test                           # 116 tests, fully offline
```

- **Run behind HTTPS in production** (HSTS + HTTPS redirect turn on outside Development).
- **Persist the key ring** (`DataProtection:KeyPath`) on a volume so cookies survive
  redeploys; point it at shared storage for multi-instance deployments.
- **Schema changes** ship as EF migrations and apply automatically on startup.
- Add a migration: `dotnet ef migrations add <Name> --project Profiler.Web`.

---

## 9. Testing

116 xUnit tests, **fully offline and fast (~1–2s)**:
- **Unit:** fingerprint math (incl. the union = element-wise-min property), matcher
  (ranking, threshold, empty exclusion), aggregator (failures), view-model tiers/validation,
  connectors (CSV parsing + garbage handling + API-error handling via a stub HTTP handler).
- **Integration** (`Integration/AuthFlowTests.cs`): boots the real app on an isolated temp
  database and exercises auth redirects, register/auto-login, bad login, profile save,
  change password (old fails / new works), account deletion, and ghost-cookie rejection.

---

## 10. Known limitations & backlog

- **Sources can't auto-refresh.** Because tokens are never stored (privacy), refreshing a
  source means re-entering its credentials. This is intentional.
- **No in-app messaging.** Matches connect via the contact line each person opts to share.
- **No email / password reset.** Account deletion is the only recovery path today.
- Matching returns the top 20; there is no pagination or manual filtering yet.
- **Matching is O(all users) in memory.** Every `/matches` request loads and deserialises every
  user's fingerprint, then compares against all of them. Fine at current scale; past a few
  thousand users this needs LSH banding (bucket candidates by signature bands) so each request
  only compares against plausible neighbours.

---

## 11. Changelog (newest first)

Each entry: what changed and why it mattered.

### 2026-07-21
- **Display text is checked for deceptive characters** — usernames, bios and contact lines are all
  read by a stranger deciding whether to make contact, so all three now reject bidirectional
  overrides and invisible/control characters (`TextPolicy`). A contact line is the sharpest case: an
  override makes it display as something other than what it copies. Bios may span lines; usernames
  and contact lines may not. Accented, Cyrillic and Japanese text is explicitly still accepted,
  asserted by tests so the rule cannot drift into a Latin-only filter.
- **Usernames can no longer carry invisible or direction-flipping characters** — the username is
  the only thing a stranger sees before deciding to make contact, and bidirectional overrides or
  zero-width characters let one account impersonate another. Those specific characters are refused;
  accented, Cyrillic and Japanese names are explicitly still accepted, asserted by tests so the
  rule cannot drift into a Latin-only filter.
- **Credentials are not echoed back** when the connect form redisplays — previously true only
  because password inputs omit their value, now asserted, with a text field in the same request
  proving the check isn't passing through a binding failure.
- **The core privacy promise is now asserted against the database** — "raw interests are never
  stored" had only ever been true by construction. A test now sends distinctive interests through
  the real fingerprinting and persistence path, then searches every row of every table for any
  trace of them, and separately confirms the signature *was* stored so the assertion cannot pass
  vacuously. Verified to fail when a leak is introduced.
- **Internal user ids no longer reach the page** — the hide/unhide forms carried the target's
  database id, handing every signed-in user a sequential identifier for their matches and a rough
  count of everyone registered. They post the username instead, and `MatchViewModel` no longer has
  an id to leak.
- **Authorization is now deny-by-default** — `AccountController` mixed anonymous and protected
  actions and depended on each protected one remembering `[Authorize]`; a new action serving
  profile or export data would have been public. The class is authorized and register/login/logout
  opt out explicitly. Reflection tests assert every POST carries an antiforgery token and every
  controller except Home is authorized at class level — both verified to fail when violated.
- **Production posture verified, and a deployment trap documented** — the docs claimed HTTPS
  redirection outside Development and a friendly error page, but neither had ever been exercised.
  Both now have tests. Writing the first one surfaced something worth knowing: ASP.NET's HTTPS
  redirection **silently does nothing when it cannot determine the HTTPS port**, which is exactly
  what happens behind a TLS-terminating proxy. `ASPNETCORE_HTTPS_PORT` (or forwarded headers) must
  be set or the app serves plain HTTP with no redirect and no warning. Added to the README.
- **Mobile layout checked and fixed** — the app had never been viewed at phone width. The
  dashboard's source and discoverability rows were flex rows that assumed desktop width, so at
  375px the Disconnect and "Hide me" buttons were clipped off the card edge. They wrap now.
  Everything else held up: no horizontal overflow, working nav toggle, single-column collapse.
- **Duplication cleanup** — the user-id claim lookup (twelve copies) and the "user vanished
  mid-request" handling (seven copies) each now live in one place.
- **DNS-rebinding window closed** — the SSRF guard validated a hostname and then let `HttpClient`
  resolve it again to open the socket, leaving room for the answer to change in between. The RSS
  client now resolves, validates, and connects to those exact addresses via
  `SocketsHttpHandler.ConnectCallback`, so what was checked is what is reached. Verified that a
  real HTTPS feed still fetches (TLS and manual redirect following are unaffected).
- **Styled rate-limit page** — tripping the login limiter returned bare text with no way back,
  which a user who simply forgot their password could hit. It now returns a proper 429 page
  (with `Retry-After`) that links the app's own stylesheet rather than duplicating it.
- **CI** — GitHub Actions workflow builds in Release and runs the full suite on every push and
  pull request. The suite is offline, so CI needs no secrets or network.
- **Full journey re-verified on a clean database** — dropped the dev database and walked the whole
  flow end to end: all five migrations applied from scratch, `/health` green, a stale cookie for a
  deleted user correctly rejected, register → auto sign-in → connect two sources in one submit →
  matches → profile → data page → account deletion, ending with every table empty.
- **Weak-password screening** — registration and change-password now reject well-known passwords
  (`password123`, `qwerty123`, …), anything containing the username, and near-single-character
  strings. Previously only an 8-character minimum was enforced. `Security/PasswordPolicy`.
- **Connect page grouped by friction** — the flat 18-card grid is now split into "No account
  needed" (GitHub, Goodreads, Netflix, RSS) and "Needs a token or API key", so the fastest ways
  to start are above the fold instead of buried among OAuth-only sources.
- **Stale-source nudge** — a source not refreshed in over 90 days is marked "stale" on the
  dashboard, with a hint to reconnect it. Without this, people get matched on months-old
  interests with nothing prompting a refresh (tokens aren't stored, so refresh must be manual).
- **`/health` endpoint** — anonymous liveness/readiness probe that verifies database connectivity
  (200 `{"status":"healthy"}` or 503). For load balancers and uptime monitors; reports nothing
  beyond reachability. No new dependency.
- **Oversized uploads rejected at the pipeline level** — `[RequestSizeLimit(25 MB)]` on the connect
  endpoint, so a huge multipart body is refused before ASP.NET buffers up to its 128 MB default
  and the per-file 10 MB check runs.
- **Verified XXE is not exploitable** in RSS XML parsing (rather than assuming it). A payload with
  an external entity pointing at `file:///etc/passwd` parses, but the entity expands to nothing —
  .NET's `XmlResolver` defaults to null, so no file is read and nothing leaks into features. Locked
  in with a regression test.
- **Closed the SSRF redirect bypass + capped response size** — the first SSRF fix only validated
  the initial URL, but `HttpClient` auto-follows redirects, so a public URL could 302 to an
  internal address. RSS now uses its own client with auto-redirect **off** and follows redirects
  manually (max 3 hops), re-validating every hop. Connector clients also cap the response buffer
  at 5 MB so a hostile endpoint cannot exhaust memory.
- **Fixed SSRF in the RSS connector** — the connector fetches user-supplied feed URLs on the
  server, which previously only checked the URL scheme. It now resolves each host and refuses
  any that maps to a loopback, private, link-local (incl. the 169.254.169.254 cloud-metadata
  endpoint), CGNAT, unique-local, multicast, or reserved address (`SsrfGuard`, fail-closed).
  A real (public) feed still works. Residual note: this does not fully close DNS-rebinding
  (TOCTOU) — see Known limitations.
- **Connector happy-path tests** — added a URL-routing stub handler and tests that verify the
  GitHub connector extracts languages/topics/forks/gists/starred-topics from realistic JSON
  (and throws on an unknown user), plus Reddit subreddit extraction. Protects the core
  interest-extraction logic, which previously had only error-path coverage.
- **Landing page privacy section** — added a "Your Privacy, Your Control" section to the home
  page showcasing the differentiators (raw data never stored, see/download your data, hide from
  matches, one-click delete) so the storefront reflects the product's actual strengths.
- **Hide/block a specific person** — a "🙈 Hide" button on each match card removes that person
  from your matches; the effect is symmetric (neither of you sees the other). Manage/undo at
  `/matches/hidden`. New `UserBlock` table (migration `AddUserBlocks`). A safety tool distinct
  from the all-or-nothing discoverability toggle.
- **"Your data" transparency page + JSON export** — `/account/data` shows a user exactly
  what is stored (username, join date, discoverability, bio/contact, the anonymized
  fingerprint's dimensions, and per-source signal counts) plus a "what we never store"
  section; `/account/data.json` downloads the same as JSON. Reinforces the privacy promise
  by making it legible. Linked from the dashboard.
- **Discoverability toggle** — a privacy control: "Hide me from matches" keeps your
  fingerprint and lets you still browse your own matches, but removes you from everyone
  else's results. Toggled from the dashboard; the matches page shows a banner while hidden.
  New `IsDiscoverable` column (migration `AddIsDiscoverable`, existing users default to
  discoverable). `MatchesController` excludes non-discoverable users from others' candidates.
- **Accessibility pass** — added a "Skip to content" link (visible on keyboard focus), an
  `id` on the main landmark, `role="status"`/`role="alert"` on flash messages so screen
  readers announce success/errors, and the mobile nav toggle now updates `aria-expanded`.
- **Case-insensitive usernames** — "Alice" and "alice" are now the same account, and you can
  sign in regardless of the case you type. Enforced by a `NOCASE` collation + unique index on
  `Username` (migration `CaseInsensitiveUsername`); registration also catches the concurrent-
  duplicate race. The originally-registered casing is preserved for display.
- **Loading state on form submit** — `wwwroot/js/site.js` disables the submit button and
  shows "Working…" after the form serializes, giving feedback on slow connect posts and
  preventing accidental double-submits. Progressive enhancement (works without JS); the
  nav sign-out is excluded. Verified that submission still completes.
- **Product Log created** — this file, `PRODUCT_LOG.md`, so the owner stays fully informed
  of the product across agent sessions; agents must keep it updated with every change.
- **Change-password flow** — `/account/password`; verifies current password, requires a
  new (different) one, keeps you signed in. Closes the gap where deleting the account was
  the only way to deal with a compromised password. Covered by integration tests.
- **Per-source signal counts + thin-fingerprint nudge** — dashboard now shows how many
  interest signals each source contributed and the total, and suggests connecting more
  when the fingerprint is thin. Privacy-safe (a count, not the interests). New
  `FeatureCount` column (migration `AddSourceFeatureCount`).
- **Production security** — auth cookie `Secure` over HTTPS; HSTS + HTTPS redirect outside
  Development; **persistent Data Protection key ring** so auth cookies survive
  restarts/redeploys (verified). `.gitignore` cleaned of stale Python entries; `keys/`
  ignored.
- **Offline connector tests** — replaced 7 tests that hit real third-party APIs with a stub
  HTTP handler; added CSV garbage/empty edge cases and a Spotify happy-path parse. Suite is
  now deterministic and ~3× faster.
- **Connect-page onboarding callout + match-card polish** — new users get a "start here /
  no tokens needed" hint (hidden once a source is connected); match-card bio spacing refined.
- **EF Core migrations** — replaced `EnsureCreated` with migrations applied on startup, so
  schema can evolve without deleting the database. Required for real deployment.
- **Integration tests** — added a `WebApplicationFactory`-based suite covering the HTTP/auth
  pipeline end to end.
- **Optional public profile (bio + contact)** — makes matches actionable while staying
  private; opt-in, plain text, XSS-safe.
- **Reject auth cookies for deleted/nonexistent users** — no more "ghost" sessions after
  out-of-band deletion.
- **Match quality** — 5% floor, qualitative tiers instead of false-precision percentages,
  and honest empty states.
- **Account deletion** — password-confirmed, cascades to all user data.
- **Cookie auth + hardening** — persistent cookie auth replacing in-memory sessions; login
  rate limiting; 15s connector timeout; 10 MB CSV upload cap.
- **Incremental per-source fingerprints + disconnect** — store a raw MinHash signature per
  source and combine by element-wise minimum, so sources can be added/refreshed/removed
  independently.
- **Fix navbar markup/CSS mismatch** — the nav bar was rendering unstyled.
- **Surface connector failures; block empty fingerprints** — users are told when a source
  fails; empty/all-zero fingerprints are never saved and never match.
- **Baseline established** — build, test, and run the .NET app.
- **Removed the Python version** — the .NET app is now the only implementation.
- **Retargeted to net9.0 and replaced `.slnx` with `Profiler.sln`** — so it builds with the
  installed SDK.
```
