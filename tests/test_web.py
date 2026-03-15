"""Tests for the Flask web application."""

from __future__ import annotations

import io
import json
import tempfile
from pathlib import Path

import pytest

from profiler.web.app import create_app


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def tmp_db(tmp_path: Path):
    return tmp_path / "test.db"


@pytest.fixture
def app(tmp_db: Path):
    return create_app({"TESTING": True, "DATABASE": tmp_db, "WTF_CSRF_ENABLED": False})


@pytest.fixture
def client(app):
    return app.test_client()


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _register(client, username="alice", password="password123"):
    return client.post(
        "/register",
        data={"username": username, "password": password, "confirm_password": password},
        follow_redirects=True,
    )


def _login(client, username="alice", password="password123"):
    return client.post(
        "/login",
        data={"username": username, "password": password},
        follow_redirects=True,
    )


# ---------------------------------------------------------------------------
# Landing page
# ---------------------------------------------------------------------------

class TestIndex:
    def test_landing_page_loads(self, client) -> None:
        resp = client.get("/")
        assert resp.status_code == 200
        assert b"Find people" in resp.data or b"Profiler" in resp.data


# ---------------------------------------------------------------------------
# Auth routes
# ---------------------------------------------------------------------------

class TestRegister:
    def test_register_page_loads(self, client) -> None:
        resp = client.get("/register")
        assert resp.status_code == 200

    def test_register_creates_account(self, client) -> None:
        resp = _register(client)
        assert resp.status_code == 200
        assert b"Sign in" in resp.data or b"created" in resp.data.lower()

    def test_register_duplicate_username(self, client) -> None:
        _register(client)
        resp = _register(client)  # second registration with same name
        assert b"taken" in resp.data.lower() or b"already" in resp.data.lower()

    def test_register_password_mismatch(self, client) -> None:
        resp = client.post(
            "/register",
            data={"username": "alice", "password": "abc123", "confirm_password": "xyz999"},
            follow_redirects=True,
        )
        assert b"match" in resp.data.lower()

    def test_register_short_password(self, client) -> None:
        resp = client.post(
            "/register",
            data={"username": "alice", "password": "ab", "confirm_password": "ab"},
            follow_redirects=True,
        )
        assert b"6 char" in resp.data.lower() or b"least 6" in resp.data.lower()


class TestLogin:
    def test_login_page_loads(self, client) -> None:
        resp = client.get("/login")
        assert resp.status_code == 200

    def test_login_success_redirects_to_dashboard(self, client) -> None:
        _register(client)
        resp = _login(client)
        assert resp.status_code == 200
        assert b"Dashboard" in resp.data or b"dashboard" in resp.data.lower()

    def test_login_invalid_password(self, client) -> None:
        _register(client)
        resp = client.post(
            "/login",
            data={"username": "alice", "password": "wrong"},
            follow_redirects=True,
        )
        assert b"Invalid" in resp.data

    def test_logout_redirects_to_index(self, client) -> None:
        _register(client)
        _login(client)
        resp = client.get("/logout", follow_redirects=True)
        assert resp.status_code == 200


# ---------------------------------------------------------------------------
# Dashboard
# ---------------------------------------------------------------------------

class TestDashboard:
    def test_dashboard_requires_login(self, client) -> None:
        resp = client.get("/dashboard", follow_redirects=True)
        assert b"Sign in" in resp.data

    def test_dashboard_loads_after_login(self, client) -> None:
        _register(client)
        _login(client)
        resp = client.get("/dashboard")
        assert resp.status_code == 200
        assert b"Dashboard" in resp.data


# ---------------------------------------------------------------------------
# Connect sources
# ---------------------------------------------------------------------------

_GOODREADS_CSV = (
    "Title,Bookshelves,My Rating\n"
    "Dune,science-fiction,5\n"
    "Foundation,science-fiction,4\n"
)

_NETFLIX_CSV = (
    "Title,Date\n"
    "Breaking Bad: Season 1: Pilot,2023-01-01\n"
    "The Dark Knight,2023-01-02\n"
)


class TestConnect:
    def test_connect_page_requires_login(self, client) -> None:
        resp = client.get("/connect", follow_redirects=True)
        assert b"Sign in" in resp.data

    def test_connect_page_loads(self, client) -> None:
        _register(client)
        _login(client)
        resp = client.get("/connect")
        assert resp.status_code == 200
        assert b"GitHub" in resp.data
        assert b"Goodreads" in resp.data
        assert b"Netflix" in resp.data

    def test_connect_goodreads_csv(self, client) -> None:
        _register(client)
        _login(client)
        resp = client.post(
            "/connect",
            data={
                "goodreads_csv": (io.BytesIO(_GOODREADS_CSV.encode()), "goodreads.csv"),
            },
            content_type="multipart/form-data",
            follow_redirects=True,
        )
        assert resp.status_code == 200
        assert b"matches" in resp.data.lower() or b"fingerprint" in resp.data.lower()

    def test_connect_netflix_csv(self, client) -> None:
        _register(client)
        _login(client)
        resp = client.post(
            "/connect",
            data={
                "netflix_csv": (io.BytesIO(_NETFLIX_CSV.encode()), "netflix.csv"),
            },
            content_type="multipart/form-data",
            follow_redirects=True,
        )
        assert resp.status_code == 200

    def test_connect_no_sources_shows_warning(self, client) -> None:
        _register(client)
        _login(client)
        resp = client.post("/connect", data={}, follow_redirects=True)
        assert b"at least one" in resp.data.lower() or b"source" in resp.data.lower()


# ---------------------------------------------------------------------------
# Matches
# ---------------------------------------------------------------------------

class TestMatches:
    def test_matches_requires_login(self, client) -> None:
        resp = client.get("/matches", follow_redirects=True)
        assert b"Sign in" in resp.data

    def test_matches_redirects_to_connect_when_no_fingerprint(self, client) -> None:
        _register(client)
        _login(client)
        resp = client.get("/matches", follow_redirects=True)
        assert b"connect" in resp.data.lower() or b"source" in resp.data.lower()

    def test_matches_page_loads_after_fingerprint(self, client) -> None:
        _register(client)
        _login(client)
        # Connect a source first
        client.post(
            "/connect",
            data={
                "goodreads_csv": (io.BytesIO(_GOODREADS_CSV.encode()), "goodreads.csv"),
            },
            content_type="multipart/form-data",
        )
        resp = client.get("/matches")
        assert resp.status_code == 200
        assert b"match" in resp.data.lower()

    def test_two_users_see_each_other(self, client) -> None:
        # Register alice + bob with similar profiles
        _register(client, "alice", "pass1234")
        _login(client, "alice", "pass1234")
        client.post(
            "/connect",
            data={"goodreads_csv": (io.BytesIO(_GOODREADS_CSV.encode()), "gr.csv")},
            content_type="multipart/form-data",
        )
        client.get("/logout")

        _register(client, "bob", "pass1234")
        _login(client, "bob", "pass1234")
        client.post(
            "/connect",
            data={"goodreads_csv": (io.BytesIO(_GOODREADS_CSV.encode()), "gr.csv")},
            content_type="multipart/form-data",
        )

        resp = client.get("/matches")
        assert resp.status_code == 200
        assert b"alice" in resp.data.lower()
