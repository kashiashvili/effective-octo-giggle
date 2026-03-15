from setuptools import setup, find_packages

setup(
    name="profiler",
    version="0.2.0",
    packages=find_packages(exclude=["tests*"]),
    install_requires=[
        "flask>=3.0.0",
        "flask-wtf>=1.2.0",
        "werkzeug>=3.0.0",
        "requests>=2.28.0",
    ],
    entry_points={
        "console_scripts": [
            "profiler-web=profiler.web.app:main",
        ],
    },
    python_requires=">=3.8",
)
