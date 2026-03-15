from setuptools import setup, find_packages

setup(
    name="profiler",
    version="0.1.0",
    packages=find_packages(exclude=["tests*"]),
    install_requires=[
        "requests>=2.28.0",
        "click>=8.1.0",
    ],
    entry_points={
        "console_scripts": [
            "profiler=profiler.cli:main",
        ],
    },
    python_requires=">=3.8",
)
