"""Netflix connector – extracts viewing-habit signals from a Netflix
viewing-history export.

Netflix does not provide a public API.  Users can download their viewing
history from https://www.netflix.com/viewingactivity as a CSV file and supply
the path to this connector.

The export contains only ``Title`` and ``Date`` columns.  We parse the title
to extract:

* Series name vs movie distinction
* Season / episode patterns

Features extracted
------------------
* ``netflix-genre:<inferred-genre>``  – genre inferred from title keywords
* ``netflix-type:series``             – user watches TV series
* ``netflix-type:movie``              – user watches movies (no "Season" in title)
* ``netflix-watched:<title-hash>``    – privacy-safe hash of each title watched
"""

from __future__ import annotations

import csv
import hashlib
import io
import re
from pathlib import Path
from typing import Union

from .base import BaseConnector, ConnectorError, ProfileData

# Simple keyword → genre mapping for title inference
_GENRE_KEYWORDS: dict[str, list[str]] = {
    "documentary": ["documentary", "explained", "history of", "untold"],
    "comedy": ["comedy", "stand-up", "standup"],
    "anime": ["anime", "attack on titan", "demon slayer", "naruto", "one piece"],
    "thriller": ["thriller", "crime", "murder", "dark"],
    "sci-fi": ["sci-fi", "star trek", "black mirror", "altered carbon"],
    "reality-tv": ["love is blind", "too hot", "bachelor", "real housewives"],
    "kids": ["paw patrol", "peppa", "bluey", "cocomelon"],
}

_SERIES_PATTERN = re.compile(r"season\s+\d+|episode\s+\d+|s\d+e\d+", re.IGNORECASE)


def _title_hash(title: str) -> str:
    return hashlib.sha256(title.strip().lower().encode()).hexdigest()[:12]


class NetflixConnector(BaseConnector):
    """Collect viewing-habit signals from a Netflix viewing-history CSV.

    Parameters
    ----------
    csv_source:
        A file-system path to the exported CSV **or** a raw CSV string.
    """

    def __init__(self, csv_source: Union[str, Path]) -> None:
        if isinstance(csv_source, Path) or (
            isinstance(csv_source, str) and "\n" not in csv_source
        ):
            self._path = Path(csv_source)
            self._raw: str | None = None
        else:
            self._path = None
            self._raw = csv_source

    @property
    def name(self) -> str:
        return "netflix"

    def fetch(self) -> ProfileData:
        features: list[str] = []
        metadata: dict[str, object] = {"titles": []}

        try:
            if self._raw is not None:
                reader = csv.DictReader(io.StringIO(self._raw))
            else:
                if not self._path.exists():
                    raise ConnectorError(f"Netflix CSV not found: {self._path}")
                reader = csv.DictReader(self._path.open(encoding="utf-8"))

            for row in reader:
                title = row.get("Title", "").strip()
                if not title:
                    continue

                title_lower = title.lower()

                # series vs movie
                if _SERIES_PATTERN.search(title_lower):
                    features.append("netflix-type:series")
                else:
                    features.append("netflix-type:movie")

                # infer genre from title keywords
                for genre, keywords in _GENRE_KEYWORDS.items():
                    if any(kw in title_lower for kw in keywords):
                        features.append(f"netflix-genre:{genre}")

                # privacy-safe title signal
                features.append(f"netflix-watched:{_title_hash(title)}")

                metadata["titles"].append(title)  # type: ignore[index]

        except (OSError, csv.Error) as exc:
            raise ConnectorError(f"Netflix CSV parse failed: {exc}") from exc

        return ProfileData(source=self.name, features=list(set(features)), metadata=metadata)
