"""GitHub connector – uses the public GitHub REST API (no auth required for
public data; supply a personal-access token to increase rate limits and access
private data).
"""

from __future__ import annotations

from typing import Optional

import requests

from .base import BaseConnector, ConnectorError, ProfileData

_API_BASE = "https://api.github.com"
_TIMEOUT = 10


class GitHubConnector(BaseConnector):
    """Collect public GitHub profile data for *username*.

    Features extracted
    ------------------
    * ``language:<lang>``     – programming languages used in public repos
    * ``topic:<topic>``       – repository topics starred / owned
    * ``repo-type:forked``    – user forks repositories (behaviour signal)
    * ``activity:public-gist`` – user has public gists
    """

    def __init__(self, username: str, token: Optional[str] = None) -> None:
        self._username = username
        self._session = requests.Session()
        self._session.headers.update({"Accept": "application/vnd.github+json"})
        if token:
            self._session.headers.update({"Authorization": f"Bearer {token}"})

    @property
    def name(self) -> str:
        return "github"

    def fetch(self) -> ProfileData:
        features: list[str] = []
        metadata: dict[str, object] = {}

        try:
            # --- user info ---------------------------------------------------
            user_resp = self._session.get(
                f"{_API_BASE}/users/{self._username}", timeout=_TIMEOUT
            )
            user_resp.raise_for_status()
            user = user_resp.json()
            metadata["user"] = user

            if user.get("public_gists", 0) > 0:
                features.append("activity:public-gist")

            # --- public repos ------------------------------------------------
            repos_resp = self._session.get(
                f"{_API_BASE}/users/{self._username}/repos",
                params={"per_page": 100, "sort": "updated"},
                timeout=_TIMEOUT,
            )
            repos_resp.raise_for_status()
            repos = repos_resp.json()
            metadata["repos"] = repos

            languages: set[str] = set()
            topics: set[str] = set()
            has_fork = False

            for repo in repos:
                if repo.get("fork"):
                    has_fork = True
                lang = repo.get("language")
                if lang:
                    languages.add(lang.lower())
                for topic in repo.get("topics", []):
                    topics.add(topic.lower())

            features.extend(f"language:{l}" for l in sorted(languages))
            features.extend(f"topic:{t}" for t in sorted(topics))
            if has_fork:
                features.append("repo-type:forked")

            # --- starred repos (topics) --------------------------------------
            starred_resp = self._session.get(
                f"{_API_BASE}/users/{self._username}/starred",
                params={"per_page": 50},
                timeout=_TIMEOUT,
                headers={"Accept": "application/vnd.github.mercy-preview+json"},
            )
            if starred_resp.ok:
                for repo in starred_resp.json():
                    for topic in repo.get("topics", []):
                        features.append(f"starred-topic:{topic.lower()}")

        except requests.RequestException as exc:
            raise ConnectorError(f"GitHub request failed: {exc}") from exc

        return ProfileData(source=self.name, features=list(set(features)), metadata=metadata)
