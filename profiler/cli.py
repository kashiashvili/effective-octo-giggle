"""Command-line interface for the profiler."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Optional

import click

from profiler.connectors.github import GitHubConnector
from profiler.connectors.goodreads import GoodreadsConnector
from profiler.connectors.netflix import NetflixConnector
from profiler.profile.aggregator import ProfileAggregator
from profiler.profile.fingerprint import FingerprintGenerator


@click.group()
def main() -> None:
    """Profiler – generate and compare privacy-preserving user fingerprints."""


@main.command("generate")
@click.option("--github-user", default=None, help="GitHub username.")
@click.option("--github-token", default=None, envvar="GITHUB_TOKEN", help="GitHub PAT (optional).")
@click.option("--goodreads-csv", default=None, type=click.Path(), help="Path to Goodreads export CSV.")
@click.option("--netflix-csv", default=None, type=click.Path(), help="Path to Netflix viewing history CSV.")
@click.option("--num-hashes", default=128, show_default=True, help="MinHash size.")
@click.option("--output", "-o", default=None, type=click.Path(), help="Write fingerprint JSON to this file.")
def generate_cmd(
    github_user: Optional[str],
    github_token: Optional[str],
    goodreads_csv: Optional[str],
    netflix_csv: Optional[str],
    num_hashes: int,
    output: Optional[str],
) -> None:
    """Gather data from configured sources and generate a fingerprint."""
    connectors = []

    if github_user:
        connectors.append(GitHubConnector(username=github_user, token=github_token))
    if goodreads_csv:
        connectors.append(GoodreadsConnector(csv_source=Path(goodreads_csv)))
    if netflix_csv:
        connectors.append(NetflixConnector(csv_source=Path(netflix_csv)))

    if not connectors:
        click.echo(
            "No data sources configured.  Supply at least one of: "
            "--github-user, --goodreads-csv, --netflix-csv.",
            err=True,
        )
        sys.exit(1)

    click.echo("Fetching data from sources…")
    aggregator = ProfileAggregator(connectors)
    profile = aggregator.aggregate()

    click.echo(
        f"Aggregated {len(profile.features)} features from: {', '.join(profile.sources)}"
    )

    generator = FingerprintGenerator(num_hashes=num_hashes)
    fingerprint = generator.generate(profile.features)

    result = fingerprint.to_dict()

    if output:
        Path(output).write_text(json.dumps(result, indent=2))
        click.echo(f"Fingerprint written to {output}")
    else:
        click.echo(json.dumps(result, indent=2))


@main.command("compare")
@click.argument("fingerprint_a", type=click.Path(exists=True))
@click.argument("fingerprint_b", type=click.Path(exists=True))
def compare_cmd(fingerprint_a: str, fingerprint_b: str) -> None:
    """Compare two fingerprint JSON files and print their similarity score."""
    from profiler.profile.fingerprint import ProfileFingerprint

    fp_a = ProfileFingerprint.from_json(Path(fingerprint_a).read_text())
    fp_b = ProfileFingerprint.from_json(Path(fingerprint_b).read_text())

    try:
        sim = fp_a.similarity(fp_b)
    except ValueError as exc:
        click.echo(f"Error: {exc}", err=True)
        sys.exit(1)

    click.echo(f"Similarity: {sim:.4f} ({sim * 100:.1f}%)")


if __name__ == "__main__":
    main()
