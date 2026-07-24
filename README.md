# Profiler – Privacy-Preserving Social Matching

A web application that gathers interest signals from data sources **you choose to
connect**, turns them into a **privacy-preserving MinHash fingerprint**, and
connects you with like-minded people — without ever storing your raw personal
data.

Built with ASP.NET Core MVC, Entity Framework Core, and SQLite.

---

## Features

- **User accounts** – register and sign in securely (passwords hashed with BCrypt)
- **Multi-source data collection** – 18 connectors: enter a GitHub username, upload
  Goodreads/Netflix CSV exports, or paste OAuth tokens / API keys for the rest
- **Privacy-preserving fingerprinting** – a MinHash algorithm turns your interests
  into a comparable signature; raw feature data is never persisted
- **Independent source management** – add, refresh, or disconnect sources one at a
  time; a per-source signature is kept (still no raw data) and the matching
  fingerprint is rebuilt as the element-wise minimum
- **Similarity matching** – ranked list of users with compatible profiles
  (estimated Jaccard similarity), with a noise floor and qualitative match tiers
  instead of false-precision percentages
- **Optional public profile** – add a short bio and a way to be reached, shown only
  to people you match with; rendered as plain text (never a live link)
- **Account controls** – persistent cookie sign-in, a one-time recovery code (no email
  address is collected, so there is no reset link), per-source disconnect, and
  password-confirmed account deletion that removes all of your data

---

## Supported data sources

| Source        | Method                                                                    |
|---------------|---------------------------------------------------------------------------|
| **GitHub**    | REST API – enter your username (public data; token optional)              |
| **Goodreads** | Export CSV from [goodreads.com/review/import](https://www.goodreads.com/review/import) |
| **Netflix**   | Export CSV from [netflix.com/viewingactivity](https://www.netflix.com/viewingactivity) |
| **Google**    | OAuth 2.0 access token (People API)                                       |
| **Facebook**  | OAuth 2.0 access token (Graph API)                                        |
| **Spotify**   | OAuth 2.0 access token                                                    |
| **YouTube**   | OAuth 2.0 access token                                                    |
| **Twitter/X** | OAuth 2.0 access token                                                    |
| **LinkedIn**  | OAuth 2.0 access token                                                    |
| **Reddit**    | OAuth 2.0 access token                                                    |
| **Instagram** | OAuth 2.0 access token                                                    |
| **TikTok**    | OAuth 2.0 access token                                                    |
| **Pinterest** | OAuth 2.0 access token                                                    |
| **SoundCloud**| OAuth 2.0 access token                                                    |
| **Twitch**    | OAuth token + client ID                                                   |
| **Last.fm**   | API key + username                                                        |
| **Steam**     | API key + Steam ID                                                        |
| **RSS feeds** | Comma-separated feed URLs                                                 |

---

## Privacy model

Raw feature strings (e.g. `language:python`, `genre:sci-fi`) are processed by a
**MinHash** algorithm and never stored:

1. Each feature is hashed to an integer via SHA-256
2. *k* independent universal hash functions each produce a minimum value over all features
3. Only the resulting *k*-dimensional integer signature is saved

Two signatures can be compared to estimate *Jaccard similarity* without
revealing any underlying data. Access tokens are used once to fetch data and
are never persisted.

---

## Getting started

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later.

```bash
dotnet run --project Profiler.Web
# → http://localhost:5000 (see Profiler.Web/Properties/launchSettings.json)
```

The SQLite database (`profiler.db`) is created and migrated automatically on first
run (EF Core migrations in `Profiler.Web/Migrations/`).

---

## Configuration

All settings can be supplied via `appsettings.json` or environment variables.

| Key                              | Default              | Purpose                                              |
|----------------------------------|----------------------|------------------------------------------------------|
| `ConnectionStrings:Default`      | `Data Source=profiler.db` | SQLite database location                        |
| `RateLimiting:LoginPermitLimit`  | `5`                  | Allowed login attempts per IP per minute             |
| `RateLimiting:RegisterPermitLimit` | `5`                | Allowed registrations per IP per hour                |
| `RateLimiting:ConnectPermitLimit`  | `10`               | Allowed source-connect submits per IP per minute      |
| `DataProtection:KeyPath`         | `<contentRoot>/keys` | Where the auth-cookie key ring is persisted          |
| `Fingerprint:Pepper`             | — (**required**)     | Secret mixed into every fingerprint hash             |
| `Metrics:Token`                  | — (off)              | Bearer token for `GET /metrics` (aggregate adoption counts); route 404s unless set |
| `ForwardedHeaders:Enabled`       | `false`              | Believe `X-Forwarded-For`/`-Proto` (set this behind a proxy) |
| `ForwardedHeaders:KnownProxies`  | —                    | Proxy IPs to trust, comma-separated. Required when enabled |
| `ForwardedHeaders:KnownNetworks` | —                    | Proxy networks to trust in CIDR form, e.g. `10.0.0.0/8` |

## Deployment notes

- **Run behind HTTPS.** In non-Development environments the app enables HSTS and
  HTTPS redirection, and the auth cookie is only sent over HTTPS.
  Note that ASP.NET's HTTPS redirection **silently does nothing if it cannot determine
  the HTTPS port**. If TLS is terminated by a proxy, set `ASPNETCORE_HTTPS_PORT` (or
  configure forwarded headers) — otherwise requests will simply be served over HTTP
  with no redirect and no warning.
- **Behind a proxy, configure forwarded headers.** Every rate limiter partitions on the
  connecting IP. Behind a TLS-terminating proxy that address is the *proxy's* for every
  visitor, so all of them share one bucket and the login limit becomes five attempts per
  minute for the whole site rather than per person. Set `ForwardedHeaders:Enabled=true`
  **and** name the proxies in `ForwardedHeaders:KnownProxies` / `:KnownNetworks`. Enabling
  it without naming anything is refused at startup: the headers are attacker-supplied, and
  trusting them from any caller lets anyone forge a fresh client address per request and
  walk straight past the limiters. This also gives HTTPS redirection the original scheme.
- **Set `Fingerprint:Pepper` and keep it.** The app refuses to start without one outside
  Development. It is what stops a stolen database being tested against a list of guessed
  interests — the fingerprint scheme is otherwise entirely public, and interest labels come from
  a small vocabulary. Changing it invalidates every stored signature: the app detects that at
  startup, clears them, and everyone has to reconnect their sources.
- **Persist the key ring.** Auth cookies are protected by the Data Protection key
  ring at `DataProtection:KeyPath`. Mount this on a persistent volume (or point it
  at shared storage for multi-instance deploys) so cookies survive restarts and
  redeploys. The keys are secret material and are git-ignored.
- **Schema changes** go through EF Core migrations
  (`dotnet ef migrations add <Name> --project Profiler.Web`), applied automatically
  on startup — no manual database steps.
- **Health probe:** `GET /health` returns `200 {"status":"healthy"}` when the database
  is reachable, `503` otherwise. Anonymous, and reports nothing beyond reachability.

---

## Running tests

```bash
dotnet test
```

The suite (271 xUnit tests) is fully offline — connector tests use a stub HTTP
handler, and integration tests (`Profiler.Web.Tests/Integration/`) boot the real
app against an isolated temporary database.

---

## Project layout

```
Profiler.sln
Profiler.Web/
├── Connectors/          One IConnector implementation per data source
├── Profile/
│   ├── ProfileAggregator.cs     Merges features from multiple connectors
│   ├── FingerprintGenerator.cs  MinHash signature generation
│   └── ProfileFingerprint.cs    Signature comparison (Jaccard estimate)
├── Matching/            Similarity-based user matcher
├── Controllers/         Account, Sources, Matches, Home
├── Data/                EF Core DbContext + models (users, fingerprints)
├── Migrations/          EF Core migrations (applied on startup)
├── ViewModels/
├── Views/               Razor views
└── wwwroot/             Static assets
Profiler.Web.Tests/
├── *Tests.cs            Unit tests (fingerprint, matcher, connectors)
└── Integration/         WebApplicationFactory HTTP/auth-flow tests
```
