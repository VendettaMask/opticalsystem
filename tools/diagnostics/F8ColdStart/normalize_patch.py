"""Combine non-overlapping hunks for a file into one apply_patch operation."""
from pathlib import Path
import sys

path = Path(sys.argv[1])
sections = {}
header = None
for line in path.read_text(encoding="utf-8").splitlines():
    if line.startswith(("*** Update File:", "*** Add File:")):
        header = line
        sections.setdefault(header, [])
    elif line in ("*** Begin Patch", "*** End Patch"):
        continue
    elif header is not None:
        sections[header].append(line)
path.write_text("\n".join(["*** Begin Patch", *[line for header, body in sections.items() for line in [header, *body]], "*** End Patch", ""]), encoding="utf-8")
