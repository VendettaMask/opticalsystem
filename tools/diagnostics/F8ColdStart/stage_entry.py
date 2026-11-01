"""Prepare an isolated code draft; never applies integration to the active checkout."""
from pathlib import Path
import re
import shutil
import sys

repo, frozen, stage, patch = [Path(arg).resolve() for arg in sys.argv[1:]]
if stage.exists():
    raise SystemExit("Draft stage exists.")
shutil.copytree(frozen, stage, ignore=shutil.ignore_patterns("bin", "obj", ".git"))
text = patch.read_text(encoding="utf-8")
for name in re.findall(r"^\*\*\* Update File: (.+)$", text, re.M):
    source = Path(name).resolve()
    relative = source.relative_to(repo)
    destination = stage / relative
    if not destination.exists():
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
draft = text.replace(repo.as_posix(), stage.as_posix())
(stage / "integration.patch").write_text(draft, encoding="utf-8")
print(stage / "integration.patch")
