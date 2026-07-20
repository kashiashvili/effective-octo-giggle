"""Authentication routes – register, login, logout."""

from __future__ import annotations

from flask import (
    Blueprint,
    flash,
    redirect,
    render_template,
    request,
    session,
    url_for,
)
from werkzeug.security import check_password_hash, generate_password_hash

from profiler.web.db import create_user, get_user_by_username

auth_bp = Blueprint("auth", __name__)


@auth_bp.route("/")
def index():
    return render_template("index.html")


@auth_bp.route("/register", methods=["GET", "POST"])
def register():
    if request.method == "POST":
        username = request.form.get("username", "").strip()
        password = request.form.get("password", "")
        confirm  = request.form.get("confirm_password", "")

        error = None
        if not username:
            error = "Username is required."
        elif len(username) < 3:
            error = "Username must be at least 3 characters."
        elif not password:
            error = "Password is required."
        elif len(password) < 6:
            error = "Password must be at least 6 characters."
        elif password != confirm:
            error = "Passwords do not match."
        elif get_user_by_username(username):
            error = "Username already taken."

        if error:
            flash(error, "danger")
        else:
            create_user(username, generate_password_hash(password))
            flash("Account created! Please sign in.", "success")
            return redirect(url_for("auth.login"), 303)

    return render_template("register.html")


@auth_bp.route("/login", methods=["GET", "POST"])
def login():
    if request.method == "POST":
        username = request.form.get("username", "").strip()
        password = request.form.get("password", "")

        user = get_user_by_username(username)
        if user is None or not check_password_hash(user["password"], password):
            flash("Invalid username or password.", "danger")
        else:
            session.clear()
            session["user_id"] = user["id"]
            session["username"] = user["username"]
            return redirect(url_for("sources.dashboard"), 303)

    return render_template("login.html")


@auth_bp.route("/logout")
def logout():
    session.clear()
    return redirect(url_for("auth.index"))
