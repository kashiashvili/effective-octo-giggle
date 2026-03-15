"""SQLite database helpers."""

from __future__ import annotations

import json
import sqlite3
from pathlib import Path
from typing import Optional

_DB_PATH: Path = Path("profiler_data.db")


def get_db_path() -> Path:
    return _DB_PATH


def set_db_path(path: Path) -> None:
    global _DB_PATH
    _DB_PATH = path


def get_connection() -> sqlite3.Connection:
    conn = sqlite3.connect(str(_DB_PATH))
    conn.row_factory = sqlite3.Row
    return conn


def init_db() -> None:
    """Create tables if they don't exist."""
    with get_connection() as conn:
        conn.executescript(
            """
            CREATE TABLE IF NOT EXISTS users (
                id       INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT    NOT NULL UNIQUE,
                password TEXT    NOT NULL,
                created_at TEXT  DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS fingerprints (
                user_id      INTEGER PRIMARY KEY REFERENCES users(id),
                fingerprint  TEXT NOT NULL,
                sources      TEXT NOT NULL DEFAULT '[]',
                updated_at   TEXT DEFAULT (datetime('now'))
            );
            """
        )


# ---------------------------------------------------------------------------
# User helpers
# ---------------------------------------------------------------------------

def create_user(username: str, password_hash: str) -> int:
    """Insert a new user; returns the new row id."""
    with get_connection() as conn:
        cur = conn.execute(
            "INSERT INTO users (username, password) VALUES (?, ?)",
            (username, password_hash),
        )
        return cur.lastrowid


def get_user_by_username(username: str) -> Optional[sqlite3.Row]:
    with get_connection() as conn:
        return conn.execute(
            "SELECT * FROM users WHERE username = ?", (username,)
        ).fetchone()


def get_user_by_id(user_id: int) -> Optional[sqlite3.Row]:
    with get_connection() as conn:
        return conn.execute(
            "SELECT * FROM users WHERE id = ?", (user_id,)
        ).fetchone()


def list_all_users() -> list[sqlite3.Row]:
    with get_connection() as conn:
        return conn.execute("SELECT * FROM users").fetchall()


# ---------------------------------------------------------------------------
# Fingerprint helpers
# ---------------------------------------------------------------------------

def save_fingerprint(user_id: int, fingerprint_dict: dict, sources: list[str]) -> None:
    with get_connection() as conn:
        conn.execute(
            """
            INSERT INTO fingerprints (user_id, fingerprint, sources, updated_at)
            VALUES (?, ?, ?, datetime('now'))
            ON CONFLICT(user_id) DO UPDATE SET
                fingerprint = excluded.fingerprint,
                sources     = excluded.sources,
                updated_at  = excluded.updated_at
            """,
            (user_id, json.dumps(fingerprint_dict), json.dumps(sources)),
        )


def get_fingerprint(user_id: int) -> Optional[dict]:
    """Return the stored fingerprint dict for *user_id*, or None."""
    with get_connection() as conn:
        row = conn.execute(
            "SELECT fingerprint, sources FROM fingerprints WHERE user_id = ?",
            (user_id,),
        ).fetchone()
    if row is None:
        return None
    return {
        "fingerprint": json.loads(row["fingerprint"]),
        "sources": json.loads(row["sources"]),
    }


def list_all_fingerprints() -> list[dict]:
    """Return all stored {user_id, username, fingerprint, sources} rows."""
    with get_connection() as conn:
        rows = conn.execute(
            """
            SELECT u.id AS user_id, u.username, f.fingerprint, f.sources
            FROM fingerprints f
            JOIN users u ON u.id = f.user_id
            """
        ).fetchall()
    return [
        {
            "user_id": r["user_id"],
            "username": r["username"],
            "fingerprint": json.loads(r["fingerprint"]),
            "sources": json.loads(r["sources"]),
        }
        for r in rows
    ]
