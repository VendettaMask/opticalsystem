# -*- coding: utf-8 -*-
"""Verify the final sync snapshot, preserving historical evidence byte-for-byte."""
from datetime import datetime, timezone
from pathlib import Path
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[3]
stage = Path(__file__).resolve().parent
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def record(path):
    path = Path(path).resolve()
    data = path.read_bytes()
    return {"path": str(path.relative_to(root)), "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest()}


def tests(name, expected):
    tree = ET.parse(stage / name)
    counters = {key: int(value) for key, value in tree.find(".//t:Counters", ns).attrib.items()}
    assert counters["total"] == counters["executed"] == counters["passed"] == expected
    assert counters["failed"] == counters["notExecuted"] == 0
    return {"counters": counters, "trx": record(stage / name)}


builds = []
for name in ["debug-final-build.log", "release-final-build.log",
             "coating-debug-final-build.log", "coating-release-final-build.log"]:
    text = (stage / name).read_text()
    assert "0 个警告" in text and "0 个错误" in text
    builds.append(record(stage / name))
for name in ["format-main-final.log", "format-coating-final.log"]:
    assert (stage / name).read_text() == ""
subprocess.run(["git", "diff", "--cached", "--check"], cwd=root, check=True)

previous = json.loads((root / "artifacts/validation/mtf-tolerancing-20261004/verification.json").read_text())
baseline = []
for expected in previous["baselineIntegrity"]:
    actual = record(root / expected["path"])
    assert actual["sha256"] == expected["sha256"]
    assert subprocess.check_output(["git", "show", "HEAD:" + expected["path"]], cwd=root) == (root / expected["path"]).read_bytes()
    baseline.append({**actual, "byteEqualPreSyncHead": True})
assert len(baseline) == 29

evidence = [root / name for name in (stage / "evidence-files.txt").read_text().splitlines()]
# Batch-read the staged blobs: attributes must not alter logs, captures or fixtures.
reader = subprocess.Popen(["git", "cat-file", "--batch"], cwd=root,
                          stdin=subprocess.PIPE, stdout=subprocess.PIPE)
for path in evidence:
    reader.stdin.write((":" + str(path.relative_to(root)) + "\n").encode())
    reader.stdin.flush()
    header = reader.stdout.readline().decode().split()
    assert len(header) == 3 and header[1] == "blob", (path, header)
    data = reader.stdout.read(int(header[2]))
    assert reader.stdout.read(1) == b"\n"
    assert data == path.read_bytes(), path
reader.stdin.close()
assert reader.wait() == 0

staged = subprocess.check_output(["git", "diff", "--cached", "--name-only", "-z"], cwd=root).decode().split("\0")
project = [root / name for name in staged if name and not name.startswith("artifacts/")]
documents = [root / name for name in dict.fromkeys((stage / "documentation-files.txt").read_text().splitlines())]
links = []
for document in documents:
    markdown = re.sub(r"```[\s\S]*?```", "", document.read_text())
    markdown = re.sub(r"`[^`\n]*`", "", markdown)
    for target in re.findall(r"\]\(([^)]+)\)", markdown):
        if "://" in target or target.startswith("#"):
            continue
        path = (document.parent / target.split("#", 1)[0]).resolve()
        assert path.exists() or path == stage / "verification.json", (document, target)
        links.append(str(path.relative_to(root)))

result = {
    "clientDate": "2026-10-05", "recordedAt": datetime.now(timezone.utc).isoformat(),
    "preSyncHead": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root).decode().strip(),
    "branch": "main", "remote": "origin", "scope": "Cumulative project changes and documentation-referenced evidence; no generated binaries or dumps.",
    "finalBuilds": builds,
    "finalTests": {"cumulativeRelease": tests("cumulative-final-release.trx", 3500),
                   "assemblySidebarDebug": tests("assembly-sidebar-final-debug.trx", 25),
                   "assemblySidebarRelease": tests("assembly-sidebar-final-release.trx", 25),
                   "coatingDebug": tests("coating-final-debug.trx", 44),
                   "coatingRelease": tests("coating-final-release.trx", 44)},
    "initialPassingRuns": {"cumulativeRelease": tests("cumulative-release.trx", 3500),
                           "assemblySidebarRelease": tests("assembly-sidebar-release.trx", 25),
                           "coatingRelease": tests("coating-release.trx", 44)},
    "projectFiles": [record(path) for path in project],
    "referencedEvidence": [record(path) for path in evidence],
    "stagedEvidenceBytePreserved": len(evidence),
    "baselineIntegrity": baseline,
    "formatting": {"whitespaceOnlyChangedFiles": (stage / "format-changed-files.txt").read_text().splitlines(),
                   "main": record(stage / "format-main-final.log"),
                   "coating": record(stage / "format-coating-final.log")},
    "documentation": [record(path) for path in documents],
    "checkedLocalLinks": len(links),
    "limitations": ["Cumulative Debug 3500 remains the 2026-10-04 record; not rerun in this sync task.",
                    "Test sets overlap and must not be summed as a total repository count.",
                    "No new native Zemax capture, Optiland comparison, cross-platform desktop or installer verification."]}
(stage / "verification.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
print(json.dumps({"cumulativeRelease": 3500, "assemblySidebarEachConfiguration": 25,
                  "coatingEachConfiguration": 44, "projectFiles": len(project),
                  "bytePreservedEvidenceFiles": len(evidence), "localLinks": len(links)}))
