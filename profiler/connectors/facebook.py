"""Facebook connector – uses the Facebook Graph API to extract interest signals.

Requires a valid user *access_token* with the following scopes:
  ``user_likes``, ``user_interests``, ``user_education_history``,
  ``user_work_history``

Note: Facebook has progressively restricted API access; not all fields are
available to all app types.  This connector gracefully ignores fields that
are not returned.

Features extracted
------------------
* ``fb-like-category:<cat>``  – categories of pages the user has liked
* ``fb-interest:<interest>``  – user-reported interests
* ``fb-edu-type:<type>``      – type of educational institution attended
"""

from __future__ import annotations

import requests

from .base import BaseConnector, ConnectorError, ProfileData

_GRAPH_BASE = "https://graph.facebook.com/v18.0"
_TIMEOUT = 10


class FacebookConnector(BaseConnector):
    """Collect Facebook interest signals via the Graph API."""

    def __init__(self, access_token: str) -> None:
        self._token = access_token
        self._session = requests.Session()

    @property
    def name(self) -> str:
        return "facebook"

    def _get(self, path: str, **params: object) -> dict:
        params["access_token"] = self._token
        resp = self._session.get(f"{_GRAPH_BASE}{path}", params=params, timeout=_TIMEOUT)
        resp.raise_for_status()
        return resp.json()

    def fetch(self) -> ProfileData:
        features: list[str] = []
        metadata: dict[str, object] = {}

        try:
            # --- liked pages -------------------------------------------------
            try:
                likes_data = self._get("/me/likes", fields="category", limit=200)
                metadata["likes"] = likes_data
                for item in likes_data.get("data", []):
                    cat = item.get("category", "").strip().lower()
                    if cat:
                        features.append(f"fb-like-category:{cat}")
            except requests.HTTPError:
                pass  # permission not granted – skip silently

            # --- interests ---------------------------------------------------
            try:
                interests_data = self._get("/me/interests", fields="name,category", limit=200)
                metadata["interests"] = interests_data
                for item in interests_data.get("data", []):
                    name = item.get("name", "").strip().lower()
                    if name:
                        features.append(f"fb-interest:{name}")
            except requests.HTTPError:
                pass

            # --- education ---------------------------------------------------
            try:
                edu_data = self._get("/me", fields="education")
                metadata["education"] = edu_data
                for entry in edu_data.get("education", []):
                    edu_type = entry.get("type", "").strip().lower()
                    if edu_type:
                        features.append(f"fb-edu-type:{edu_type}")
            except requests.HTTPError:
                pass

        except requests.RequestException as exc:
            raise ConnectorError(f"Facebook Graph API request failed: {exc}") from exc

        return ProfileData(source=self.name, features=list(set(features)), metadata=metadata)
