"""Flask application factory."""

from __future__ import annotations

import logging
import os
from pathlib import Path

from flask import Flask

from profiler.web.db import init_db, set_db_path

_DEFAULT_SECRET = "dev-secret-change-in-production"
logger = logging.getLogger(__name__)


def create_app(test_config: dict | None = None) -> Flask:
    app = Flask(__name__, template_folder="templates", static_folder="static")

    # Secret key for session / CSRF
    secret_key = os.environ.get("SECRET_KEY", _DEFAULT_SECRET)
    if secret_key == _DEFAULT_SECRET and not app.testing:
        logger.warning(
            "Using the default SECRET_KEY. Set the SECRET_KEY environment "
            "variable to a strong random value before deploying to production."
        )
    app.config["SECRET_KEY"] = secret_key
    app.config["MAX_CONTENT_LENGTH"] = 10 * 1024 * 1024  # 10 MB upload limit

    if test_config is not None:
        app.config.update(test_config)

    # Database path
    db_path = app.config.get("DATABASE", Path("profiler_data.db"))
    set_db_path(Path(db_path))

    with app.app_context():
        init_db()

    # Register blueprints
    from profiler.web.routes.auth import auth_bp
    from profiler.web.routes.sources import sources_bp
    from profiler.web.routes.matches import matches_bp

    app.register_blueprint(auth_bp)
    app.register_blueprint(sources_bp)
    app.register_blueprint(matches_bp)

    return app


def main() -> None:
    """Entry-point for ``profiler-web`` command."""
    app = create_app()
    host = os.environ.get("HOST", "127.0.0.1")
    port = int(os.environ.get("PORT", "5000"))
    app.run(host=host, port=port, debug=False)


if __name__ == "__main__":
    main()
