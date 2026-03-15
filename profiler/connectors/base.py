"""Base connector interface for all data-source connectors."""

from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import Dict, List


@dataclass
class ProfileData:
    """Holds the profile data returned by a single connector.

    Attributes:
        source:   Human-readable name of the data source (e.g. "github").
        features: Normalised, lower-case feature strings extracted from the
                  source (e.g. "language:python", "topic:machine-learning").
        metadata: Optional raw metadata kept locally for debugging.  This is
                  *never* included in the privacy-preserving fingerprint.
    """

    source: str
    features: List[str] = field(default_factory=list)
    metadata: Dict[str, object] = field(default_factory=dict)


class BaseConnector(ABC):
    """Abstract base class that every source connector must implement."""

    @property
    @abstractmethod
    def name(self) -> str:
        """Return the connector's canonical source name."""

    @abstractmethod
    def fetch(self) -> ProfileData:
        """Fetch data from the source and return a :class:`ProfileData`.

        Implementations must honour the following contract:

        * All feature strings must be lower-case and of the form
          ``"<category>:<value>"``, e.g. ``"language:python"``.
        * No personally-identifiable raw values should be stored in
          ``features`` – only normalised categorical labels.
        * Network errors must raise :class:`ConnectorError`.
        """

    def __repr__(self) -> str:
        return f"<{self.__class__.__name__} source={self.name!r}>"


class ConnectorError(Exception):
    """Raised when a connector fails to retrieve data."""
