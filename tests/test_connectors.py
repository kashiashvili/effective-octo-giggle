"""Tests for the data-source connectors."""

from __future__ import annotations

from unittest.mock import MagicMock, patch

import pytest
import requests

from profiler.connectors.base import ConnectorError, ProfileData
from profiler.connectors.github import GitHubConnector
from profiler.connectors.goodreads import GoodreadsConnector
from profiler.connectors.netflix import NetflixConnector


# ---------------------------------------------------------------------------
# GitHub connector
# ---------------------------------------------------------------------------

_GH_USER = {
    "login": "testuser",
    "public_gists": 3,
}

_GH_REPOS = [
    {"name": "repo1", "language": "Python",     "fork": False, "topics": ["machine-learning"]},
    {"name": "repo2", "language": "JavaScript", "fork": True,  "topics": ["web", "react"]},
    {"name": "repo3", "language": None,         "fork": False, "topics": []},
]

_GH_STARRED = [
    {"name": "cool-project", "topics": ["rust", "systems"]},
]


def _make_mock_response(json_data: object, ok: bool = True) -> MagicMock:
    resp = MagicMock()
    resp.ok = ok
    resp.json.return_value = json_data
    resp.raise_for_status.side_effect = None if ok else requests.HTTPError("error")
    return resp


class TestGitHubConnector:
    def _patched_session(self, connector: GitHubConnector, responses: list) -> None:
        connector._session.get = MagicMock(side_effect=responses)

    def test_name(self) -> None:
        conn = GitHubConnector("testuser")
        assert conn.name == "github"

    def test_fetch_extracts_languages(self) -> None:
        conn = GitHubConnector("testuser")
        self._patched_session(conn, [
            _make_mock_response(_GH_USER),
            _make_mock_response(_GH_REPOS),
            _make_mock_response(_GH_STARRED),
        ])
        data = conn.fetch()
        assert "language:python" in data.features
        assert "language:javascript" in data.features

    def test_fetch_extracts_topics(self) -> None:
        conn = GitHubConnector("testuser")
        self._patched_session(conn, [
            _make_mock_response(_GH_USER),
            _make_mock_response(_GH_REPOS),
            _make_mock_response(_GH_STARRED),
        ])
        data = conn.fetch()
        assert "topic:machine-learning" in data.features
        assert "topic:web" in data.features

    def test_fetch_fork_signal(self) -> None:
        conn = GitHubConnector("testuser")
        self._patched_session(conn, [
            _make_mock_response(_GH_USER),
            _make_mock_response(_GH_REPOS),
            _make_mock_response(_GH_STARRED),
        ])
        data = conn.fetch()
        assert "repo-type:forked" in data.features

    def test_fetch_gist_signal(self) -> None:
        conn = GitHubConnector("testuser")
        self._patched_session(conn, [
            _make_mock_response(_GH_USER),
            _make_mock_response(_GH_REPOS),
            _make_mock_response(_GH_STARRED),
        ])
        data = conn.fetch()
        assert "activity:public-gist" in data.features

    def test_fetch_starred_topics(self) -> None:
        conn = GitHubConnector("testuser")
        self._patched_session(conn, [
            _make_mock_response(_GH_USER),
            _make_mock_response(_GH_REPOS),
            _make_mock_response(_GH_STARRED),
        ])
        data = conn.fetch()
        assert "starred-topic:rust" in data.features

    def test_network_error_raises_connector_error(self) -> None:
        conn = GitHubConnector("testuser")
        conn._session.get = MagicMock(side_effect=requests.ConnectionError("no internet"))
        with pytest.raises(ConnectorError):
            conn.fetch()

    def test_returns_profile_data(self) -> None:
        conn = GitHubConnector("testuser")
        self._patched_session(conn, [
            _make_mock_response(_GH_USER),
            _make_mock_response(_GH_REPOS),
            _make_mock_response(_GH_STARRED),
        ])
        data = conn.fetch()
        assert isinstance(data, ProfileData)
        assert data.source == "github"


# ---------------------------------------------------------------------------
# Goodreads connector (CSV-based)
# ---------------------------------------------------------------------------

_GOODREADS_CSV = """\
Title,Bookshelves,My Rating
Dune,fiction science-fiction,5
Foundation,science-fiction,4
Harry Potter,fiction young-adult,3
Clean Code,technology,5
"""


class TestGoodreadsConnector:
    def test_name(self) -> None:
        conn = GoodreadsConnector(_GOODREADS_CSV)
        assert conn.name == "goodreads"

    def test_genre_features_extracted(self) -> None:
        conn = GoodreadsConnector(_GOODREADS_CSV)
        data = conn.fetch()
        assert "genre:fiction" in data.features
        assert "genre:science-fiction" in data.features
        assert "genre:technology" in data.features

    def test_high_rating_features(self) -> None:
        conn = GoodreadsConnector(_GOODREADS_CSV)
        data = conn.fetch()
        # Dune (rated 5) and Foundation (rated 4) should produce rating-high features
        high_features = [f for f in data.features if f.startswith("rating-high:")]
        assert len(high_features) >= 2

    def test_low_rating_not_included(self) -> None:
        csv = "Title,Bookshelves,My Rating\nBad Book,fiction,2\n"
        conn = GoodreadsConnector(csv)
        data = conn.fetch()
        high_features = [f for f in data.features if f.startswith("rating-high:")]
        assert len(high_features) == 0

    def test_missing_file_raises_connector_error(self, tmp_path) -> None:
        conn = GoodreadsConnector(tmp_path / "nonexistent.csv")
        with pytest.raises(ConnectorError, match="not found"):
            conn.fetch()

    def test_returns_profile_data(self) -> None:
        conn = GoodreadsConnector(_GOODREADS_CSV)
        data = conn.fetch()
        assert isinstance(data, ProfileData)
        assert data.source == "goodreads"

    def test_metadata_contains_books(self) -> None:
        conn = GoodreadsConnector(_GOODREADS_CSV)
        data = conn.fetch()
        assert isinstance(data.metadata.get("books"), list)
        assert len(data.metadata["books"]) > 0


# ---------------------------------------------------------------------------
# Netflix connector (CSV-based)
# ---------------------------------------------------------------------------

_NETFLIX_CSV = """\
Title,Date
Breaking Bad: Season 1: Pilot,2023-01-01
The Dark Knight,2023-01-02
Cowboy Bebop: Season 1: Episode 1,2023-01-03
Stand-Up Comedy Special,2023-01-04
"""


class TestNetflixConnector:
    def test_name(self) -> None:
        conn = NetflixConnector(_NETFLIX_CSV)
        assert conn.name == "netflix"

    def test_series_type_detected(self) -> None:
        conn = NetflixConnector(_NETFLIX_CSV)
        data = conn.fetch()
        assert "netflix-type:series" in data.features

    def test_movie_type_detected(self) -> None:
        conn = NetflixConnector(_NETFLIX_CSV)
        data = conn.fetch()
        assert "netflix-type:movie" in data.features

    def test_watched_hash_features(self) -> None:
        conn = NetflixConnector(_NETFLIX_CSV)
        data = conn.fetch()
        watched = [f for f in data.features if f.startswith("netflix-watched:")]
        assert len(watched) > 0

    def test_missing_file_raises_connector_error(self, tmp_path) -> None:
        conn = NetflixConnector(tmp_path / "nonexistent.csv")
        with pytest.raises(ConnectorError, match="not found"):
            conn.fetch()

    def test_returns_profile_data(self) -> None:
        conn = NetflixConnector(_NETFLIX_CSV)
        data = conn.fetch()
        assert isinstance(data, ProfileData)
        assert data.source == "netflix"

    def test_metadata_contains_titles(self) -> None:
        conn = NetflixConnector(_NETFLIX_CSV)
        data = conn.fetch()
        assert isinstance(data.metadata.get("titles"), list)
        assert len(data.metadata["titles"]) > 0

    def test_comedy_genre_inferred(self) -> None:
        csv = "Title,Date\nSome Stand-Up Comedy Show,2023-01-01\n"
        conn = NetflixConnector(csv)
        data = conn.fetch()
        assert "netflix-genre:comedy" in data.features
