# effective-octo-giggle – Privacy-Preserving User Profiler

Gather interest signals from multiple data sources, combine them into a
**privacy-preserving fingerprint**, and find people with similar tastes —
without ever exposing your raw personal data.

---

## How it works

```
Data sources          Aggregation          Fingerprint         Matching
─────────────         ───────────          ───────────         ────────
GitHub   ──┐
Goodreads──┼──► ProfileAggregator ──► FingerprintGenerator ──► UserMatcher
Netflix  ──┤                               (MinHash)
Google   ──┤
Facebook ──┘
```

### Privacy model

Raw feature strings (e.g. `language:python`, `genre:sci-fi`) are fed into a
**MinHash** algorithm:

1. Each feature is hashed to an integer via SHA-256.
2. *k* independent universal hash functions each produce a minimum value over
   all features.
3. The resulting *k*-dimensional signature is the **profile fingerprint**.

The probability that two users share a value at position *i* equals the
*Jaccard similarity* of their underlying feature sets.  This lets you compare
profiles without ever transmitting (or storing) the original feature strings.

---

## Supported data sources

| Source      | Method                                    |
|-------------|-------------------------------------------|
| **GitHub**  | REST API (public; optional token for rate-limit) |
| **Goodreads** | Export CSV (downloaded from your account) |
| **Netflix** | Viewing-history CSV (downloaded from your account) |
| **Google**  | People API (OAuth 2.0 access token)       |
| **Facebook**| Graph API (OAuth 2.0 access token)        |

---

## Installation

```bash
pip install -e .
```

Requires Python ≥ 3.8.

---

## Quick start

### 1 – Generate a fingerprint

```bash
# GitHub only (no auth needed for public profiles)
profiler generate --github-user octocat --output my_fingerprint.json

# Multiple sources
profiler generate \
  --github-user octocat \
  --goodreads-csv ~/goodreads_library_export.csv \
  --netflix-csv  ~/NetflixViewingHistory.csv \
  --output my_fingerprint.json
```

### 2 – Compare two fingerprints

```bash
profiler compare alice_fingerprint.json bob_fingerprint.json
# Similarity: 0.4531 (45.3%)
```

### 3 – Programmatic usage

```python
from profiler.connectors.github import GitHubConnector
from profiler.connectors.goodreads import GoodreadsConnector
from profiler.profile.aggregator import ProfileAggregator
from profiler.profile.fingerprint import FingerprintGenerator
from profiler.matching.matcher import UserMatcher

# Build profile for alice
connectors = [
    GitHubConnector("alice"),
    GoodreadsConnector("/path/to/alice_goodreads.csv"),
]
profile = ProfileAggregator(connectors).aggregate()
alice_fp = FingerprintGenerator().generate(profile.features)

# Build profile for bob
bob_fp = FingerprintGenerator().generate(["language:python", "genre:sci-fi"])

# Register and match
matcher = UserMatcher()
matcher.add("alice", alice_fp)
matcher.add("bob",   bob_fp)

results = matcher.find_matches("alice", top_k=5)
for r in results:
    print(r)  # MatchResult(user_id='bob', similarity=0.453)
```

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
└── cli.py               Click-based command-line interface
tests/
├── test_connectors.py
├── test_aggregator.py
├── test_fingerprint.py
└── test_matcher.py
```
