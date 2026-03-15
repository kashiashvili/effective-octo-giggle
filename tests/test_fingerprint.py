"""Tests for the MinHash fingerprint module."""

from __future__ import annotations

import json

import pytest

from profiler.profile.fingerprint import (
    DEFAULT_NUM_HASHES,
    FingerprintGenerator,
    ProfileFingerprint,
    _generate_hash_params,
    _str_to_int,
)


# ---------------------------------------------------------------------------
# Helper fixtures
# ---------------------------------------------------------------------------

FEATURES_A = ["language:python", "topic:machine-learning", "topic:data-science", "language:r"]
FEATURES_B = ["language:python", "topic:machine-learning", "language:javascript", "topic:web"]
FEATURES_C = ["language:rust", "topic:embedded", "os:linux"]  # no overlap with A


@pytest.fixture
def generator() -> FingerprintGenerator:
    return FingerprintGenerator(num_hashes=128)


# ---------------------------------------------------------------------------
# Unit tests
# ---------------------------------------------------------------------------


class TestStrToInt:
    def test_deterministic(self) -> None:
        assert _str_to_int("language:python") == _str_to_int("language:python")

    def test_different_strings_differ(self) -> None:
        assert _str_to_int("language:python") != _str_to_int("language:java")

    def test_non_negative(self) -> None:
        assert _str_to_int("any string") >= 0


class TestGenerateHashParams:
    def test_correct_length(self) -> None:
        params = _generate_hash_params(64)
        assert len(params) == 64

    def test_deterministic(self) -> None:
        p1 = _generate_hash_params(32)
        p2 = _generate_hash_params(32)
        assert p1 == p2

    def test_params_are_positive(self) -> None:
        for a, b in _generate_hash_params(10):
            assert a > 0
            assert b > 0


class TestFingerprintGenerator:
    def test_signature_length(self, generator: FingerprintGenerator) -> None:
        fp = generator.generate(FEATURES_A)
        assert len(fp.signature) == 128

    def test_num_hashes_attribute(self, generator: FingerprintGenerator) -> None:
        fp = generator.generate(FEATURES_A)
        assert fp.num_hashes == 128

    def test_deterministic(self, generator: FingerprintGenerator) -> None:
        fp1 = generator.generate(FEATURES_A)
        fp2 = generator.generate(FEATURES_A)
        assert fp1.signature == fp2.signature

    def test_duplicate_features_ignored(self, generator: FingerprintGenerator) -> None:
        fp1 = generator.generate(["language:python", "language:python"])
        fp2 = generator.generate(["language:python"])
        assert fp1.signature == fp2.signature

    def test_order_independence(self, generator: FingerprintGenerator) -> None:
        fp1 = generator.generate(["a", "b", "c"])
        fp2 = generator.generate(["c", "a", "b"])
        assert fp1.signature == fp2.signature

    def test_empty_features(self, generator: FingerprintGenerator) -> None:
        fp = generator.generate([])
        assert len(fp.signature) == 128

    def test_similarity_with_self_is_one(self, generator: FingerprintGenerator) -> None:
        fp = generator.generate(FEATURES_A)
        assert fp.similarity(fp) == pytest.approx(1.0)

    def test_empty_vs_nonempty_similarity_is_zero(
        self, generator: FingerprintGenerator
    ) -> None:
        fp_empty = generator.generate([])
        fp_full = generator.generate(FEATURES_A)
        assert fp_empty.similarity(fp_full) == pytest.approx(0.0)

    def test_partial_overlap_similarity(self, generator: FingerprintGenerator) -> None:
        """Two sets with ~50 % overlap should have estimated sim > 0."""
        fp_a = generator.generate(FEATURES_A)
        fp_b = generator.generate(FEATURES_B)
        sim = fp_a.similarity(fp_b)
        assert 0.0 < sim < 1.0

    def test_disjoint_similarity_near_zero(self, generator: FingerprintGenerator) -> None:
        """Disjoint sets should produce near-zero similarity."""
        fp_a = generator.generate(FEATURES_A)
        fp_c = generator.generate(FEATURES_C)
        sim = fp_a.similarity(fp_c)
        assert sim < 0.2  # allows for MinHash estimation noise

    def test_high_overlap_similarity(self, generator: FingerprintGenerator) -> None:
        """Near-identical sets should produce high similarity."""
        base = ["feature:" + str(i) for i in range(100)]
        almost_same = base + ["extra:1"]
        fp1 = generator.generate(base)
        fp2 = generator.generate(almost_same)
        sim = fp1.similarity(fp2)
        assert sim > 0.8

    def test_custom_num_hashes(self) -> None:
        gen = FingerprintGenerator(num_hashes=64)
        fp = gen.generate(FEATURES_A)
        assert fp.num_hashes == 64
        assert len(fp.signature) == 64


class TestProfileFingerprintSerialization:
    def test_to_dict_round_trip(self, generator: FingerprintGenerator) -> None:
        fp = generator.generate(FEATURES_A)
        d = fp.to_dict()
        fp2 = ProfileFingerprint.from_dict(d)
        assert fp.signature == fp2.signature
        assert fp.num_hashes == fp2.num_hashes

    def test_to_json_round_trip(self, generator: FingerprintGenerator) -> None:
        fp = generator.generate(FEATURES_A)
        s = fp.to_json()
        fp2 = ProfileFingerprint.from_json(s)
        assert fp.signature == fp2.signature

    def test_json_is_valid_json(self, generator: FingerprintGenerator) -> None:
        fp = generator.generate(FEATURES_A)
        parsed = json.loads(fp.to_json())
        assert "signature" in parsed
        assert "num_hashes" in parsed

    def test_mismatched_num_hashes_raises(self, generator: FingerprintGenerator) -> None:
        fp_128 = generator.generate(FEATURES_A)
        fp_64 = FingerprintGenerator(num_hashes=64).generate(FEATURES_A)
        with pytest.raises(ValueError, match="num_hashes"):
            fp_128.similarity(fp_64)

    def test_wrong_signature_length_raises(self) -> None:
        with pytest.raises(ValueError):
            ProfileFingerprint(signature=[1, 2, 3], num_hashes=5)
