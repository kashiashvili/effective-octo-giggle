"""Goodreads connector – uses the Goodreads API to extract reading signals.

Goodreads ended their public API in December 2020.  This connector therefore
uses an *export CSV* approach: users can download their library as a CSV from
https://www.goodreads.com/review/import and supply the path to that file.

Features extracted
------------------
* ``genre:<genre>``           – genres of shelved books (bookshelves)
* ``shelf:<shelf>``           – custom shelf names (e.g. "to-read", "read")
* ``rating-high:<title-hash>``– books rated 4-5 stars (hashed title only)
"""

from __future__ import annotations

import csv
import hashlib
import io
import re
from pathlib import Path
from typing import Union

from .base import BaseConnector, ConnectorError, ProfileData

# Shelves that convey reading taste rather than just status
_TASTE_SHELVES = {"read", "currently-reading", "favorites"}
_GENRE_SHELVES = {
    "fiction", "non-fiction", "science-fiction", "fantasy", "mystery",
    "thriller", "romance", "biography", "history", "self-help", "science",
    "philosophy", "poetry", "horror", "young-adult", "children",
    "graphic-novels", "travel", "cooking", "art", "religion", "psychology",
    "business", "technology",
}


def _title_hash(title: str) -> str:
    """Return a short, non-reversible hash of a book title."""
    return hashlib.sha256(title.strip().lower().encode()).hexdigest()[:12]


class GoodreadsConnector(BaseConnector):
    """Collect reading-habit signals from a Goodreads export CSV.

    Parameters
    ----------
    csv_source:
        Either a file-system path to the exported CSV file **or** a
        raw CSV string (useful for testing).
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
        return "goodreads"

    def fetch(self) -> ProfileData:
        features: list[str] = []
        metadata: dict[str, object] = {"books": []}

        try:
            if self._raw is not None:
                reader = csv.DictReader(io.StringIO(self._raw))
            else:
                if not self._path.exists():
                    raise ConnectorError(f"Goodreads CSV not found: {self._path}")
                reader = csv.DictReader(self._path.open(encoding="utf-8"))

            for row in reader:
                title = row.get("Title", "")
                shelves_raw = row.get("Bookshelves", "") or row.get("Exclusive Shelf", "")
                rating_str = row.get("My Rating", "0")

                # Shelves may be comma-separated or space-separated in the export
                shelves = [s.strip().lower() for s in re.split(r"[,\s]+", shelves_raw) if s.strip()]
                try:
                    rating = int(rating_str)
                except ValueError:
                    rating = 0

                for shelf in shelves:
                    if shelf in _GENRE_SHELVES:
                        features.append(f"genre:{shelf}")
                    elif shelf in _TASTE_SHELVES:
                        features.append(f"shelf:{shelf}")

                if rating >= 4 and title:
                    features.append(f"rating-high:{_title_hash(title)}")

                metadata["books"].append({"title": title, "shelves": shelves, "rating": rating})  # type: ignore[index]

        except (OSError, csv.Error) as exc:
            raise ConnectorError(f"Goodreads CSV parse failed: {exc}") from exc

        return ProfileData(source=self.name, features=list(set(features)), metadata=metadata)
