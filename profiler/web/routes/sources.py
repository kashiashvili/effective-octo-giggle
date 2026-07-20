"""Data-sources routes – dashboard and source connection."""

from __future__ import annotations

import io
import tempfile
from pathlib import Path

from flask import (
    Blueprint,
    flash,
    redirect,
    render_template,
    request,
    session,
    url_for,
)

from profiler.connectors.github import GitHubConnector
from profiler.connectors.goodreads import GoodreadsConnector
from profiler.connectors.netflix import NetflixConnector
from profiler.connectors.google import GoogleConnector
from profiler.connectors.facebook import FacebookConnector
from profiler.profile.aggregator import ProfileAggregator
from profiler.profile.fingerprint import FingerprintGenerator
from profiler.web.db import get_fingerprint, save_fingerprint

sources_bp = Blueprint("sources", __name__)

_GENERATOR = FingerprintGenerator(num_hashes=128)


def _require_login():
    if "user_id" not in session:
        return redirect(url_for("auth.login"))
    return None


@sources_bp.route("/dashboard")
def dashboard():
    redir = _require_login()
    if redir:
        return redir

    fp_data = get_fingerprint(session["user_id"])
    return render_template("dashboard.html", fp_data=fp_data)


@sources_bp.route("/connect", methods=["GET", "POST"])
def connect():
    redir = _require_login()
    if redir:
        return redir

    if request.method == "POST":
        connectors = []
        errors = []

        # --- GitHub ---
        gh_user = request.form.get("github_user", "").strip()
        gh_token = request.form.get("github_token", "").strip() or None
        if gh_user:
            connectors.append(GitHubConnector(username=gh_user, token=gh_token))

        # --- Goodreads CSV upload ---
        gr_file = request.files.get("goodreads_csv")
        if gr_file and gr_file.filename:
            raw = gr_file.stream.read().decode("utf-8", errors="replace")
            connectors.append(GoodreadsConnector(csv_source=raw))

        # --- Netflix CSV upload ---
        nf_file = request.files.get("netflix_csv")
        if nf_file and nf_file.filename:
            raw = nf_file.stream.read().decode("utf-8", errors="replace")
            connectors.append(NetflixConnector(csv_source=raw))

        # --- Google (access token) ---
        google_token = request.form.get("google_token", "").strip()
        if google_token:
            connectors.append(GoogleConnector(access_token=google_token))

        # --- Facebook (access token) ---
        fb_token = request.form.get("facebook_token", "").strip()
        if fb_token:
            connectors.append(FacebookConnector(access_token=fb_token))

        if not connectors:
            flash("Please provide at least one data source.", "warning")
            return redirect(url_for("sources.connect"))

        # Aggregate and generate fingerprint
        aggregator = ProfileAggregator(connectors, skip_errors=True)
        profile = aggregator.aggregate()

        if not profile.features:
            flash(
                "No features could be extracted from the provided sources. "
                "Please check your inputs and try again.",
                "danger",
            )
            return redirect(url_for("sources.connect"))

        fingerprint = _GENERATOR.generate(profile.features)
        save_fingerprint(
            user_id=session["user_id"],
            fingerprint_dict=fingerprint.to_dict(),
            sources=profile.sources,
        )

        flash(
            f"Profile updated! Extracted {len(profile.features)} features "
            f"from: {', '.join(profile.sources)}.",
            "success",
        )
        return redirect(url_for("matches.matches"))

    return render_template("connect.html")
