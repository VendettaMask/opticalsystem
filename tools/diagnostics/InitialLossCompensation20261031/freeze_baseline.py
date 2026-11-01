"""Evidence housekeeping only; all optical computation remains in the C# runner."""
from pathlib import Path
import hashlib
import json
import shutil
import sys
import zipfile

root, frozen, evidence = (Path(value).resolve() for value in sys.argv[1:])
if frozen.exists() or evidence.exists():
    raise SystemExit("Use new directories; never overwrite frozen evidence.")
assert frozen.is_relative_to(Path("D:/Projects").resolve())
assert evidence.is_relative_to(root / "validation" / "rechecks")
folders = ["src/OpticalSystemDesign.Core", "labs/InitialStructure/src", "tools/diagnostics/InitialSearchM2"]
files = [root / name for name in ("Directory.Build.props", "Directory.Build.targets", "ProductVersion.props",
                                 "global.json", "NuGet.Config", "nuget.config", "labs/InitialStructure/Directory.Build.props")
         if (root / name).is_file()]
for folder in folders:
    files.extend(path for path in (root / folder).rglob("*") if path.is_file()
                 and not set(path.relative_to(root / folder).parts) & {"bin", "obj", ".git"})
frozen.mkdir(parents=True)
evidence.mkdir(parents=True)
manifest = []
with zipfile.ZipFile(evidence / "baseline-source.zip", "w", zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(set(files)):
        relative = path.relative_to(root)
        data = path.read_bytes()
        target = frozen / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        archive.writestr(relative.as_posix(), data)
        manifest.append({"path": relative.as_posix(), "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)})
(evidence / "baseline-source-sha256.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
print(json.dumps({"files": len(manifest), "frozen_source": str(frozen), "evidence": str(evidence)}))
