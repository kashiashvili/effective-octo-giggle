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

_Last updated: 2026-09-17_

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

1. **Register** → account is created, you are **signed in automatically**, and you are shown
   your **recovery code once** (there is no email address on file, so this is the only way
   back in). Its continue button leads to the interests list; connecting a source is the
   alternative link.
2. **Describe your interests, or connect a source.** No account at all is needed: at
   `/sources/interests` you tick interests from a curated list (plus free text) and get the same
   fingerprint. Connectors that need no tokens: a GitHub username, RSS feed URLs, or a
   Goodreads, Netflix or YouTube (Google Takeout) export. The rest accept OAuth tokens / API keys you create yourself —
   the landing page and the dashboard say so, and lead with the no-accounts path.
3. Profiler **fetches your interests, builds the fingerprint, and discards the raw data** (and
   the picks, and any tokens).
4. **View your matches** — ranked by interest similarity, with a qualitative tier, the source
   types you share, how fresh the other fingerprint is, and — when both of you opted in — the
   interests you both chose to show. Filter by shared interest theme; sort by best match, same
   connection intent first, or similar outlook first. Nothing is blended into one score.
5. **Optionally add compatibility signals** beyond interests: a connection intent (what kind of
   connection you want), a short values/outlook questionnaire (consent-gated; answers discarded
   after deriving a coarse bucket), and a list of interests you are happy to show on your card.
   Each is shown to matches as its own explainable line.
6. **See each match's optional bio and contact** and reach out off-platform. Hide anyone you
   don't want to see again; report anyone who misbehaves.
7. **Manage yourself** from the dashboard: per-source signal counts, disconnect a source, edit
   your public profile and signals, change your password, sign out other devices, regenerate
   your recovery code, review and export everything stored about you, or delete your account.

---

## 3. Feature inventory (everything that exists today)

### Accounts & auth
- Register (with auto sign-in), login, logout.
- **Persistent cookie authentication** (30-day sliding expiry) — you stay signed in
  across server restarts and redeploys.
- **Change password** (verifies current password, requires a different new one). Doing so **ends
  every other session**, which is the point of changing it; the session doing the change stays in.
- **Sign out on every other device** — same cutoff, without changing the password, for a borrowed
  laptop rather than a compromised secret.
- **Recovery code** — the substitute for a password-reset email, since no email address is
  collected. 100 bits in Crockford base32, shown once at registration, stored as a BCrypt hash
  only. Single-use, and using it issues a replacement. Regenerate from the dashboard
  (password-confirmed). Reachable from the sign-in page.
- **Delete account** — password-confirmed; removes the user, their fingerprint, all per-source
  signatures, and every block involving them, then signs out.

### Data sources (18 connectors)
GitHub (username; optional token), Goodreads (CSV), Netflix (CSV), YouTube (Google Takeout
`subscriptions.csv` — or a token), Google, Facebook, Pinterest, Spotify, Twitter/X, LinkedIn,
Reddit, Last.fm (key + username), Steam (key + Steam ID), TikTok, Instagram, Twitch (token +
client ID), RSS/Blogs (feed URLs), SoundCloud. Most API connectors need an OAuth token or API
key; GitHub, RSS and the three exports need no account credentials. The YouTube export emits
the same `youtube-channel:<slug>` features as the token path, so export users and token users
match on shared channels.

### Self-described interests (no account needed)
- **Curated picker** at `/sources/interests`: ~140 tags across 9 themes. Picks become features
  through the same `FingerprintGenerator` pipeline as any connector, stored as a "Self-described"
  source (signature + count); **the picks themselves are discarded** (asserted against the DB).
- **Free-text interests** for the rare, high-signal ones a fixed list cannot hold. Aggressive
  normalisation (case, spacing, punctuation) so spellings collide; text matching a catalog concept
  maps onto it, otherwise `interest:<slug>`. Hashed and discarded, never rendered. Synonyms are an
  accepted v1 limitation.
- **Cross-pool bridge:** programming-language picks emit the canonical `language:*` feature the
  GitHub connector uses, so a self-describer who picks Python matches a GitHub user who codes
  Python. Music/film genres are not yet bridged.
- **Rarity weighting:** static per-tag weights in `InterestCatalog` replicate rare features in the
  MinHash input, so sharing a niche interest counts for more than sharing a ubiquitous one. No
  frequency oracle, no new retained state. Connector-side weighting is deliberately not applied
  yet (it would silently invalidate existing signatures — needs scheme versioning first).

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
- Results use **qualitative tiers** — "Strong match" (≥35% estimated Jaccard), "Good match"
  (≥15%), "Some overlap" — with the percentage shown as approximate (`~N% shared`), never as false
  precision. The cut-offs are calibrated by a synthetic-population test
  (`InterestSignalResolutionTests`) so that same-niche pairs read Good-or-better ~92% of the time
  and strangers never do; the test fails if calibration drifts.
- **Filter** the list by a shared interest theme (count-only chips), and **sort** by best match,
  "same intent first", or "similar outlook first". Sorting is a stable secondary order on top of
  interest similarity — signals are never blended into a score.
- **Shared-interest reveal (opt-in):** if both people chose interests to show on their card, the
  card leads with the intersection ("You both want to talk about: …"), otherwise with the match's
  own list ("Ask them about: …"). This list is separate from the discarded fingerprint input, so
  the raw-data guarantee is intact.
- Up to 20 matches, ranked. Each card shows the **source types you have in common** and names the
  one you overlap on most ("Closest on RSS/Blogs, ~45% there"), computed by comparing the stored
  per-source signatures. A category, never an interest.
- Each card also shows **how fresh the other person's fingerprint is**, coarsely, and flags anything
  past the same 90-day staleness threshold used for your own sources.
- **The combined fingerprint is a union**, so a wide-ranging profile scores lower against everyone
  even where two people are identical on a source they share. The per-source line is what makes that
  legible; a test pins the effect.
- Empty state distinguishes "you're the first user" from "others exist but none are close
  enough yet".
- **Interest lens on connect:** immediately after a source is connected, a themed, count-only
  summary of what was found ("Programming & tech 5, Music 3") is shown once, giving a brand-new user
  standalone value before anyone has matched them. Themes/counts only — raw features are discarded.

### Public profile (opt-in)
- Optional **bio** (≤ 280 chars) and **contact** line (≤ 120 chars) shown to people
  you match with, and previewed on your own dashboard.
- Optional **connection intent** — a small closed set (friends / collaborators / discussion /
  open) saying what kind of connection you want. The first **non-interest compatibility signal**:
  shown to matches as a separate explainable line ("🎯 Here for: …"), never blended into the
  similarity score. Blank stays private; in export; cleared via profile; removed on deletion.
- Rendered as **plain text** (HTML-escaped by Razor, verified XSS-safe) and **never as a
  clickable link**, to avoid phishing/open-redirect. Blank fields stay private.

### Compatibility signals (each optional, separate, explainable)
- **Connection intent** (above): closed set, mutual intent is highlighted ("You're both here for:
  …"). Adoption nudge on the dashboard.
- **Values / outlook** at `/account/values`: four plainly-worded 5-point items on one axis
  (openness ↔ conservation, Schwartz's public structure — no licensed instrument, no Big Five, no
  political/moral items). Consent is required and enforced server-side. Answers are reduced to a
  signed bucket (−2..+2) and **discarded** (asserted against the DB); retake = re-answer. Matches
  see only a coarse alignment line ("Similar outlook" / "Some overlap" / "Different outlook"), and
  only when both sides took it. Withheld while you are hidden; in export; deletable on its own.
  The signal is **weak on its own** (95% of people land in the middle buckets) and is the most
  sensitive data collected, so hiding it by default is an open owner decision; the
  `Signals:ValuesEnabled` switch hides the questionnaire, the card line and the sort in one flip.
- **Shown interests**: the opt-in list behind the shared-interest reveal (above).
- All three: opt-in, skippable, independently removable, included in export, removed on account
  deletion, and never a hard filter or a blended score.

### Privacy controls
- **Discoverability toggle** — "Hide me from matches" keeps your fingerprint and your own
  match view, but removes you from everyone else's results. Defaults to discoverable. It is
  **reciprocal for personal details**: while you are hidden, other people's bios and contact lines
  are withheld from you, so an invisible account cannot harvest them.
- **Data transparency** — `/account/data` shows exactly what is stored about you, including the
  people you have hidden, with a "what we never store" section and a JSON download
  (`/account/data.json`). Who hid *you* is deliberately not shown, to either party.
- **Hide a specific person** — a "Hide" button on any match card removes that person from your
  matches, symmetrically (neither sees the other). Manage/undo at `/matches/hidden`.
- **Report a person** — a "Report" action on any match card records a closed-set reason and hides
  the reported person from you. One report per reporter per target (unique index), so a single
  actor cannot inflate a count.

### Trust, safety & operations (operator-token-gated)
- Setting `Metrics:Token` enables three operator endpoints, all `404` until then:
  `GET /metrics` (aggregate adoption counts: the funnel — registered, registered last 7 days, with
  fingerprint, fingerprints by source (self-described vs each connector), ever viewed matches,
  returned to the match list ≥1 day after registering, active last 7 days — plus bio/contact/intent/
  values adoption; intent and values breakdowns withheld below a 10-user cohort so nobody is
  identifiable — the source breakdown follows the same rule; all computed from columns already
  stored, no new tracking),
  `GET /metrics/reports` (reported users ranked by distinct reporters, with suspension status) and
  `POST /metrics/suspend` (`{"username":"…","suspend":true|false}`).
- **Suspension is a reversible flag** (`AppUser.SuspendedAt`): a suspended account is removed from
  everyone's matches, its session dies on the next request and it cannot sign in; `false`
  reinstates. Deliberately not a delete, so a leaked token cannot destroy accounts. The token check
  runs before model binding and is constant-time.
- **Anti-sybil registration guard, privacy-preserving** (no third-party CAPTCHA — a tracker would
  contradict the product): an always-on honeypot field plus, when `AntiAbuse:GuardRegistration=true`,
  a Data-Protection-signed, time-limited, single-use form ticket that also rejects submissions
  faster than `AntiAbuse:MinFormSeconds`. Blocks blind, looped and replayed POSTs; the per-IP
  register rate limit is the always-on volume cap. Single-use is tracked in-process.
- `GET /health` — anonymous DB-reachability probe.

### Robustness & failure handling
- Per-connector failures are surfaced to the user; a connector that returns no data is
  treated as a failure (many APIs return an error body with a 200-ish shape).
- Connectors are fetched **concurrently under a 30s overall budget** (as are the RSS feeds within
  the RSS connector), so connecting several sources costs the slowest one rather than the sum, and a
  hung source is reported instead of holding the whole POST until a gateway kills it. The budget and
  the caller going away both reach the connectors as a `CancellationToken`, so outbound work is
  **aborted, not merely abandoned**.
- A fingerprint is **never saved from zero features**; a partial failure keeps the
  sources that succeeded and leaves your existing data intact.
- An oversized connect submission (over the 25 MB pipeline cap) gets a styled "upload too large"
  page with next steps, instead of a raw framework error or the generic error page — checked against
  the endpoint's own size limit before any body reading is attempted, so it fires reliably under
  both Kestrel and the test host.

### Security hardening
- Passwords hashed with **BCrypt**, screened against common passwords / username containment /
  near-single-character strings. Usernames, bios and contact lines reject invisible and
  bidirectional-override characters, a **username must use a single writing system** so a Cyrillic
  lookalike cannot shadow an existing Latin name, and a username that folds to the same form as an
  existing one (case + Unicode compatibility, beyond the ASCII SQLite's NOCASE covers) is refused. **Antiforgery** tokens on every POST (asserted by a reflection test).
- **Login rate limiting** (5 attempts/min per IP, configurable).
- **15s timeout** on all connector HTTP calls; **10 MB cap** per CSV upload, plus a 25 MB
  `RequestSizeLimit` on the connect endpoint so oversized bodies are rejected before buffering.
- **Ghost-session rejection**: a cookie whose user no longer exists is rejected.
- **HTTPS redirect + HSTS** in non-Development; auth cookie marked Secure over HTTPS.
- **Persistent Data Protection key ring** so auth cookies survive restarts/redeploys.
- **SSRF protection** on user-supplied RSS feed URLs: hosts resolving to loopback, private,
  link-local (incl. cloud metadata), CGNAT, unique-local, multicast or reserved addresses are
  refused before any request is made (`SsrfGuard`, fail-closed) — including internal IPv4 hidden
  inside 6to4/NAT64/Teredo IPv6 transition addresses. RSS uses a dedicated client with
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
| `AppUser` | Id, Username, NormalizedUsername, PasswordHash, CreatedAt, Bio?, Contact?, IsDiscoverable, RecoveryCodeHash?, SessionsValidFrom, LastMatchesViewedAt?, ConnectionIntent?, ValuesOpenness?, ValuesScheme?, ShowableInterestsJson?, SuspendedAt? | `NormalizedUsername` is the compatibility-folded, lower-cased comparison form (non-unique index); display casing stays in `Username`. Username is case-insensitive unique (NOCASE). Bio/Contact/ConnectionIntent/ShowableInterestsJson are the opt-in public profile; `ValuesOpenness` is the derived −2..+2 bucket (raw answers never stored) tagged with the `ValuesScheme` it was derived under; `SuspendedAt` is the reversible operator suspension |
| `FingerprintRecord` | UserId (PK), FingerprintJson, SourcesJson, UpdatedAt | The **combined** (truncated) signature used for matching |
| `SourceFingerprintRecord` | Id, UserId, Source, RawSignatureJson, FeatureCount, UpdatedAt | One per (user, source); **raw 64-bit** signature; unique index on (UserId, Source) |
| `FingerprintScheme` | Id, Verifier, UpdatedAt | One row. Records which pepper the stored signatures were built under, so a rotation is noticed instead of silently breaking every comparison |
| `UserBlock` | Id, BlockerId, BlockedId, CreatedAt | One person hiding another; unique on (Blocker, Blocked). Cascades from **both** ends, so a block cannot outlive either party's account deletion |
| `UserReport` | Id, ReporterId, ReportedId, Reason, CreatedAt | One person reporting another with a closed-set reason; unique on (Reporter, Reported); cascades from both ends. Read only through the token-gated operator endpoint |

Schema changes are made with EF migrations and applied on startup via
`db.Database.Migrate()`.

---

## 6. Privacy & security model (the promise, precisely)

1. Each interest is a short string (e.g. `language:python`, `genre:sci-fi`).
2. Strings are hashed (SHA-256) and reduced to a **128-dimension MinHash signature**.
3. **Only the signature is stored. The raw interest strings are discarded.** Every hash is
   HMAC-keyed with a per-deployment secret (`Fingerprint:Pepper`), so a copy of the database on its
   own cannot be tested against a list of guessed interests to find out which were yours.
4. Access tokens / API keys are used once to fetch data and **never persisted**.
5. Signal counts are aggregate numbers only — not the underlying interests.
6. Other users only ever see your **username, a match tier/approximate %, the source
   types you share, and whatever bio/contact you chose to make public.**
7. Where the operator turns on database snapshots (`Backup:Directory`; the shipped container
   and Azure setups do), a deleted account survives in them for at most `Backup:Keep ×
   Backup:IntervalHours` (7 days by default). The snapshots are copies of the same signature-only
   database on the same volume; the privacy page and the delete-account panel state the number.

Two signatures are compared to estimate how much two people's interests overlap
(Jaccard similarity) without either side revealing what those interests are.

---

## 7. Configuration

All settings come from `appsettings.json` or environment variables.

| Key | Default | Purpose |
|-----|---------|---------|
| `ConnectionStrings:Default` | `Data Source=profiler.db` | SQLite database location |
| `RateLimiting:LoginPermitLimit` | `5` | Login attempts per IP per minute |
| `RateLimiting:RegisterPermitLimit` | `5` | Registrations per IP per hour |
| `RateLimiting:ConnectPermitLimit` | `10` | Source-connect submits per IP per minute |
| `DataProtection:KeyPath` | `<contentRoot>/keys` | Where the auth-cookie key ring is stored (git-ignored secret) |
| `Fingerprint:Pepper` | — (**required outside Development**) | Secret mixed into every fingerprint hash. Without it, a stolen database can be tested against guessed interests. Changing it invalidates every signature |
| `ForwardedHeaders:Enabled` | `false` | Believe `X-Forwarded-For`/`-Proto`. **Required behind a proxy**, or every visitor shares one rate-limit bucket |
| `ForwardedHeaders:KnownProxies` | — | Proxy IPs to trust, comma-separated. Enabling without this (or KnownNetworks) is refused at startup |
| `ForwardedHeaders:KnownNetworks` | — | Proxy networks to trust, CIDR form (`10.0.0.0/8`) |
| `Metrics:Token` | — (off) | Operator bearer token. Enables `GET /metrics`, `GET /metrics/reports`, `POST /metrics/suspend`; all `404` until set |
| `AntiAbuse:GuardRegistration` | `false` | Enforce the signed single-use registration form ticket. Turn on for a public launch |
| `AntiAbuse:MinFormSeconds` | `3` | With the guard on, reject a registration submitted faster than this after the form loaded |
| `Signals:ValuesEnabled` | `true` | Set `false` to hide the values questionnaire, the match-card alignment line and the "Similar outlook first" sort; stored buckets are kept for a clean re-enable |
| `Backup:Directory` | — (off) | Folder for rolling SQLite snapshots. Image sets `/data/backups`, Azure bootstrap `/home/data/backups`; unset in dev/tests |
| `Backup:Keep` | `7` | Snapshots kept per kind (`scheduled`, `premigrate`) |
| `Backup:IntervalHours` | `24` | Scheduled cadence, anchored to the newest snapshot on disk |

---

## 8. Running & deploying

```bash
dotnet run --project Profiler.Web     # dev, http://localhost:5000 (see launchSettings)
dotnet test                           # 367 tests, fully offline
```

- **Run behind HTTPS in production** (HSTS + HTTPS redirect turn on outside Development).
- **Behind a proxy, set `ForwardedHeaders:Enabled` and name the trusted proxies** — otherwise
  every visitor arrives as the proxy's IP and shares a single rate-limit bucket. Enabling it
  without naming any is refused at startup, on purpose.
- **Persist the key ring** (`DataProtection:KeyPath`) on a volume so cookies survive
  redeploys; point it at shared storage for multi-instance deployments.
- **Set `Fingerprint:Pepper` once and keep it.** The app refuses to start without one outside
  Development; changing it invalidates every stored signature (detected at startup; everyone must
  reconnect).
- **Schema changes** ship as EF migrations and apply automatically on startup.
- Add a migration: `dotnet ef migrations add <Name> --project Profiler.Web`.
- **Back up.** `Data/DatabaseSnapshots` + `DatabaseSnapshotService` write consistent snapshots via
  SQLite's online-backup API whenever `Backup:Directory` is set: a scheduled one every
  `Backup:IntervalHours` (cadence anchored to the newest file, so a host that recycles the process
  — App Service F1 sleeps — neither piles up copies nor skips a day) and one right before a
  migration touches an established database; `Backup:Keep` newest per kind, and anything of either
  kind older than `Keep × Interval` days deleted on every pass (that is the promise users read).
  The scheduled service logs and retries every failure and never stops the host; the pre-migration
  copy fails closed — an app that cannot write its safety copy refuses to migrate and says why. Copy them off-host (`docker cp profiler:/data/backups .`; Kudu
  `/api/zip/data/backups/` on Azure) and restore by stop → replace file → start — exact commands in
  `README.md` "Back up and restore". `/health` (DB reachability) is what the compose healthcheck
  (bash `/dev/tcp`, since the runtime image has no `wget`/`curl`), the Azure health-check path and
  the deploy workflow's readiness poll all hit.

### Container (any Docker host) — verified 2026-08-08
`Dockerfile` (multi-stage, non-root, `/data` volume, pepper injected at runtime) and
`docker-compose.yml`. Secrets live in a git-ignored `deploy/.env.production` copied from
`deploy/.env.example`. After `docker compose up -d --build`, `deploy/smoke.sh` verifies a running
deployment end to end (core journey, anti-abuse guards firing, raw interests absent from the wire,
operator endpoints token-gated); it only creates throwaway accounts. Every push to `main` (or a
`v*` tag) publishes the image to GHCR as `ghcr.io/kashiashvili/effective-octo-giggle` via
`.github/workflows/deploy.yml` — the repo's Actions workflow permission must be "Read and write".

### Azure App Service — path chosen 2026-09-12, not yet live
One-time setup the owner runs under their own `az login`: `./deploy/azure-bootstrap.sh`. It creates
the resource group, a Linux App Service plan (F1 free by default; `SKU=B1` for always-on, ~$13/mo)
and a container web app, then sets every setting that is easy to get wrong: `WEBSITES_PORT=8080`;
`WEBSITES_ENABLE_APP_SERVICE_STORAGE=true` with the SQLite database and the Data Protection key
ring under `/home/data` (the only persistent path — otherwise every restart wipes accounts and logs
everyone out); forwarded headers enabled *with* the platform's internal networks named; the
registration guard on; a single worker (SQLite corrupts if App Service scales out). It generates
and prints the permanent pepper, the operator token and the publish profile.

Then in GitHub → Settings → Secrets and variables → Actions: variable `AZURE_WEBAPP_NAME`, secret
`AZURE_WEBAPP_PUBLISH_PROFILE`; and make the GHCR package public so App Service can pull it (the
image holds no secrets). From then on the `deploy-azure` job ships every push to `main` — the
exact sha-pinned image, then polls the public URL until it answers 200, because a container that
cannot boot otherwise "deploys" fine and serves 503. The job is skipped entirely until the variable
exists, so the workflow stays green before Azure does. Upgrade the tier in place, no redeploy, no
data loss: `az appservice plan update -g <rg> -n <plan> --sku B1`.

---

## 9. Testing

349 xUnit tests, **fully offline** (~35s, dominated by the integration suite booting the app):
- **Unit:** fingerprint math (incl. the union = element-wise-min property), matcher
  (ranking, threshold, empty exclusion), aggregator (failures), view-model tiers/validation,
  connectors (CSV parsing + garbage handling + API-error handling via a stub HTTP handler),
  interest catalog (allowlist, normalisation, bridge, weights), values derivation, registration
  guard.
- **Integration** (`Integration/`): boots the real app on an isolated temp database (so every
  migration is proven on a fresh DB) and exercises auth, profile, signals, self-described
  interests, matching, hide/report/suspend, export, deletion, operator endpoints — and asserts the
  privacy promise directly against the database (raw interests, picks and questionnaire answers
  are never stored).
- **Synthetic assumption tests** (no real users): `InterestSignalResolutionTests`,
  `ValuesSignalResolutionTests`, `ValuesSortImpactTests`, `InterestWeightingExperimentTests` build
  synthetic populations, run them through the real pipeline and pin the calibrations the product
  relies on. They fail if a threshold drifts.

---

## 10. Known limitations & backlog

- **Sources can't auto-refresh.** Because tokens are never stored (privacy), refreshing a
  source means re-entering its credentials. This is intentional.
- **No in-app messaging.** Matches connect via the contact line each person opts to share.
- **No email address, therefore no reset link.** Recovery is a one-time code the user must keep;
  losing both the password and the code means the account is unreachable by anyone, including us.
- Matching returns the top 20 (filterable by theme, sortable by signal); there is no pagination.
- **Matching is O(all users) in memory.** Every `/matches` request loads and deserialises every
  user's fingerprint, then compares against all of them. Fine at current scale; past a few
  thousand users this needs LSH banding (bucket candidates by signature bands) so each request
  only compares against plausible neighbours.
- **Data Protection keys are unencrypted on the data volume.** Whoever can read the volume can
  forge sessions as well as read the (signature-only) database; rotation = delete the folder
  (signs everyone out). Encrypting at rest needs a certificate or cloud key store — non-goal for
  a single-instance deployment; documented in `README.md` deployment notes.
- **Snapshots live on the same volume as the database.** They cover a bad migration, a bad
  release or accidental data damage, not the loss of the volume itself; copying `backups/` off-host
  is a manual operator step (README "Back up and restore").
- **SQLite means one instance.** Run a single worker (the Azure bootstrap pins it); the
  registration ticket's single-use cache is in-process too, so a multi-instance deployment would
  need a shared cache and a different database.
- **13 of 18 connectors need a self-minted OAuth token or API key.** Pasting tokens into a form
  is a phishing-shaped habit and out of reach for the non-developer target user; the self-described
  path exists so nobody needs them. Culling those connectors (or building real OAuth) is an open
  owner decision.
- **The values signal is weak on its own** (one averaged axis; most pairs land in the middle
  tier) while being the most sensitive data collected. Hiding it by default is recommended and is
  one config flip (`Signals:ValuesEnabled=false`) — an open owner decision. Strengthening it (a
  second Schwartz axis) is gated on real adoption evidence.
- **Cross-pool bridge covers programming languages only.** Self-described music/film picks and
  connector genres still live in different vocabularies and cannot match each other.
- **Connector-side rarity weighting is not applied.** It would change established connector
  signatures and `SchemeVerifier` only detects pepper changes, so old and new signatures would
  silently stop matching. Needs a weighting version folded into the scheme first.

**Open product bets, all gated** (details and recommendations in `docs/OWNER_DECISIONS.md` and
`PROJECT_STATE.md`): promote the vision to multi-signal compatibility (owner); hide the values
signal (owner); go live to gather `/metrics` evidence (owner); connector-side weighting (design);
community-scoped pools (needs a real partner); interest clusters and a return channel (need a
pool); client-side fingerprinting (pepper-on-client problem).

---

## 11. Changelog (newest first)

Each entry: what changed and why it mattered.

### 2026-09-17 (later)
- **YouTube without a token: Google Takeout `subscriptions.csv` upload.** Opportunity Critic #3's
  "bring your own export" bet, smallest increment. The token YouTube connector emits
  `youtube-channel:<slug of title>`; the new `YouTubeSubscriptionsConnector` parses the Takeout
  file (header by name, third column as the localised-header fallback; BOM tolerated; first 50
  distinct channels — Takeout lists everything while the token path sees 30, and wide sets dilute
  Jaccard) into the same features, so someone who uploaded an export and someone who pasted a
  token match on shared channels. Same source name "YouTube", so it refreshes the same per-source
  signature. Parsed in memory, discarded like every CSV. Wired into the connect form's "No account
  needed" group with the Takeout steps, the landing and connect pages' no-token lists, and a
  pointer on the token card. The connect page's own "More sources to come" list (six unbuilt
  sources) is gone for the same honesty reason as the landing's, with its dead CSS. 13 of 18
  connectors now need a token. Tests: Takeout fixture with BOM → exact slugs; localised header;
  cap + dedupe + non-Latin skip; garbage never throws raw; end to end two exports match on
  "YouTube" with no raw title on any page: 367.
- **Release Auditor on the day's units: two P1s fixed, retention promise made literally true.** An
  independent audit of `ab80929..e6a0fcb` found (1) the snapshot service read the backup folder
  outside its guard, so an unreadable `Backup:Directory` threw out of the hosted service and — with
  .NET's default — stopped the whole host (reproduced by the auditor); (2) pre-migration snapshots
  were pruned only by count when the *next* migration shipped, so a deleted account could outlive
  the "≤7 days" the privacy page promised. Fixed: the entire loop body is guarded (log, wait, retry),
  an in-memory database exits cleanly, a missing database file backs off instead of spinning, and an
  age-based prune deletes any snapshot of either kind older than `Keep × Interval` days on every
  pass plus temp files left by an interrupted copy; the privacy copy now says exactly that. The
  pre-migration copy fails closed by design and now logs a critical line saying so. The
  `fingerprintsBySource` breakdown is withheld below the 10-account cohort like the other
  breakdowns (a hidden account's sources are not the operator's to read either), and the return
  metric's proxy nature (only the last visit is stored) is stated. Tests: age prune across kinds
  and stale temp files, unreadable directory keeps the host serving, source breakdown withheld
  below / present above the cohort: 362.
- **Landing and onboarding now lead with the path that actually converts.** Opportunity Critic #3
  (evidence in `PROJECT_STATE.md` §7): the hero and "How it works" said "link your GitHub,
  Goodreads, Netflix", never mentioned the self-described interests list — the funnel the product
  was rebuilt around — advertised six "coming soon" sources with no code or backlog behind them,
  and the recovery-code page's only continue button dropped new users onto the 18-card token
  grid. Now: hero and step 1 lead with "pick your interests from a list", the sources section says
  honestly which four need no account or token and that the rest take a self-made API token, the
  unbacked "coming soon" line is gone (nothing shipped was removed — culling connectors stays an
  owner call, the grid remains), the recovery-code continue goes to `/sources/interests` with
  "connect a source instead" as the secondary link, and the dashboard's quick actions put
  "Describe your interests" first until a fingerprint exists. Tests: landing orders the interests
  path before any platform, names the token honesty line, contains no "coming soon"; recovery page
  links both paths: 359.
- **`/metrics` now answers the questions the first cohort exists to answer.** The state file's untested
  assumptions — do privacy-conscious people fingerprint at all, self-described or via a connector;
  does interest similarity bring anyone back — were "needs live users", but the live endpoint only
  reported adoption of the extra signals. Added, all as aggregate counts over the two timestamps and
  the source names already stored (no new tracking): `registeredLast7Days`, `fingerprintsBySource`,
  `viewedMatches`, `returnedAfterFirstDay` (last plain match-list visit ≥1 day after registration —
  the "try-but-not-return" number), `activeLast7Days`. Source breakdown is not cohort-withheld because
  source types are already shown to every match. Return arithmetic runs in memory over the viewers'
  two dates rather than trusting date translation to SQLite. Also silenced the boot-time EF warning
  10103 (`FirstOrDefault` without ordering on the single-row scheme table). One delta-based
  integration test (three seeded accounts, exact deltas, tag never in payload): 358.
- **Go-live data safety: rolling database snapshots, a working container healthcheck, `/health`
  everywhere.** A fresh-session structural inspection of the recommended go-live path found that
  nothing backed the database up — the state file said "back it up" with no tooling, App Service
  F1/B1 has no platform backup, and the permanent pepper makes lost signatures unrebuildable — and
  that the compose healthcheck called `wget`, which the runtime image does not ship, so the
  container reported `unhealthy` forever (verified in the built image). Shipped: `Backup:Directory`-
  gated snapshots through SQLite's online-backup API (a plain copy of a live database can be torn),
  scheduled every `Backup:IntervalHours` with the cadence anchored to the newest file on disk so a
  sleeping/recycling host neither duplicates nor skips, plus an automatic snapshot of an established
  database right before migrations run; `Backup:Keep` newest per kind; failures log and retry. The
  image and the Azure bootstrap point it at the persistent volume; dev and tests stay off. Because
  "permanently deleted" must stay true including backups, the privacy page and the delete-account
  panel now state the retention window whenever snapshots are on. The compose probe speaks HTTP over
  bash's `/dev/tcp` to `/health`; the Azure health-check path and the deploy workflow's readiness
  poll moved from `/` to `/health`, so a container that boots but cannot open its database (lost or
  unwritable volume) fails the deploy instead of serving errors. README gained a backup/restore
  runbook for compose (`docker cp`, `docker compose run` restore) and Azure (Kudu zip/vfs API with
  the publish-profile credentials). 8 tests (consistent copy, no-op paths, per-kind prune, UTC stamp
  parsing, pre-migration only when pending on an established DB, retention arithmetic, host boots
  and writes a snapshot, disclosure on/off): 357. Live compose verification: rebuilt image reports
  `healthy`, startup snapshot lands on the volume owned by `app`, `deploy/smoke.sh` 16/16, the
  copied-out snapshot passes `PRAGMA integrity_check` with all 16 migrations, a restart adds no
  duplicate, the privacy page shows "7 days". Independent reviewer (read-only) found one docs gap
  (Kudu VFS root), fixed. Restore drill executed with the README's compose command verbatim: live
  database (1 user) replaced by the earlier snapshot (0 users), `/health` 200, container healthy,
  `/metrics` confirms the rollback. Also documented in the owner checklist and README: onboarding a
  group from one network hits the 5-per-IP-per-hour register limit — raise
  `RateLimiting:RegisterPermitLimit` for the session.

### 2026-09-17
- **Documentation consolidated so the loop and the owner read one truth each.** `CLAUDE.md`
  (persistent rules) and `docs/PRODUCT_AGENT.md` (operating manual) had drifted into two copies
  of the same rules with three different names for the loop; the manual now holds only the
  *how* (roles, delegation with concrete agent/model tiers, state-file discipline, a gated-work
  protocol, the discovery techniques that actually produced this product's decisions, the
  validation ladder) and both are written tersely. `PROJECT_STATE.md` was cut from 762 lines of
  history to a current-only snapshot (the history is here, in this changelog). This handbook's
  sections 2–10 were brought up to date with everything shipped since July (signals, self-described
  interests, safety loop, operations, deploy paths); the test count is reconciled everywhere to the
  real figure (349).
- **Operating rules optimised after an independent cold-start review.** A fresh-session reviewer
  confirmed the two-file resume works, then found the real gap: the rules governed the loop but never
  bounded the agent's *external* authority. Fixed: `CLAUDE.md` now forbids pushing, promoting `main`
  (which deploys), running `az`/`gh`/`docker push`, rewriting history, or writing any secret into any
  file unless the owner asks in chat for that specific action; `.claude/settings.json` enforces it
  (push/deploy commands prompt, force-push and history-destroying git commands are denied, build/test
  and read-only git are pre-allowed so an unattended run does not stall) and now fires the loop
  reminder on startup as well as after compaction. Also fixed: one priority scale instead of two, one
  build gate (`dotnet build -warnaserror`, now also in CI), a test-count-regression rule, an explicit
  "unanswered owner brief = stop condition 3" tiebreak, a state-vs-git reconciliation rule, rule-file
  edit discipline, ready-to-use Auditor/Critic/delegation prompts and state/owner-brief templates in
  `docs/PRODUCT_AGENT.md`, and `GOAL_COMMAND.md` reduced to a pointer so it cannot drift again.
  `docs/OWNER_DECISIONS.md` entries now all carry context, evidence, options and a recommendation.

### 2026-09-12
- **Azure App Service deploy path.** Owner chose App Service (Linux container) with publish-profile
  auth and the GHCR image. `deploy/azure-bootstrap.sh` is the one-time setup run under the owner's
  own `az login`: resource group, plan (F1 free by default), container web app, and every setting
  that is easy to get wrong — port, persistent `/home` storage for the SQLite db and key ring
  (otherwise every restart wipes accounts), forwarded headers with the platform's networks named,
  the registration guard on, a single worker. It prints the permanent pepper, the operator token
  and the publish profile. `deploy.yml` gained a `deploy-azure` job that ships the exact sha-pinned
  image and polls the URL until it answers 200; skipped until the `AZURE_WEBAPP_NAME` variable
  exists so the workflow stays green before Azure does.

### 2026-08-08
- **Go live, as far as this environment allows: compose stack, secret template, smoke test.**
  Built and ran the production image with real production config (generated pepper and operator
  token in a git-ignored `deploy/.env.production`, `Production` environment, registration guard on,
  persistent volume). `deploy/smoke.sh` verifies a *running* deployment end to end — core journey,
  anti-abuse guards actually firing, raw interests absent from the wire, operator endpoints
  token-gated — using only throwaway accounts, and treats a 429 from the register limiter as the
  guard working. Verified 16/16, migrations applied on boot, data and key ring survive a restart.
  Public exposure still needs a host, TLS in front and the Actions write permission for GHCR.

### 2026-07-25
- **Owner decision brief + a one-flip values-signal switch.** Consolidated the gated strategic decisions
  (promote the vision; keep/hide/strengthen the values signal; enable the registration guard; go-live
  checklist) into `docs/OWNER_DECISIONS.md` with evidence and recommendations, so the owner can decide
  fast. Pre-built the mechanism for the top recommendation: `Signals:ValuesEnabled` (default **true** — no
  behavior change) hides the values questionnaire, the match-card alignment line, and the "Similar outlook
  first" sort when set false, keeping stored buckets for a clean re-enable. This makes "hide the weak
  values signal" (the data-minimizing default) a single config flip rather than future code work. Values
  disabled → questionnaire not served/collected, no line/sort on matches (2 tests); default-on path
  unchanged (existing values tests pass).
- **Accessibility pass — two heading-hierarchy defects fixed.** The core journey was already
  strong (`lang`, skip-link, `<main>`, `role=status/alert` flashes, label associations,
  `:focus-visible`, no bare `<img>`). Two real WCAG defects: the auth pages used `<h2>` as their
  top heading with no `<h1>`, and the Matches/Hidden empty states jumped from `<h1>` to `<h4>`.
  Both fixed semantically only — class-scoped CSS moved with the tags, rendering pixel-identical.
- **Values sort: coherence fix + impact evidence.** Assumption-tested the values signal's main
  user-facing use (the "Similar outlook first" sort). Finding: with both sides set, it reorders heavily
  (top-1 changes ~66%, ~18/20 positions move) — inherent to an outlook-first sort since outlook and
  interest are independent — while the underlying bucket discriminates weakly (95% cluster in −1..+1).
  So the sort is **high-impact but low-resolution**: opting in lets a coarse/weak signal heavily override
  the strong interest ranking. Recorded as decision-support for the owner's hide/keep/strengthen call on
  the values signal. Coherence fix the test surfaced: `AlignmentRank` ordered by the raw 0–4 distance —
  finer than the three tiers the label shows and finer than the weak signal supports — so two identically-
  labelled matches could be ordered differently on a distinction the UI never displays. Rank now keys to
  the shown tiers; interest breaks ties within a tier.
- **Privacy-preserving registration anti-sybil (honeypot + signed single-use ticket).** Closes the
  pre-launch safety gap without a third-party CAPTCHA — the point of a privacy-first product is not
  embedding a tracker. Two self-hosted layers: an always-on honeypot field (hidden from humans, filled
  by naive bots) and a signed, time-limited, single-use registration form ticket (`RegistrationGuard`,
  Data-Protection-signed, in-process single-use cache) enforced when `AntiAbuse:GuardRegistration=true`
  — blocking scripts that POST blind at the endpoint or stockpile/replay one form load, plus a
  `AntiAbuse:MinFormSeconds` time-trap. Off by default (dev/tests undisturbed); the per-IP register rate
  limit stays the always-on volume cap; single-use is per-instance (multi-instance strict single-use
  wants a shared cache — noted). No JS, no external service, no PII. 6 tests (honeypot always-on,
  guard-off passes, blind-POST rejected, normal flow, replay blocked, time-trap).
- **Integrated user-flow review passed; card-density redesign closed as not warranted.** Walked
  the cumulative product live with a fully-populated worst-case match card (Strong tier, mutual
  intent, values alignment, shared-interest intersection, bio, contact, Hide + Report, all three
  sorts, contact nudge). No regression or cross-feature bug. The card is tall but logically grouped
  and readable, so the recorded redesign bet was closed on evidence rather than built. Seed data
  removed afterwards.
- **Containerised deploy pipeline; repository pushed.** `Dockerfile` (multi-stage, non-root,
  `/data` volume, pepper at runtime), `.dockerignore`, and `.github/workflows/deploy.yml` (test →
  build → push to GHCR with the built-in token on `main`/`v*`/manual). `main` fast-forwarded from
  the working branch to the private GitHub repo. Deployment readiness verified separately: pepper
  enforced outside Development (and the published dev value refused), 16 migrations proven on a
  fresh DB, key ring persisted, `/health` present, HTTPS/HSTS/forwarded-headers documented — a
  small private deployment to gather initial `/metrics` evidence was unblocked from this point.
- **Operator suspend loop — act on reports (reversible).** Completes the safety loop: the operator could
  see reports but not act. `AppUser.SuspendedAt` (migration `AddUserSuspension`) is a **reversible**
  soft-suspend — deliberately not a token-gated hard-delete, which a leaked token could turn into mass
  account deletion. A suspended account is excluded from everyone's matches, has its session refused on
  every request (`OnValidatePrincipal`, mirroring the deleted/pre-cutoff rejection), and cannot log in.
  Operator action: token-gated `POST /metrics/suspend {username, suspend}` (bearer token, a flag flip);
  `/metrics/reports` shows each reported user's suspension status. 4 tests.
- **Report path — safety recourse beyond a silent hide.** With more now shown on cards (bio + contact +
  showable interests) and registration being username+password only, the only prior recourse was a
  silent one-sided hide — a bad actor stayed in everyone else's pool. A "Report" control on the match
  card records operator-facing moderation data (`UserReport`, migration `AddUserReports`; reason from a
  closed set — never free text about another person) and **also hides** the reported user from the
  reporter (immediate self-protection). Idempotent (unique reporter→reported); cascades with either
  account. Operator review: token-gated `GET /metrics/reports` (same token as `/metrics`) ranks reported
  users by distinct reporters with a reason breakdown — necessarily names users (moderation is about
  specific accounts), so behind the operator token, never an ordinary session. Deferred to deployment:
  anti-sybil CAPTCHA (needs a provider; per-IP register rate limit is the interim guard) and an admin
  ban/delete loop. 7 tests.
- **Opt-in shared-interest reveal — a concrete reason to reach out.** A fresh Opportunity Critic found
  the input funnel is now strong but everything *after* the match is thin: a card told a stranger how
  much + which category they share, never *what* — the one thing that would drive an off-platform
  message, discarded by the privacy model. New opt-in **"interests to show on your card"**
  (`AppUser.ShowableInterestsJson`, migration `AddShowableInterests`): a small capped list of plain-text
  labels the user chooses to make public, **separate from the matching fingerprint** (interests used
  for matching are still derived and discarded — the "raw interests never stored" guarantee is
  untouched; this is voluntary disclosure, the same trust model as the bio). The match card leads with
  the **intersection** ("🔖 You both want to talk about: sea kayaking") when both show overlapping
  interests, else offers the match's own list as an icebreaker ("🔖 Ask them about: …"). Withheld while
  the viewer is hidden (bio/contact reciprocity); whitespace/case-folded so different spellings unify.
  Edited on the profile page, shown on the dashboard, in the export, removed on deletion. 10 tests.
- **Free-text custom interests — lift the catalog ceiling on rarity.** A fixed ~140-tag list cannot hold
  the genuinely niche interest that makes the best match, and the rarity-weighting evidence shows rare
  shared interests carry the most signal — so the highest-value interests were unexpressible. The
  self-described picker now has a "type your own, one per line" box. The entity-resolution problem that
  had deferred this is handled by aggressive normalization (`InterestCatalog.NormalizeCustom`: lowercase,
  collapse every run of non-alphanumerics to one hyphen, trim, length/count caps) so "Byzantine History!",
  "byzantine  history", and "Byzantine-History" all become `byzantine-history` and match; synonyms are an
  accepted v1 limitation. A typed interest that matches a catalog concept is mapped onto that concept's
  feature (typing "python" unifies with picking Python and with a GitHub user); otherwise it becomes
  `interest:<slug>` in the shared "Communities & topics" namespace. Text is only hashed and discarded —
  never stored (DB-asserted) or rendered (no XSS). Two people who type the same interest differently
  match (integration-tested). 15 tests.
- **Cross-pool matching: bridge self-described interests into the connector vocabulary.** Self-described
  interests live in a `self-*` namespace, so a self-describer and a GitHub user who both love Python
  could never match — self-described had grown a *parallel* pool instead of the existing one, capping its
  value and hurting match density. For the unambiguous 1:1 concepts (programming languages, which GitHub
  always emits as lowercased `language:*`), the self-described build now emits the **canonical connector
  string instead of** the `self-*` one (`self-tech:python` → `language:python`), so the interest lands in
  the shared vocabulary and matches across sources. Emitting *instead of* (not in addition to) avoids
  double-counting one interest, which would inflate self-to-self similarity and undo the rarity weighting.
  Fuzzy concepts (music/film genres, where each platform uses its own vocabulary) are left un-bridged for
  now. Verified: a self-describer who picks Python matches a seeded GitHub user with `language:python`;
  self-to-self matching and connector-only matching unregressed; picks still discarded. 3 tests.
- **Rarity-weighted self-described matching (no new retained data).** Sharing a rare interest predicts
  a real connection far better than sharing a popular one; a synthetic experiment measured ~6x better
  separation from weighting. Shipped it for self-described interests without a per-user frequency table
  (which would be retained state against the north star): the catalog is finite, so rarity is authored
  statically (`InterestCatalog` common/rare slug sets → weight 1/2/3) and applied by **feature
  replication** — a weight-w interest is expanded to w distinct sub-features before MinHash, so two
  people who share a niche agree on more slots than two who share a popular thing. No new storage, no
  scheme/pepper change; connector features keep weight 1 (down-weighting open-vocabulary connector
  commons would need a frequency oracle, deliberately deferred). Verified through the real pipeline: a
  rare shared interest scores above a common one; identical picks still match; picks (and their
  replicas) are still discarded. Note: the effect is modest for *self-described* picks (deliberate, not
  auto-emitted noise); the larger win is on connector features and remains gated on the deferred oracle.
- **Self-described interests — the funnel unblock.** Opportunity-Critic finding: the connect funnel
  is effectively developer-only — 14 of 18 sources demand a self-minted OAuth token / API key, so a
  normal privacy-conscious person with no GitHub and no patience for CSV exports could not produce a
  fingerprint *at all*, and the core promise was unreachable for the actual target user. Fix: a
  curated interest picker at `/sources/interests` (`InterestCatalog`, ~140 tags across 9 themes). A
  user ticks what they're into → each tick becomes a `self-<theme>:<slug>` feature → run through the
  **same** `FingerprintGenerator` pipeline as any connector, stored as a "Self-described" source (raw
  signature + count), and the **picks themselves are discarded** — asserted against the DB, mirroring
  the connector privacy model. Because it is stored like any source, export, account-deletion,
  disconnect, and matching treat it uniformly (free parity). Two people who tick the same interests
  cross-match. Server-side allowlist validation blocks injecting arbitrary features. The one-shot
  interest lens themes the picks correctly (new `self-*` prefixes added to `InterestLens`). No new
  matching code. Removes the dev-only barrier and gives zero-connected-account privacy-maximalists a
  full-strength interest fingerprint. Unit + 4 integration tests (privacy round-trip, two-user match,
  crafted-input rejection, re-pick/export parity) + catalog integrity; verified live end to end.
- **Assumption test → recalibrated match tiers (the core signal).** The values axis got a
  resolution simulation; the *core* interest signal never had, so its tier cut-offs (Strong ≥60%,
  Good ≥30%) were unexamined guesses on a naïve 0–100 scale. A synthetic-profile simulation (no real
  users, `InterestSignalResolutionTests`) showed MinHash Jaccard over interest *sets* runs low: even
  two users who share a whole niche sit at **median ~0.21, P90 ~0.34, almost none above 0.50**, while
  strangers sit at ~0. Under the old cut-offs **"Strong match" was unreachable (0% even for same-niche
  pairs)** and only ~17% of genuine matches cleared "Good" — real matches were mislabelled "Some
  overlap". Recalibrated to the realistic range (**Good ≥15%, Strong ≥35%**): same-niche pairs now read
  Good-or-better **92%** of the time (Strong a rare-but-earned 8%), and strangers still read "Some
  overlap" 100% of the time. Also confirmed the fingerprint is faithful — the 128-hash estimate tracks
  true Jaccard within a mean absolute error of 0.006. CSS tier classes (high/medium/low) unchanged, so
  the card styling is untouched; only the thresholds moved. The resolution test asserts against the
  real `MatchViewModel` tiers, so it fails loudly if the calibration ever drifts again.
- **Match-card similarity bar rescaled to match the new tiers.** The bar used to fill to the raw
  percent, so after the recalibration a top-tier "Strong match" (35%+) would have drawn a barely
  third-full bar — the visual contradicting its own label. `MatchViewModel.BarPercent` now maps the
  realistic Jaccard range (0–50%) onto the full width, so the fill tracks the tier (Good floor ~30%
  full, Strong floor ~70%, top of the range full), clamped to a 3–100% sliver. The honest estimate
  stays in the "~X% shared" text beside the bar; only the visual was rescaled, never the number.

### 2026-07-24
- **Assumption test → recalibrated values alignment.** A synthetic-profile simulation (no real
  users) showed averaging four items clumps 95% of people into the middle buckets, so the old
  "within one = similar" label read 77.6% of random pairs as "Similar outlook" — nearly meaningless.
  Recalibrated so an exact match is "Similar" (now 31% of random pairs), one–two steps "Some
  overlap", further "Different". Recorded finding: one averaged axis discriminates weakly, so a
  second axis / finer scoring is the right way to strengthen values matching — evidence for that
  deferred decision rather than a guess.
- **Release audit of the multi-signal expansion (independent) — clean, five P3s fixed.** No
  P0/P1/P2. Fixes: the "new since last visit" marker now advances only on the plain matches view (a
  sort/filter click no longer zeroes it); `/metrics` withholds the per-category intent/values
  breakdowns below a 10-user cohort to prevent re-identifying one person; the data export now
  includes `ValuesScheme` + `LastMatchesViewedAt`; the values sort keys off a numeric rank rather
  than a display string; the duplicate consent error is gone. Verified clean by the auditor: raw
  questionnaire answers are discarded (DB-asserted), consent is server-enforced, `/metrics` leaks no
  per-user data and is token-gated with a constant-time compare.
- **Matches: user-controlled sort by shared intent or values.** The intent and values signals were
  displayed but changed nothing about matching. The matches page now offers an explicit, user-chosen
  ordering — "Best match" (default), "Same intent first", "Similar outlook first" — a stable
  secondary sort, never a hidden blended score: interest similarity stays the ranking within each
  group and the default/tiebreaker. Options appear only when the viewer set the relevant signal, and
  compose with the source filter. Gives the multi-signal work its payoff (signals now affect what you
  see, on your terms). Integration-tested.
- **Measurement: token-gated aggregate adoption metrics.** So the owner can see whether the
  compatibility signals are used before investing in more, a `GET /metrics` endpoint reports counts
  and distributions from existing columns only (total users, connected, intent by type, values by
  bucket, bio/contact) — never a user id or per-user row, no new retention. Off unless `Metrics:Token`
  is set (404s otherwise), then bearer-token gated with a constant-time compare; an ordinary session
  does not grant access. Four integration tests. Records the PO decision that measurement gates
  adding further signals (e.g. a second values axis) over growing sensitive-data collection blindly.
- **Multi-signal compatibility — signal 2: optional values/worldview.** Behind the same privacy
  model as connection intent. A consent-gated opt-in questionnaire (`/account/values`) asks a few
  plain openness-vs-conservation statements (own wording, no licensed instrument, grounded in
  Schwartz's public two-axis structure), derives a single coarse bucket (−2..+2), stores only that
  plus a scheme version, and **discards the raw answers** — asserted against the database. Shown to
  matches as coarse alignment only ("🧭 Similar / Different outlook"), never the answers, never a
  number, never a blended score, withheld while hidden. In the export as the derived bucket;
  removable any time; removed on deletion. Framed as optional and non-clinical. `ValuesQuestionnaire`
  derivation (reverse-scored, 15 tests) + `AppUser.ValuesOpenness`/`ValuesScheme`
  (migration `AddValuesOpenness`) + 4 integration tests. Verified live.

### 2026-07-22
- **Multi-signal compatibility — slice 2: mutual intent highlight.** Intent only signals
  compatibility when it is shared, so a match who is here for the same thing as the viewer is now
  emphasised ("You're both here for: collaboration") instead of just showing their choice. Still a
  separate explainable line; no score; no re-ranking; withheld while hidden.
- **Multi-signal compatibility — slice 1: optional connection intent.** Product Opportunity Discovery
  evaluated an optional values/worldview questionnaire and decided to expand toward privacy-preserving
  *multi-signal* compatibility **incrementally, lowest-risk signal first** — deferring the values
  questionnaire (Schwartz-grounded; licensing + sensitivity) and rejecting Big Five (clinical
  overclaim), political/moral items (filter-bubble risk), verbatim licensed instruments, and any
  blended "compatibility %". The first signal is **connection intent**: an optional closed-set field
  (friends / collaborators / discussion / open) for what kind of connection you want. Sharing
  interests is not the same as wanting the same thing; this closes that gap. Follows the opt-in
  profile pattern (blank private, shown to matches as a separate explainable line, withheld while
  hidden, in export, cleared via profile, removed on deletion); never re-ranks matches. `AppUser.
  ConnectionIntent` (migration `AddConnectionIntent`), unit + integration tests, verified live.
- **Discovery: filter matches by shared interest area** — the vision is niche interests, so "show me
  the people I share Music with" is a core move. The matches page now shows a chip row of the source
  types you share with your matches; clicking one narrows the list. Filtering is view-only — the
  retention count and empty-state distinction are computed on the full set. Unknown source values are
  ignored (no blank page), an empty filter result explains itself, and chips appear only with 2+
  areas to choose from. Three integration tests. (First increment of the "let users express what
  kind of similarity matters" opportunity.)
- **Growth: an invite link on the empty-matches state** — the empty state told users to "invite
  friends" but gave no mechanism. It now shows a shareable sign-up link (built from the request, so
  correct behind a proxy) with a copy-to-clipboard button, progressive-enhancement (the link is a
  visible selectable field without JS). No referral tracking, no new data — "we never see who you
  invited" is literally true. Closes the third cold-start lever (grow the network); the first two
  (immediate value, a reason to return) shipped alongside.
- **Retention: "new since your last visit"** — the product collects no email, so it can't notify
  anyone out of band; the only reason to return has to arrive when they do. The matches page now
  records when you last looked and, next visit, tells you how many matches have refreshed their
  interests since — suppressed on the first visit and when nothing is new. Just a per-user timestamp
  (`LastMatchesViewedAt`), no new personal data. Two integration tests.
- **Onboarding: a "what we found" interest lens** — the product's biggest weakness was cold-start,
  not a defect: a new user connected, hit an empty match list, and had no reason to stay. The moment
  the fingerprint is built, the raw features are now summarised into a themed count-only breakdown
  ("Programming & tech 5, Music 3") and shown once on the matches page, so even a first user with
  zero matches sees something about themselves — and the privacy promise becomes concrete (here is
  what we saw, now discarded). Only theme names and counts travel (TempData); the raw features are
  never stored and never placed in the cookie. `InterestLens`, 6 unit tests, verified live.
- **Product decision — contact model kept opt-in (owner, 2026-07-22).** Independent review flagged a
  mutual "connect request" (contacts exchanged only when both accept) as the top *potential*
  improvement. Weighed as a design change, not a defect: the current model is opt-in, labelled
  "shown to people you match with", withheld while you are hidden, and inert plain text. The owner
  chose to keep the simpler privacy-by-default model as final rather than build the consent flow.
  Recorded so the decision, and the alternative, are not silently revisited.
- **Two hardening gaps from an independent review** — (1) the SSRF guard judged an IPv6 *transition*
  address (6to4 `2002::/16`, NAT64 `64:ff9b::/96`, Teredo `2001:0::/32`) by its outer public prefix,
  so a wrapped `127.0.0.1` or `169.254.169.254` slipped past on a host with the matching egress path;
  the embedded IPv4 is now extracted and re-checked, while a transition address to a genuinely public
  host still passes. (2) A username of pure combining marks or punctuation cleared the length minimum
  but rendered as an empty/garbled avatar — a name now needs at least one letter or digit. Both
  covered by tests with positive controls.
- **A username that reads as an existing one is refused** — SQLite's `NOCASE` index folds only ASCII,
  so `André` and `ANDRÉ` could both register and read as the same person on a match card. A
  `NormalizedUsername` column (compatibility-folded and lower-cased, accents kept — `André` and
  `Andre` stay distinct) is stored beside the display name and checked at registration. The column is
  additive with a **non-unique** index (a `UNIQUE` one would fail to create on any existing database
  already holding an accidental lookalike pair), and pre-existing rows are backfilled once at startup
  since SQLite cannot compute a Unicode fold in SQL. Residual: a narrow concurrent-double-insert race
  on the accented case; the ASCII-identical case stays hard-blocked by the `NOCASE` unique index.
- **Two security-branch coverage gaps closed** — the pepper-rotation purge (a data-destroying startup
  path, previously only manually checked) and the session cutoff's fail-closed branch (a missing or
  unreadable issue-time claim must reject) both have direct tests now.

### 2026-07-21
- **Outbound work is now aborted rather than abandoned** — the 30s budget could only stop *waiting*,
  since `IConnector.FetchAsync` took no `CancellationToken`. The claim that each client's 15s timeout
  ended things shortly was untrue for multi-request connectors (RSS follows up to three redirect hops
  across ten feeds, each hop with its own timeout), so one client could leave requests running long
  after the response was sent. The token now runs the whole way: the aggregator links the budget with
  the caller's `RequestAborted`, every connector forwards it to each outbound call, and the SSRF
  guard's DNS lookup takes it too — the one step of a feed fetch no HttpClient timeout covers.
  Cancellation is deliberately not swallowed by the connectors' catch-all handlers, which would
  otherwise report it as "returned no interest data".
- **The stored fingerprint was reversible by anyone holding the database; it no longer is.** The
  product tells people their interests cannot be read back out of what is stored, and against a
  database thief that was untrue: the MinHash parameters came from a published constant, features
  were hashed with unsalted SHA-256, and the per-source signatures are stored full-width — so since
  interest labels come from a small guessable vocabulary (`language:python`, `genre:sci-fi`), an
  attacker could hash each guess and look for it in a stored slot. A test now runs exactly that
  attack: it recovers **20 of 20** interests when the attacker knows the secret and **none** when
  they don't, so the protection is asserted rather than assumed. Every hash, including the
  derivation of the hash family itself, is now HMAC-keyed with a per-deployment
  **`Fingerprint:Pepper`**; outside Development the app refuses to start without one, or with the
  published development value. Because rotating it would silently invalidate every signature
  (they would compare against nothing, forever, with no error), a one-row `FingerprintSchemes`
  table records a verifier derived from the pepper, and a mismatch clears the fingerprints so
  users are asked to reconnect. Verified live: the dev database's pre-pepper signature was
  detected and cleared on first boot, and matching works afterwards.
- **Changing your password now ends every other session.** Cookies are persistent for 30 days and
  the only per-request check was "does this user still exist", so a stolen session survived a
  password change and a recovery reset alike — the one remediation offered to a compromised user
  did nothing. Sign-in now stamps the ticket with its issue time and `AppUser` carries a cutoff
  that password changes and recovery both move; the session doing the change is re-issued so the
  person who asked stays signed in. Adds an explicit **"sign out on every other device"** for the
  borrowed-laptop case, where the password is not the problem.
- **Discoverability is reciprocal for personal details.** It was enforced one way only: an account
  could stay permanently invisible, read up to twenty bios and contact lines on every refresh, and
  never be hidden in return — hiding someone requires seeing their card first. Contact lines and
  bios are now withheld while you are hidden. Similarity and shared sources still show; only the
  personal half is reciprocal.
- **Connecting a source is now one transaction.** The per-source rows and the combined fingerprint
  were two separate commits, so a failure between them left the dashboard showing a freshly
  connected source while matching kept using the old signature — permanently, since nothing else
  recomputes it.
- **The 429 page tells the right people apart.** One handler served all three limiters with login's
  wording, so someone rate-limited after filling in the longest form in the product was told their
  sign-in attempts were paused and offered a link to sign in. Each policy now has its own wording
  and its own way back.
- **Username length is measured after trimming**, so `"  a  "` no longer satisfies the
  three-character minimum and then get stored as `a`.
- **An oversized upload is now explained rather than dumped** — `[RequestSizeLimit]` fires while the
  request body is read, long before the action and its friendly per-file check, so a too-large CSV
  produced a raw framework error page and the user lost everything typed into the form (file inputs
  cannot be repopulated by a browser). Oversized submissions now get a styled 413 stating both limits
  and what to do about them. It is caught two ways: the advertised `Content-Length` is compared
  against the endpoint's own declared limit before a byte is buffered, and `BadHttpRequestException`
  is caught as a fallback for bodies with no upfront length. The precheck also matters for coverage —
  `TestServer` does not implement `IHttpMaxRequestBodySizeFeature`, so the exception path alone could
  never be exercised by the integration suite.
- **The TempData cookie is confined to HTTPS** — TempData rides in a cookie, and since recovery
  landed it carries a freshly issued recovery code on its way to the page that displays it. The
  payload is Data Protection encrypted, but the cookie defaulted to being sent over plain HTTP too;
  it now follows the auth cookie's `SameAsRequest` policy.
- **Usernames may no longer mix alphabets** — the earlier text rules caught invisible and
  direction-flipping characters, but a Cyrillic "а" is simply indistinguishable from a Latin one, so
  `аdmin` could register and sit next to `admin` on a match card with nothing to tell them apart. The
  username is the only thing a stranger sees before deciding whether to make contact, so it now has
  to be written in a single writing system. Wholly Cyrillic, Greek, Japanese or any other single-
  script name is unaffected; kanji and kana count as one system, since ordinary Japanese names use
  both. Not yet closed: a name written *entirely* in lookalikes (`сор` vs `cop`) — that needs a
  confusable-skeleton column with a unique index, and is recorded in the backlog.
- **Matches now say what kind of overlap they are, not just how much** — a card gave a single number
  and the source types in common, so two people were told they were a "Strong match" with no idea
  what they actually shared, which is the main reason a match never becomes a message. The per-source
  signatures needed to answer that were already stored and had never been compared to anything. Cards
  name the strongest shared source and its own overlap ("Closest on RSS/Blogs, ~45% there") beside
  the overall figure. This also explains a number the combined fingerprint deflates: that fingerprint
  is the *union* of every source, so a wide-ranging profile scores lower against everyone even where
  the two people are identical on what they share. Ranking is unchanged — Jaccard of the union is a
  defensible definition of overall similarity, and the per-source line is what makes it legible
  rather than mysterious. Verified live: two accounts sharing feeds but not repos read ~25% overall,
  "Closest on RSS/Blogs (~45% there)".
- **A forgotten password is no longer fatal** — there was no reset path, and because deletion is
  password-confirmed, a locked-out user could not even remove themselves: their fingerprint stayed in
  everyone else's match pool forever and their username stayed squatted. No email address is
  collected (that is the point of the product), so recovery is a **one-time code** instead: 100 bits
  in Crockford base32 — no I/L/O/U, since it gets written down and typed back — shown exactly once at
  registration and stored only as a BCrypt hash. It resets the password in a single post, is
  single-use, and immediately issues a replacement so recovering never leaves the account with no way
  back. A wrong code and an unknown username give the same message, and the endpoint shares the login
  rate limiter. Signed-in users can regenerate a code, password-confirmed; accounts created before
  this have none and are told so on the dashboard. Verified end to end against a running server,
  including typing the code back lowercase and without dashes.
- **Rate limiting now survives a reverse proxy** — every limiter partitions on the connecting IP, and
  behind the TLS-terminating proxy the deployment notes describe, that address is the *proxy's* for
  every visitor. All of them shared one bucket, so the login limit was five attempts per minute for
  the entire site rather than per person — a self-inflicted outage waiting for the first busy hour.
  `ForwardedHeaders:Enabled` plus `KnownProxies`/`KnownNetworks` makes the app read the real client
  address (and the original scheme, which also fixes the HTTPS-redirect trap noted below). It is off
  by default and **enabling it without naming trusted proxies is refused at startup**: the headers
  are attacker-supplied, so trusting them from any caller would let anyone forge a fresh client
  address per request and bypass the limiters entirely — worse than the problem being fixed.
- **Registration and connect are rate-limited too** — only login was metered. Unlimited registration
  lets one person flood the matching pool with sybil accounts, which degrades match quality for real
  users and is the enabling step for harvesting the contact lines matches expose; an unmetered
  connect endpoint lets the host be used to hammer third parties, since every submit fans out to
  external APIs and user-supplied RSS URLs. Both now use the same per-IP fixed-window limiter and
  styled 429 page as login: `RateLimiting:RegisterPermitLimit` (5/hour) and
  `RateLimiting:ConnectPermitLimit` (10/minute).
- **Match cards show how fresh the other person's fingerprint is** — tokens are never stored, so
  refreshes are manual and an abandoned profile never decays; a year-old snapshot was presented as a
  "Strong match" with nothing to say so. Cards now carry a coarse age ("updated this month", "updated
  about 7 months ago") and flag anything past the same 90-day staleness threshold used for your own
  sources. The wording is deliberately coarse — a match's exact activity time is nobody else's
  business.
- **Match cards say when there is no way to reach someone** — a card showed a username, a tier and a
  percentage, and if that person had left their contact line blank it simply ended there, turning the
  step the product exists for into a dead end with nothing explaining it. Cards now say so, and note
  when the other person can still reach you. Anyone who left their *own* contact blank is told once,
  above the list, that nobody there can answer them, with a link to set one.
- **Connecting sources no longer costs the sum of every timeout** — connectors were awaited one at a
  time, and the RSS connector awaited each of its ten feeds one at a time, each able to sit on the 15s
  client timeout. A realistic submission could run for minutes inside a blocking POST, long enough for
  a typical gateway to return 504 and lose everything — including uploaded CSVs, which a browser
  cannot repopulate. Connectors now fan out together, as do the RSS feeds within their connector,
  under a **30s overall budget**: whatever answered is saved and anything outstanding is reported as
  having taken too long. Results are still ordered by connector, not by who answered first. Verified
  live against three real feeds: 2s, 67 signals.
- **Blocks no longer outlive a deleted account** — deletion removed the user, their fingerprint and
  their source signatures, but left every `UserBlocks` row referring to them, and the table had no
  foreign keys at all. What survived was a record that two named people wanted nothing to do with
  each other, one of whom had asked to be erased. Blocks now cascade from both ends at the schema
  level (migration `CascadeUserBlocks`, which also purges the orphans already accumulated) and the
  delete path removes them explicitly as well, so the promise does not depend on the provider
  enforcing foreign keys. `/account/data` and the JSON export now also list **the people you have
  hidden**, which they omitted while claiming to show everything stored about you; who hid *you*
  stays unlisted, since telling someone they were hidden would defeat the control.
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
