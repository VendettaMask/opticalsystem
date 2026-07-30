#!/usr/bin/env python3
"""Verify the completeness and integrity of a captured Zemax baseline."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any

from PIL import Image


def read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("baseline", type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    root = args.baseline.resolve()
    manifest = read_json(root / "manifest.json")
    analyses = manifest["analyses"]

    assert len(analyses) == manifest["summary"]["total"]
    assert len({entry["index"] for entry in analyses}) == len(analyses)
    assert len({entry["analysisId"] for entry in analyses}) == len(analyses)

    source = root / "source" / Path(manifest["systemFile"]).name
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    assert source_hash == manifest["sourceSha256"]

    captured = 0
    native_screenshots = 0
    screenshots = 0
    not_applicable = 0
    timeouts = 0
    for entry in analyses:
        folder = root / entry["directory"]
        status = read_json(folder / "status.json")
        assert status["analysisId"] == entry["analysisId"]
        assert status["status"] == entry["status"]

        if entry["status"] == "captured":
            captured += 1
            read_json(folder / "data.json")
            screenshot = folder / entry["screenshot"]
            assert screenshot.is_file() and screenshot.stat().st_size > 0
            with Image.open(screenshot) as image:
                image.verify()
            screenshots += 1
            if entry.get("screenshotStatus") == "captured-by-opticstudio-zpl":
                native_screenshots += 1
            if entry.get("settingsSaved"):
                assert (folder / "settings.cfg").is_file()
            if entry.get("textSaved"):
                assert (folder / "data.txt").is_file()
        elif entry["status"] == "timeout":
            timeouts += 1
        else:
            not_applicable += 1

    assert not [
        path
        for path in root.rglob("screenshot.*")
        if path.name == "screenshot.JPG"
    ]
    expected_summary = {
        "total": len(analyses),
        "captured": captured,
        "notApplicableOrFailed": not_applicable,
        "timeout": timeouts,
        "screenshots": screenshots,
        "opticStudioScreenshots": native_screenshots,
    }
    assert manifest["summary"] == expected_summary

    files = [path for path in root.rglob("*") if path.is_file()]
    print(
        json.dumps(
            {
                **expected_summary,
                "fallbackScreenshots": screenshots - native_screenshots,
                "files": len(files),
                "bytes": sum(path.stat().st_size for path in files),
                "sourceSha256": source_hash,
            },
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
