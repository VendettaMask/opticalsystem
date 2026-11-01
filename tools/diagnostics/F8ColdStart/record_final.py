"""Record final source/binary identity and scoped documentation checks; no optical calculations."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

repo, evidence = (Path(arg).resolve() for arg in sys.argv[1:])
digest = lambda data: hashlib.sha256(data).hexdigest()
folders = ["src/OpticalSystemDesign.Core", "labs/InitialStructure/src", "tools/diagnostics/F8ColdStart",
           "tools/diagnostics/F8ColdVerify", "tools/diagnostics/F8ColdEntryTests"]
files = [p for folder in folders for p in (repo / folder).rglob("*")
         if p.is_file() and not {"bin", "obj", ".git", "__pycache__"} & set(p.relative_to(repo).parts)]
files += list((repo / "src/OpticalSystemDesign.App/Laboratories/InitialStructure").glob("Autonomous*.cs"))
files += [repo / "src/OpticalSystemDesign.App/Services/UiLocalization.AutonomousTopology.cs",
          repo / "labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/ColdStartDesignTests.cs",
          repo / "labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/AutonomousColdStartDesktopTests.cs"]
files += [repo / name for name in ("Directory.Build.props", "ProductVersion.props", "global.json") if (repo / name).exists()]
manifest = []
with zipfile.ZipFile(evidence / "implementation-source.zip", "x", zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(set(files)):
        data = path.read_bytes(); relative = path.relative_to(repo).as_posix()
        archive.writestr(relative, data); manifest.append({"path": relative, "sha256": digest(data)})
(evidence / "implementation-source-sha256.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
binary_names = ["OpticalSystemDesign.Core", "OpticalSystemDesign.InitialStructure.Contracts",
                "OpticalSystemDesign.InitialStructure.Engine", "OpticalSystemDesign.InitialStructure.Persistence", "OpticalSystemDesign.App"]
binary_manifest = [{"path": f"src/OpticalSystemDesign.App/bin/Release/net10.0/{name}.dll",
                    "sha256": digest((repo / f"src/OpticalSystemDesign.App/bin/Release/net10.0/{name}.dll").read_bytes())} for name in binary_names]
(evidence / "product-binary-sha256.json").write_text(json.dumps(binary_manifest, indent=2), encoding="utf-8")
frozen = json.loads((evidence / "paired-v1/source-sha256.json").read_text(encoding="utf-8"))
cold_path = "labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/ColdStartDesignSearch.cs"
assert next(x["sha256"] for x in frozen if x["path"] == cold_path) == digest((repo / cold_path).read_bytes()), "Cold algorithm changed after freeze."
audit_name = "documentation-final-refreshed.json" if (evidence / "documentation-final-refreshed.json").exists() else "documentation-final.json"
report = json.loads((evidence / audit_name).read_text(encoding="utf-8"))
scope = ["docs/INITIAL_STRUCTURE_F8_COLD_START_2026-10-31.md", "docs/INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md",
         "docs/CURRENT_STATUS.md", "labs/InitialStructure/README.md", "validation/rechecks/2026-10-31/f8-cold-start/README.md"]
scoped = {"sourceAudit": audit_name, "files": scope,
          "brokenLinks": [x for x in report["BrokenLinks"] if x["Path"] in scope],
          "markdownFenceIssues": [x for x in report["MarkdownFenceIssues"] if x["Path"] in scope],
          "outsideScopeBrokenLinks": [x for x in report["BrokenLinks"] if x["Path"] not in scope]}
(evidence / "documentation-scoped.json").write_text(json.dumps(scoped, indent=2, ensure_ascii=False), encoding="utf-8")
assert not scoped["brokenLinks"] and not scoped["markdownFenceIssues"], "Scoped documentation failed."
print(f"Recorded {len(manifest)} scoped source files and {len(binary_manifest)} product binaries; cold algorithm unchanged; scoped documentation passed.")
