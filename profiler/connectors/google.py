"""Google connector – uses the Google People API to extract interest signals.

Requires a valid OAuth 2.0 *access_token* scoped to:
  https://www.googleapis.com/auth/contacts.readonly
  https://www.googleapis.com/auth/userinfo.profile
"""

from __future__ import annotations

import requests

from .base import BaseConnector, ConnectorError, ProfileData

_PEOPLE_API = "https://people.googleapis.com/v1/people/me"
_TIMEOUT = 10


class GoogleConnector(BaseConnector):
    """Collect Google profile signals via the People API.

    Features extracted
    ------------------
    * ``interest:<interest>``     – user-reported interests / hobbies
    * ``locale:<locale>``         – locale / language preference
    * ``org-type:<type>``         – type of organisation the user belongs to
    """

    def __init__(self, access_token: str) -> None:
        self._token = access_token
        self._session = requests.Session()
        self._session.headers.update({"Authorization": f"Bearer {self._token}"})

    @property
    def name(self) -> str:
        return "google"

    def fetch(self) -> ProfileData:
        features: list[str] = []
        metadata: dict[str, object] = {}

        try:
            resp = self._session.get(
                _PEOPLE_API,
                params={
                    "personFields": "interests,locales,organizations,biographies"
                },
                timeout=_TIMEOUT,
            )
            resp.raise_for_status()
            person = resp.json()
            metadata["person"] = person

            for item in person.get("interests", []):
                val = item.get("value", "").strip().lower()
                if val:
                    features.append(f"interest:{val}")

            for item in person.get("locales", []):
                val = item.get("value", "").strip().lower()
                if val:
                    features.append(f"locale:{val}")

            for item in person.get("organizations", []):
                org_type = item.get("type", "").strip().lower()
                if org_type:
                    features.append(f"org-type:{org_type}")

        except requests.RequestException as exc:
            raise ConnectorError(f"Google People API request failed: {exc}") from exc

        return ProfileData(source=self.name, features=list(set(features)), metadata=metadata)
