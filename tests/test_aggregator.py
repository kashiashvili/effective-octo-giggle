"""Tests for the profile aggregator."""

from __future__ import annotations

from unittest.mock import MagicMock

import pytest

from profiler.connectors.base import BaseConnector, ConnectorError, ProfileData
from profiler.profile.aggregator import AggregatedProfile, ProfileAggregator


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _make_connector(name: str, features: list[str]) -> BaseConnector:
    """Return a mock connector that yields *features*."""
    conn = MagicMock(spec=BaseConnector)
    conn.name = name
    conn.fetch.return_value = ProfileData(source=name, features=features)
    return conn


def _make_failing_connector(name: str, message: str = "boom") -> BaseConnector:
    conn = MagicMock(spec=BaseConnector)
    conn.name = name
    conn.fetch.side_effect = ConnectorError(message)
    return conn


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

class TestProfileAggregator:
    def test_single_connector(self) -> None:
        conn = _make_connector("github", ["language:python", "topic:ml"])
        agg = ProfileAggregator([conn])
        profile = agg.aggregate()
        assert profile.sources == ["github"]
        assert sorted(profile.features) == ["language:python", "topic:ml"]

    def test_multiple_connectors_merged(self) -> None:
        c1 = _make_connector("github", ["language:python", "topic:ml"])
        c2 = _make_connector("netflix", ["netflix-type:series"])
        agg = ProfileAggregator([c1, c2])
        profile = agg.aggregate()
        assert set(profile.sources) == {"github", "netflix"}
        assert "language:python" in profile.features
        assert "netflix-type:series" in profile.features

    def test_duplicates_removed(self) -> None:
        c1 = _make_connector("github", ["language:python"])
        c2 = _make_connector("google", ["language:python", "interest:ai"])
        agg = ProfileAggregator([c1, c2])
        profile = agg.aggregate()
        # "language:python" should appear only once
        assert profile.features.count("language:python") == 1

    def test_features_sorted(self) -> None:
        c1 = _make_connector("github", ["z:last", "a:first", "m:middle"])
        agg = ProfileAggregator([c1])
        profile = agg.aggregate()
        assert profile.features == sorted(profile.features)

    def test_empty_connectors_list(self) -> None:
        agg = ProfileAggregator([])
        profile = agg.aggregate()
        assert profile.sources == []
        assert profile.features == []

    def test_skip_errors_default(self, capsys) -> None:
        good = _make_connector("github", ["language:python"])
        bad = _make_failing_connector("netflix")
        agg = ProfileAggregator([good, bad])
        profile = agg.aggregate()
        # Should still get data from the good connector
        assert "github" in profile.sources
        assert "netflix" not in profile.sources
        assert "language:python" in profile.features
        # Warning should be printed
        captured = capsys.readouterr()
        assert "WARNING" in captured.out

    def test_strict_mode_raises(self) -> None:
        good = _make_connector("github", ["language:python"])
        bad = _make_failing_connector("netflix")
        agg = ProfileAggregator([good, bad], skip_errors=False)
        with pytest.raises(ConnectorError):
            agg.aggregate()

    def test_result_type(self) -> None:
        agg = ProfileAggregator([_make_connector("github", [])])
        profile = agg.aggregate()
        assert isinstance(profile, AggregatedProfile)
