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
  Goodreads/Netflix/YouTube (Takeout) exports, paste RSS feed URLs, or paste OAuth tokens /
  API keys for the rest
- **Privacy-preserving fingerprinting** – a MinHash algorithm turns your interests
  into a comparable signature; raw feature data is never persisted
- **Independent source management** – add, refresh, or disconnect sources one at a
  time; a per-source signature is kept (still no raw data) and the matching
  fingerprint is rebuilt as the element-wise minimum
- **Similarity matching** – ranked list of users with compatible profiles
  (estimated Jaccard similarity), with a noise floor and qualitative match tiers
  instead of false-precision percentages, and a "you're in their top matches too"
  badge so you know when reaching out is mutual
- **Optional public profile** – add a short bio and a way to be reached, shown only
  to people you match with; rendered as plain text (never a live link)
- **Circles** – start a named group and share its invite link (signed, 30 days, never stored); people from
  the same circle carry a "Same circle" chip on each other's matches and can be sorted first. Never a
  filter, never part of a score
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
| **YouTube**   | Google Takeout `subscriptions.csv` upload (no token) — or an OAuth 2.0 access token |
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

### Deploy (verified path)

```bash
cp deploy/.env.example deploy/.env.production
echo "Fingerprint__Pepper=$(openssl rand -base64 32)" >> deploy/.env.production
echo "Metrics__Token=$(openssl rand -base64 24)"      >> deploy/.env.production
docker compose up -d --build
BASE=http://localhost:8080 MT="<Metrics:Token>" ./deploy/smoke.sh
```

`deploy/smoke.sh` verifies a running deployment end to end — the core journey, that the anti-abuse
guards actually fire, that raw interests never appear on the wire, and that the operator endpoints are
token-gated. It only creates throwaway accounts, so it is safe to run against any environment. (Run it
repeatedly and the per-IP registration limit will start returning 429 — that is the guard working; the
script reports it as such.)

**Before exposing it publicly:** terminate TLS in front (reverse proxy or platform HTTPS) and set the
forwarded-headers options below, so rate limiting and HTTPS redirection see the real client IP and
scheme. Keep the `profiler-data` volume across redeploys, and back up the pepper.

### Run with Docker

Every push to `main` (or a `v*` tag, or a manual run) builds a container image and publishes it to the
GitHub Container Registry via `.github/workflows/deploy.yml` — no external secrets required. Run it
anywhere Docker runs:

```bash
docker run -d -p 8080:8080 \
  -v profiler-data:/data \
  -e Fingerprint__Pepper="$(openssl rand -base64 32)" \
  ghcr.io/kashiashvili/effective-octo-giggle:latest
# → http://localhost:8080
```

- **`-v profiler-data:/data`** persists the SQLite database *and* the auth-cookie key ring. Keep this
  volume across redeploys.
- **`Fingerprint__Pepper`** is a per-deployment secret; generate it once and keep it (see below). The
  app refuses to start without it. Provide it via your host's secret manager, not the command line, in
  production.
- Put TLS in front (a reverse proxy or the platform's HTTPS) and set the forwarded-headers options
  below so rate limiting and HTTPS redirection see the real client.

To build the image yourself: `docker build -t profiler .`

### Deploy to Azure App Service

One-time setup, run under your own Azure login (the script needs `az login`; it creates the
resource group, a Linux App Service plan, a container web app, and every app setting the app
needs, then prints the permanent pepper, the operator token and the publish profile):

```bash
az login && ./deploy/azure-bootstrap.sh
```

Defaults: free **F1** tier (sleeps when idle, 60 CPU-min/day), `westeurope`, image
`ghcr.io/kashiashvili/effective-octo-giggle:latest`. Override with env vars, e.g.
`APP_NAME=my-profiler SKU=B1 ./deploy/azure-bootstrap.sh`. The script pins the plan to a
**single worker** (SQLite corrupts if App Service scales out) and stores the database and key ring
under the persistent `/home` path.

**Publish-profile deploys need SCM basic auth.** Azure creates apps with basic publishing
credentials disabled, and `azure/webapps-deploy` then fails with `Failed to get app runtime OS`
before uploading anything. The bootstrap enables it for the SCM endpoint of that one app (FTP stays
off). To check or change it:

```bash
az resource show -g profiler-rg --namespace Microsoft.Web \
  --resource-type basicPublishingCredentialsPolicies --name scm \
  --parent sites/<APP_NAME> --query properties.allow
```

If you move the workflow to federated (OIDC) credentials instead, set it back to `false` — OIDC needs
no basic auth and leaks no long-lived secret.

**Free tier caveat.** F1 gives 60 CPU-minutes *per day*. A .NET container that restarts, plus any
per-minute probing, can spend that in hours — Azure then stops the site and every request returns
"Error 403 - This web app is stopped" until midnight UTC. The bootstrap therefore leaves the health
check off on F1/D1. If the site keeps hitting the quota, move to B1 (no redeploy, no data loss):

```bash
az appservice plan update -g profiler-rg -n profiler-plan --sku B1
az webapp config set -g profiler-rg -n <APP_NAME> --generic-configurations '{"healthCheckPath":"/health"}'
```

Check why a site is down with:
`az webapp show -g profiler-rg -n <APP_NAME> --query "{state:state, usageState:usageState}"` —
`QuotaExceeded` means the free quota, not a crash.

Onboarding a group from one network (office, meetup Wi-Fi)? Registrations are limited to 5 per IP per
hour, so raise `RateLimiting__RegisterPermitLimit` for the session (`az webapp config appsettings set`,
then restart) and put it back afterwards.

Then in the GitHub repo → Settings → Secrets and variables → Actions, add the variable
`AZURE_WEBAPP_NAME` and the secret `AZURE_WEBAPP_PUBLISH_PROFILE` (the XML the script prints), and
make the GHCR package public so App Service can pull it (the image holds no secrets). From then on
the `deploy-azure` job in `.github/workflows/deploy.yml` ships every push to `main` and waits until
the site answers 200. Upgrade in place, no redeploy, no data loss:

```bash
az appservice plan update -g profiler-rg -n profiler-plan --sku B1
```

---

## Configuration

All settings can be supplied via `appsettings.json` or environment variables.

| Key                              | Default              | Purpose                                              |
|----------------------------------|----------------------|------------------------------------------------------|
| `ConnectionStrings:Default`      | `Data Source=profiler.db` | SQLite database location                        |
| `RateLimiting:LoginPermitLimit`  | `5`                  | Allowed login attempts per IP per minute             |
| `RateLimiting:RegisterPermitLimit` | `5`                | Allowed registrations per IP per hour                |
| `RateLimiting:ConnectPermitLimit`  | `10`               | Allowed source-connect submits per IP per minute      |
| `RateLimiting:CirclesPermitLimit`  | `10`               | Circle start/join submits per IP per minute (plus a hard cap of 20 circles per account) |
| `DataProtection:KeyPath`         | `<contentRoot>/keys` | Where the auth-cookie key ring is persisted          |
| `Fingerprint:Pepper`             | — (**required**)     | Secret mixed into every fingerprint hash             |
| `Metrics:Token`                  | — (off)              | Operator bearer token. Gates `GET /metrics` (aggregate funnel + adoption counts: registered, with fingerprint, fingerprints by source, viewed matches, returned after the first day, active last 7 days, bio/contact/intent/values), `GET /metrics/reports` (moderation review), and `POST /metrics/suspend` (suspend/reinstate). All 404 unless set |
| `AntiAbuse:GuardRegistration`    | `false`              | Enforce the signed single-use registration form ticket (blocks blind/replayed POSTs). Turn on for a public launch |
| `AntiAbuse:MinFormSeconds`       | `3`                  | When the guard is on, reject a registration submitted faster than this after the form loaded |
| `Signals:ValuesEnabled`          | `true`               | The optional values/outlook signal (questionnaire, match-card alignment line, "Similar outlook first" sort). Set `false` to hide all three — the data-minimizing default until the signal is strengthened; stored buckets are kept for a clean re-enable |
| `Signals:CirclesEnabled`         | `true`               | Circles (invite-scoped groups: start, join by link, leave; "Same circle" chip and sort on matches). Set `false` to hide all of it; memberships are kept |
| `Backup:Directory`               | — (off)              | Folder for rolling SQLite snapshots (the container image sets `/data/backups`, the Azure bootstrap `/home/data/backups`). Unset = no snapshots |
| `Backup:Keep`                    | `7`                  | Snapshots kept per kind (scheduled / pre-migration); older ones are deleted |
| `Backup:IntervalHours`           | `24`                 | Hours between scheduled snapshots, anchored to the newest file on disk so restarts neither skip nor duplicate |
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
  redeploys. The keys are secret material and are git-ignored. They are stored
  **unencrypted** on that volume (the app logs a one-line warning about it at start):
  whoever can read the volume can also forge sessions, not only read the database, so
  treat volume access as root access. Rotating is deleting the folder — everyone is
  signed out and every outstanding circle invite link stops working; nothing else is lost. Encrypting keys at rest needs a certificate or a
  cloud key store and is a deliberate non-goal for a single-instance deployment.
- **Reading `/metrics` as a funnel.** `totalUsers` → `withFingerprint` (did people get past
  "connect or self-describe"; `fingerprintsBySource` says which path) → `viewedMatches` →
  `returnedAfterFirstDay` (latest match-list visit a day or more after registering — the "did they
  find a reason to come back" number; only the last visit is stored, so it cannot tell a second visit
  from a late first one) → `withContact` / `withBio` (willing to be reached). `fingerprintsBySource`,
  like the intent and values breakdowns, is withheld below a 10-account cohort.
  `registeredLast7Days` and `activeLast7Days` show the trend. All are counts over columns
  the app already stores; nothing per-user is reported.
- **Schema changes** go through EF Core migrations
  (`dotnet ef migrations add <Name> --project Profiler.Web`), applied automatically
  on startup — no manual database steps.
- **Health probe:** `GET /health` returns `200 {"status":"healthy"}` when the database
  is reachable, `503` otherwise. Anonymous, and reports nothing beyond reachability. The compose
  healthcheck, the Azure health-check path and the deploy workflow's readiness poll all use it.
- **Back up the database — see below.** Neither a bare Docker host nor App Service F1/B1 backs
  anything up for you, and the pepper makes lost signatures unrebuildable.
- **Moderation is operator-token-gated.** Users can `Report` a match (recorded with a
  closed-set reason, and the reported user is hidden from the reporter). To act on reports,
  set `Metrics:Token` and call `GET /metrics/reports` (reported users ranked by distinct
  reporters, with suspension status) and `POST /metrics/suspend` with a JSON body
  `{"username":"…","suspend":true}` (a **reversible** flag — a suspended account is removed
  from matches and cannot sign in; send `false` to reinstate). Deliberately not a hard-delete,
  so a leaked token cannot destroy accounts.
- **Turn on the registration guard before a public launch.** Sign-up is username + password only
  (no email/verification). A built-in, **privacy-preserving** anti-sybil layer — no third-party
  CAPTCHA, which would embed a tracker into a product whose whole promise is not tracking you — is
  ready: an always-on honeypot plus a signed, single-use, time-limited form ticket enforced when
  `AntiAbuse:GuardRegistration=true` (with `AntiAbuse:MinFormSeconds`). It blocks blind/looped/replayed
  registration POSTs; the per-IP `RateLimiting:RegisterPermitLimit` is the always-on volume cap. Note:
  single-use is tracked in-process, so a multi-instance deployment wanting strict single-use needs a
  shared cache (fine for a single instance). For very high-value protection you can still add a
  privacy-respecting CAPTCHA in front as well.

### Back up and restore

With `Backup:Directory` set (the image and the Azure bootstrap both set it) the app writes a
**consistent snapshot** of its SQLite database through SQLite's online-backup API — a plain copy of a
database that is being written can be torn — every `Backup:IntervalHours` (24) and keeps the newest
`Backup:Keep` (7), as `profiler-scheduled-<UTC stamp>.db`. It also snapshots an **established database
right before applying a migration** (`profiler-premigrate-…`), the one moment a bad release could lose
data with nothing to fall back on; if that safety copy cannot be written the app refuses to migrate
and logs why (fix the folder, or unset `Backup:Directory` to migrate without one). Any snapshot of
either kind **older than `Keep × Interval` days is deleted** whenever the app runs, so a deleted
account is gone from them by then — the privacy page and the delete-account panel state that number
when snapshots are on. Snapshots hold exactly what the live database holds: signatures, never raw data.

Snapshots live on the same volume as the database, so **copy them off the host** now and then:

```bash
# Docker / compose (container name "profiler")
docker cp profiler:/data/backups ./profiler-backups
```

```bash
# Azure App Service: the Kudu zip API serves /home. Credentials = userName / userPWD from the
# publish profile the bootstrap printed (az webapp deployment list-publishing-profiles --xml).
curl -u '$<app-name>:<userPWD>' "https://<app-name>.scm.azurewebsites.net/api/zip/data/backups/" -o profiler-backups.zip
```

Restore = stop the app, replace the database file (and drop any stale `-journal`), start the app.
Never write the file while the app is running. Cookies stay valid (the key ring is untouched) and
the pepper is unchanged, so restored signatures keep matching.

Docker / compose, restoring a snapshot that is already on the volume:

```bash
docker compose stop profiler
docker compose run --rm --no-deps --user root --entrypoint bash profiler -c \
  'cp /data/backups/profiler-scheduled-YYYYMMDD-HHMMSS.db /data/profiler.db && rm -f /data/profiler.db-journal && chown app:app /data/profiler.db'
docker compose start profiler
```

(To restore a file from your machine instead, add `-v "$PWD:/restore:ro"` to the `run` line and copy
from `/restore/<file>.db`.)

Azure App Service, from a file on your machine. Both Kudu APIs are rooted at `/home`, so
`/api/vfs/data/profiler.db` is the persistent `/home/data/profiler.db` the bootstrap configured.
(The compose sequence above was executed verbatim; this Azure sequence is untested until the first live
drill — run it once on a throwaway snapshot right after going live.)

```bash
az webapp stop -g profiler-rg -n <app-name>
curl -u '$<app-name>:<userPWD>' -X PUT -H 'If-Match: *' --data-binary @profiler-scheduled-YYYYMMDD-HHMMSS.db \
  "https://<app-name>.scm.azurewebsites.net/api/vfs/data/profiler.db"
curl -u '$<app-name>:<userPWD>' -X DELETE -H 'If-Match: *' "https://<app-name>.scm.azurewebsites.net/api/vfs/data/profiler.db-journal" || true
az webapp start -g profiler-rg -n <app-name>
```

Then `GET /health` must answer 200 and `deploy/smoke.sh` should pass against the restored deployment.

---

## Running tests

```bash
dotnet test
```

The suite (388 xUnit tests) is fully offline — connector tests use a stub HTTP
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
