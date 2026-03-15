"""Privacy-preserving profile fingerprint using MinHash.

Design goals
------------
1. **One-way**: raw feature strings cannot be recovered from the fingerprint.
2. **Comparable**: two fingerprints can be compared to estimate *Jaccard
   similarity* between the underlying feature sets without revealing them.
3. **Deterministic**: the same feature set always produces the same fingerprint,
   so it can be persisted and compared later.

How it works
------------
MinHash is a locality-sensitive hashing technique.  For *k* independent hash
functions h_1 … h_k we compute:

    fingerprint[i] = min{ h_i(f) : f ∈ features }

The expected fraction of positions where two fingerprints agree equals the
Jaccard similarity of the underlying feature sets:

    P(fingerprint_A[i] == fingerprint_B[i]) ≈ |A ∩ B| / |A ∪ B|

No raw feature is exposed – only integer minimum values.

References
----------
* Broder, A.Z. (1997). "On the resemblance and containment of documents."
"""

from __future__ import annotations

import hashlib
import json
import struct
from typing import Iterable, List, Sequence

# Large Mersenne prime used as the hash modulus
_PRIME: int = (1 << 61) - 1
_MAX_HASH: int = _PRIME

# Default number of hash functions (higher → more accurate, larger fingerprint)
DEFAULT_NUM_HASHES: int = 128


def _str_to_int(value: str) -> int:
    """Convert an arbitrary string to a non-negative integer via SHA-256."""
    digest = hashlib.sha256(value.encode("utf-8")).digest()
    # Unpack first 8 bytes as an unsigned 64-bit integer
    (n,) = struct.unpack(">Q", digest[:8])
    return n


def _hash_value(feature_int: int, a: int, b: int) -> int:
    """Universal hash: h(x) = ((a*x + b) mod p) mod p.

    With a, b drawn uniformly from [1, p-1] this is a 2-universal hash family.
    """
    return ((a * feature_int + b) % _PRIME)


def _generate_hash_params(num_hashes: int) -> list[tuple[int, int]]:
    """Generate deterministic (a, b) pairs for each of the *num_hashes* functions.

    We derive them from SHA-256 of the index so that the params are
    reproducible across processes and platforms.
    """
    params: list[tuple[int, int]] = []
    for i in range(num_hashes):
        seed = hashlib.sha256(f"minhash-param-{i}".encode()).digest()
        a = (int.from_bytes(seed[:8], "big") % (_PRIME - 1)) + 1
        b = (int.from_bytes(seed[8:16], "big") % (_PRIME - 1)) + 1
        params.append((a, b))
    return params


class ProfileFingerprint:
    """An immutable MinHash-based fingerprint of a user's feature set.

    Attributes
    ----------
    signature:
        List of *num_hashes* integer minimum values.
    num_hashes:
        Number of hash functions used.  Must match between fingerprints that
        are compared.
    """

    def __init__(self, signature: List[int], num_hashes: int) -> None:
        if len(signature) != num_hashes:
            raise ValueError(
                f"signature length {len(signature)} != num_hashes {num_hashes}"
            )
        self._signature = list(signature)
        self._num_hashes = num_hashes

    @property
    def signature(self) -> List[int]:
        return list(self._signature)

    @property
    def num_hashes(self) -> int:
        return self._num_hashes

    def similarity(self, other: "ProfileFingerprint") -> float:
        """Estimate Jaccard similarity with *other* (value in [0, 1]).

        Raises
        ------
        ValueError
            If the two fingerprints used different numbers of hash functions.
        """
        if self._num_hashes != other._num_hashes:
            raise ValueError(
                "Cannot compare fingerprints with different num_hashes: "
                f"{self._num_hashes} vs {other._num_hashes}"
            )
        matches = sum(a == b for a, b in zip(self._signature, other._signature))
        return matches / self._num_hashes

    def to_dict(self) -> dict:
        """Serialise to a plain dictionary (JSON-safe)."""
        return {"num_hashes": self._num_hashes, "signature": self._signature}

    @classmethod
    def from_dict(cls, data: dict) -> "ProfileFingerprint":
        """Deserialise from a dictionary produced by :meth:`to_dict`."""
        return cls(signature=data["signature"], num_hashes=data["num_hashes"])

    def to_json(self) -> str:
        """Serialise to a JSON string."""
        return json.dumps(self.to_dict())

    @classmethod
    def from_json(cls, text: str) -> "ProfileFingerprint":
        """Deserialise from a JSON string produced by :meth:`to_json`."""
        return cls.from_dict(json.loads(text))

    def __repr__(self) -> str:
        return (
            f"ProfileFingerprint(num_hashes={self._num_hashes}, "
            f"signature=[{self._signature[0]}, ..., {self._signature[-1]}])"
        )


class FingerprintGenerator:
    """Generate :class:`ProfileFingerprint` objects from feature sets.

    Parameters
    ----------
    num_hashes:
        Number of independent hash functions to use.  128 gives roughly ±4 %
        error in similarity estimates; 256 gives ±3 %.
    """

    def __init__(self, num_hashes: int = DEFAULT_NUM_HASHES) -> None:
        self._num_hashes = num_hashes
        self._params = _generate_hash_params(num_hashes)

    def generate(self, features: Iterable[str]) -> ProfileFingerprint:
        """Compute a :class:`ProfileFingerprint` for the given *features*.

        Parameters
        ----------
        features:
            An iterable of lower-case feature strings (e.g. ``["language:python",
            "topic:ml"]``).  Duplicates are ignored.

        Returns
        -------
        ProfileFingerprint
            A privacy-preserving MinHash fingerprint.
        """
        feature_list = list(set(features))  # deduplicate

        if not feature_list:
            # Empty feature set: return a fingerprint of all-max values which
            # will have 0 similarity with any non-empty fingerprint.
            return ProfileFingerprint(
                signature=[_MAX_HASH] * self._num_hashes,
                num_hashes=self._num_hashes,
            )

        # Convert every feature string to an integer
        feature_ints = [_str_to_int(f) for f in feature_list]

        # For each hash function, compute the minimum hash value over all features
        signature: list[int] = []
        for a, b in self._params:
            min_val = min(_hash_value(fi, a, b) for fi in feature_ints)
            signature.append(min_val)

        return ProfileFingerprint(signature=signature, num_hashes=self._num_hashes)
