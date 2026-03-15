"""Profile aggregator – merges :class:`ProfileData` from multiple connectors
into a single, de-duplicated feature set.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Iterable, List

from profiler.connectors.base import BaseConnector, ProfileData


@dataclass
class AggregatedProfile:
    """The result of aggregating data from one or more connectors.

    Attributes:
        sources:  Names of all connectors that contributed data.
        features: De-duplicated, sorted list of all feature strings.
    """

    sources: List[str] = field(default_factory=list)
    features: List[str] = field(default_factory=list)


class ProfileAggregator:
    """Run a collection of connectors and merge their outputs.

    Parameters
    ----------
    connectors:
        Any iterable of :class:`~profiler.connectors.base.BaseConnector`
        instances to run.
    skip_errors:
        If ``True`` (default), a connector that raises
        :class:`~profiler.connectors.base.ConnectorError` is skipped and a
        warning is printed instead of propagating the exception.  Set to
        ``False`` for strict mode.
    """

    def __init__(
        self,
        connectors: Iterable[BaseConnector],
        skip_errors: bool = True,
    ) -> None:
        self._connectors = list(connectors)
        self._skip_errors = skip_errors

    def aggregate(self) -> AggregatedProfile:
        """Fetch data from every connector and return an :class:`AggregatedProfile`."""
        from profiler.connectors.base import ConnectorError

        sources: list[str] = []
        all_features: set[str] = set()

        for connector in self._connectors:
            try:
                data: ProfileData = connector.fetch()
                sources.append(data.source)
                all_features.update(data.features)
            except ConnectorError as exc:
                if self._skip_errors:
                    print(f"[profiler] WARNING: {connector.name} connector failed – {exc}")
                else:
                    raise

        return AggregatedProfile(
            sources=sources,
            features=sorted(all_features),
        )
