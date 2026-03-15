"""Matches routes – show similar users."""

from __future__ import annotations

from flask import Blueprint, flash, redirect, render_template, session, url_for

from profiler.matching.matcher import UserMatcher
from profiler.profile.fingerprint import ProfileFingerprint
from profiler.web.db import get_fingerprint, list_all_fingerprints

matches_bp = Blueprint("matches", __name__)


@matches_bp.route("/matches")
def matches():
    if "user_id" not in session:
        return redirect(url_for("auth.login"))

    my_fp_data = get_fingerprint(session["user_id"])
    if my_fp_data is None:
        flash(
            "You haven't connected any data sources yet. "
            "Connect at least one source to see your matches.",
            "warning",
        )
        return redirect(url_for("sources.connect"))

    my_fp = ProfileFingerprint.from_dict(my_fp_data["fingerprint"])

    # Build matcher from all stored fingerprints
    all_fp_rows = list_all_fingerprints()
    matcher = UserMatcher()
    for row in all_fp_rows:
        try:
            fp = ProfileFingerprint.from_dict(row["fingerprint"])
            matcher.add(str(row["user_id"]), fp)
        except (KeyError, ValueError):
            continue

    # Ensure current user is registered
    uid_str = str(session["user_id"])
    if uid_str not in matcher:
        matcher.add(uid_str, my_fp)

    results = matcher.find_matches(uid_str, top_k=20, min_similarity=0.0)

    # Attach usernames
    uid_to_name = {str(r["user_id"]): r["username"] for r in all_fp_rows}
    match_list = [
        {
            "username": uid_to_name.get(r.user_id, r.user_id),
            "similarity": round(r.similarity * 100, 1),
            "sources": next(
                (row["sources"] for row in all_fp_rows if str(row["user_id"]) == r.user_id),
                [],
            ),
        }
        for r in results
    ]

    return render_template(
        "matches.html",
        match_list=match_list,
        my_sources=my_fp_data["sources"],
        total_users=len(all_fp_rows),
    )
