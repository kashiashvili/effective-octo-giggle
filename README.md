# effective-octo-giggle – Privacy-Preserving Social Profiler

A web social-network application that gathers interest signals from multiple
data sources, generates a **privacy-preserving fingerprint**, and connects you
with like-minded people — without ever exposing your raw personal data.

---

## Screenshots

### Landing page
![Landing page](https://github.com/user-attachments/assets/9b9a610a-da26-4c78-919b-2b20adec0658)

### Connect your data sources
![Connect sources](https://github.com/user-attachments/assets/ff4383e8-d77a-4215-b04b-be66c547253e)

### Your matches
![Matches](https://github.com/user-attachments/assets/ce1926a9-b792-4df9-bb96-47948a164d44)

---

## Features

- **User accounts** – register and sign in securely (passwords hashed with scrypt)
- **Multi-source data collection** – connect GitHub, upload Goodreads/Netflix CSV exports, or paste Google/Facebook OAuth tokens
- **Privacy-preserving fingerprinting** – MinHash algorithm turns your interests into a comparable signature; raw data is never stored
- **Similarity matching** – ranked list of users with compatible profiles (Jaccard similarity)
- **Responsive UI** – Bootstrap 5 web interface

---

## Supported data sources

| Source      | Method                                         |
|-------------|------------------------------------------------|
| **GitHub**  | REST API – enter your username (public, no account required) |
| **Goodreads** | Export CSV from [goodreads.com/review/import](https://www.goodreads.com/review/import) |
| **Netflix** | Export CSV from [netflix.com/viewingactivity](https://www.netflix.com/viewingactivity) |
| **Google**  | OAuth 2.0 access token (People API)            |
| **Facebook**| OAuth 2.0 access token (Graph API)             |

---

## Privacy model

Raw feature strings (e.g. `language:python`, `genre:sci-fi`) are processed by
a **MinHash** algorithm and never stored:

1. Each feature is hashed to an integer via SHA-256
2. *k* independent universal hash functions each produce a minimum value over all features
3. Only the resulting *k*-dimensional integer signature is saved

Two signatures can be compared to estimate *Jaccard similarity* without
revealing any underlying data.

---

## Installation

```bash
pip install -e .
```

Requires Python ≥ 3.8.

---

## Running the web application

```bash
profiler-web
# → http://127.0.0.1:5000
```

Optional environment variables:

| Variable      | Default                          | Description              |
|---------------|----------------------------------|--------------------------|
| `SECRET_KEY`  | `dev-secret-change-in-production`| Flask session secret      |
| `HOST`        | `127.0.0.1`                      | Bind address              |
| `PORT`        | `5000`                           | Port                      |
| `DATABASE`    | `profiler_data.db`               | SQLite database file path |

---

## Running tests

```bash
pip install pytest
pytest tests/ -v
```

---

## Project layout

```
profiler/
├── connectors/
│   ├── base.py          Base connector interface (ProfileData, BaseConnector)
│   ├── github.py        GitHub REST API connector
│   ├── google.py        Google People API connector
│   ├── goodreads.py     Goodreads export-CSV connector
│   ├── netflix.py       Netflix viewing-history CSV connector
│   └── facebook.py      Facebook Graph API connector
├── profile/
│   ├── aggregator.py    Merges ProfileData from multiple connectors
│   └── fingerprint.py   MinHash-based privacy-preserving fingerprint
├── matching/
│   └── matcher.py       Similarity-based user matcher
└── web/
    ├── app.py           Flask application factory
    ├── db.py            SQLite database helpers
    ├── routes/
    │   ├── auth.py      Register / login / logout
    │   ├── sources.py   Connect data sources (dashboard)
    │   └── matches.py   Show similar users
    ├── templates/       Jinja2 HTML templates
    └── static/css/      Custom styles (Bootstrap 5 base)
tests/
├── test_connectors.py
├── test_aggregator.py
├── test_fingerprint.py
├── test_matcher.py
└── test_web.py
```
