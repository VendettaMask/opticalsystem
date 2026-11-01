"""Read-only source capture for an isolated build; never copies build outputs."""
from pathlib import Path
import sys

repo, frozen = (Path(arg).resolve() for arg in sys.argv[1:])
if frozen.exists():
    raise SystemExit("Frozen source folder exists.")
folders = ("src/OpticalSystemDesign.Core", "labs/InitialStructure/src", "tools/diagnostics/InitialLossRound2",
           "tools/diagnostics/InitialLossRound2Verify")
files = [p for folder in folders for p in (repo / folder).rglob("*") if p.is_file()
         and not {"bin", "obj", ".git", "__pycache__"} & set(p.relative_to(repo).parts)]
files += [p for name in ("Directory.Build.props", "Directory.Build.targets", "ProductVersion.props", "global.json",
                        "NuGet.Config", "nuget.config", "labs/InitialStructure/Directory.Build.props")
          if (p := repo / name).is_file()]
for path in sorted(set(files)):
    destination = frozen / path.relative_to(repo)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(path.read_bytes())
print(f"Captured {len(set(files))} files without changing the working tree.")
