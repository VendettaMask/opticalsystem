"""Archive final implementation/binary identity, without changing optical evidence."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

repo = Path(sys.argv[1]).resolve()
base = repo / "validation/rechecks/2026-10-31/f8-completion"
def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()
folders = ("src/OpticalSystemDesign.Core", "labs/InitialStructure/src", "labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests",
           "tools/diagnostics/F8ColdStart", "tools/diagnostics/F8ColdVerify", "tools/diagnostics/F8ColdEntryTests", "tools/diagnostics/F8RealPipeline")
files = [p for name in folders for p in (repo / name).rglob("*") if p.is_file()
         and not {"bin", "obj", ".git", "__pycache__"} & set(p.relative_to(repo).parts)]
files += list((repo / "src/OpticalSystemDesign.App/Laboratories/InitialStructure").glob("Autonomous*.cs"))
files += [repo / "src/OpticalSystemDesign.App/Services/UiLocalization.AutonomousTopology.cs"]
files += [repo / p for p in ("Directory.Build.props", "ProductVersion.props", "global.json", "AGENTS.md") if (repo / p).exists()]
manifest = []
with zipfile.ZipFile(base / "final-implementation-source.zip", "x", zipfile.ZIP_DEFLATED) as archive:
    for p in sorted(set(files)):
        relative = p.relative_to(repo).as_posix()
        archive.writestr(relative, p.read_bytes())
        manifest.append({"path": relative, "sha256": sha(p)})
(base / "final-implementation-source-sha256.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
names = ("Core", "InitialStructure.Contracts", "InitialStructure.Engine", "InitialStructure.Persistence", "App")
manifest = [{"path": (p := Path(f"src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.{name}.dll")).as_posix(),
             "sha256": sha(repo / p)} for name in names]
(base / "final-product-binary-sha256.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
docs = json.loads((base / "documentation-final.json").read_text(encoding="utf-8"))
assert not docs["BrokenLinks"] and not docs["MarkdownFenceIssues"], "Documentation final validation failed."
summary = json.loads((base / "completion-summary.json").read_text(encoding="utf-8"))
assert summary["candidateQualified"] == 5
print("Final implementation/source/product identities archived; documentation and final 5/5 summary verified.")
