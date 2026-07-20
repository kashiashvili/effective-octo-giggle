"""Tests for the UserMatcher."""

from __future__ import annotations

import pytest

from profiler.matching.matcher import MatchResult, UserMatcher
from profiler.profile.fingerprint import FingerprintGenerator


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def generator() -> FingerprintGenerator:
    return FingerprintGenerator(num_hashes=128)


@pytest.fixture
def populated_matcher(generator: FingerprintGenerator) -> UserMatcher:
    """Matcher with three users: alice, bob, carol."""
    alice_features = ["language:python", "topic:ml", "topic:data-science"]
    bob_features   = ["language:python", "topic:ml", "language:r"]   # similar to alice
    carol_features = ["language:rust", "topic:embedded", "os:linux"]   # different

    matcher = UserMatcher()
    matcher.add("alice", generator.generate(alice_features))
    matcher.add("bob",   generator.generate(bob_features))
    matcher.add("carol", generator.generate(carol_features))
    return matcher


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

class TestUserMatcher:
    def test_add_and_contains(self, populated_matcher: UserMatcher) -> None:
        assert "alice" in populated_matcher
        assert "bob"   in populated_matcher
        assert "carol" in populated_matcher
        assert "dave"  not in populated_matcher

    def test_len(self, populated_matcher: UserMatcher) -> None:
        assert len(populated_matcher) == 3

    def test_remove(self, populated_matcher: UserMatcher) -> None:
        populated_matcher.remove("carol")
        assert "carol" not in populated_matcher
        assert len(populated_matcher) == 2

    def test_remove_nonexistent_is_noop(self, populated_matcher: UserMatcher) -> None:
        populated_matcher.remove("nobody")  # should not raise

    def test_find_matches_returns_list(self, populated_matcher: UserMatcher) -> None:
        results = populated_matcher.find_matches("alice")
        assert isinstance(results, list)

    def test_find_matches_excludes_self(self, populated_matcher: UserMatcher) -> None:
        results = populated_matcher.find_matches("alice")
        user_ids = [r.user_id for r in results]
        assert "alice" not in user_ids

    def test_find_matches_sorted_descending(
        self, populated_matcher: UserMatcher
    ) -> None:
        results = populated_matcher.find_matches("alice")
        sims = [r.similarity for r in results]
        assert sims == sorted(sims, reverse=True)

    def test_find_matches_top_k(self, populated_matcher: UserMatcher) -> None:
        results = populated_matcher.find_matches("alice", top_k=1)
        assert len(results) <= 1

    def test_find_matches_min_similarity(self, populated_matcher: UserMatcher) -> None:
        # carol is very different – filtering at high threshold should exclude her
        results = populated_matcher.find_matches("alice", min_similarity=0.9)
        for r in results:
            assert r.similarity >= 0.9

    def test_bob_closer_to_alice_than_carol(
        self, populated_matcher: UserMatcher
    ) -> None:
        results = populated_matcher.find_matches("alice")
        by_id = {r.user_id: r.similarity for r in results}
        assert by_id["bob"] > by_id["carol"]

    def test_unknown_user_raises(self, populated_matcher: UserMatcher) -> None:
        with pytest.raises(KeyError):
            populated_matcher.find_matches("nobody")

    def test_find_matches_for_fingerprint(
        self,
        populated_matcher: UserMatcher,
        generator: FingerprintGenerator,
    ) -> None:
        # Anonymous fingerprint similar to alice
        anon_fp = generator.generate(["language:python", "topic:ml"])
        results = populated_matcher.find_matches_for_fingerprint(anon_fp, top_k=3)
        assert len(results) <= 3
        for r in results:
            assert isinstance(r, MatchResult)
            assert 0.0 <= r.similarity <= 1.0

    def test_match_result_repr(self, populated_matcher: UserMatcher) -> None:
        results = populated_matcher.find_matches("alice")
        for r in results:
            assert repr(r).startswith("MatchResult(")

    def test_overwrite_fingerprint(
        self,
        populated_matcher: UserMatcher,
        generator: FingerprintGenerator,
    ) -> None:
        new_fp = generator.generate(["completely:different"])
        populated_matcher.add("alice", new_fp)
        # Verify by checking that similarity of alice with herself is 1.0 via
        # find_matches_for_fingerprint (identical fp should not match others perfectly)
        populated_matcher.add("alice_copy", new_fp)
        results = populated_matcher.find_matches_for_fingerprint(new_fp, top_k=5)
        by_id = {r.user_id: r.similarity for r in results}
        # alice_copy shares the same fingerprint as new_fp → similarity 1.0
        assert by_id.get("alice_copy", 0.0) == pytest.approx(1.0)
