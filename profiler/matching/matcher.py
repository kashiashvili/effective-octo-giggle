"""User matcher – finds the most similar users given a set of fingerprints."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, List

from profiler.profile.fingerprint import ProfileFingerprint


@dataclass
class MatchResult:
    """A single match result.

    Attributes
    ----------
    user_id:
        The identifier of the matched user (as supplied to :meth:`UserMatcher.add`).
    similarity:
        Estimated Jaccard similarity in [0, 1].  1.0 means identical feature
        sets; 0.0 means no overlap.
    """

    user_id: str
    similarity: float

    def __repr__(self) -> str:
        return f"MatchResult(user_id={self.user_id!r}, similarity={self.similarity:.3f})"


class UserMatcher:
    """Store fingerprints for multiple users and find the closest matches.

    Usage
    -----
    >>> matcher = UserMatcher()
    >>> matcher.add("alice", alice_fingerprint)
    >>> matcher.add("bob",   bob_fingerprint)
    >>> results = matcher.find_matches("alice", top_k=5)
    """

    def __init__(self) -> None:
        self._store: Dict[str, ProfileFingerprint] = {}

    def add(self, user_id: str, fingerprint: ProfileFingerprint) -> None:
        """Register *user_id* with the given *fingerprint*.

        If *user_id* is already registered its fingerprint is replaced.
        """
        self._store[user_id] = fingerprint

    def remove(self, user_id: str) -> None:
        """Remove *user_id* from the store (no-op if not present)."""
        self._store.pop(user_id, None)

    def find_matches(
        self,
        query_user_id: str,
        top_k: int = 10,
        min_similarity: float = 0.0,
    ) -> List[MatchResult]:
        """Return the *top_k* users most similar to *query_user_id*.

        Parameters
        ----------
        query_user_id:
            The user whose matches we want.  Must already be registered.
        top_k:
            Maximum number of results to return.
        min_similarity:
            Exclude results below this similarity threshold.

        Returns
        -------
        list[MatchResult]
            Sorted descending by similarity, excluding *query_user_id* itself.

        Raises
        ------
        KeyError
            If *query_user_id* is not registered in the store.
        """
        if query_user_id not in self._store:
            raise KeyError(f"User {query_user_id!r} not found in matcher store.")

        query_fp = self._store[query_user_id]
        results: list[MatchResult] = []

        for uid, fp in self._store.items():
            if uid == query_user_id:
                continue
            sim = query_fp.similarity(fp)
            if sim >= min_similarity:
                results.append(MatchResult(user_id=uid, similarity=sim))

        results.sort(key=lambda r: r.similarity, reverse=True)
        return results[:top_k]

    def find_matches_for_fingerprint(
        self,
        fingerprint: ProfileFingerprint,
        top_k: int = 10,
        min_similarity: float = 0.0,
    ) -> List[MatchResult]:
        """Find matches for an *anonymous* fingerprint (not stored in the matcher).

        This is useful when a new user wants to find matches without
        registering their fingerprint in the shared store.

        Parameters
        ----------
        fingerprint:
            The query fingerprint.
        top_k:
            Maximum number of results to return.
        min_similarity:
            Exclude results below this threshold.
        """
        results: list[MatchResult] = []
        for uid, fp in self._store.items():
            sim = fingerprint.similarity(fp)
            if sim >= min_similarity:
                results.append(MatchResult(user_id=uid, similarity=sim))

        results.sort(key=lambda r: r.similarity, reverse=True)
        return results[:top_k]

    def __len__(self) -> int:
        return len(self._store)

    def __contains__(self, user_id: str) -> bool:
        return user_id in self._store
