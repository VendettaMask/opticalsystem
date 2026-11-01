"""Snapshot and evidence housekeeping only; no optical calculations."""
from pathlib import Path
import hashlib
import json
import shutil
import sys
import zipfile

mode, repo_arg, frozen_arg, evidence_arg = sys.argv[1:]
repo, frozen, evidence = map(lambda p: Path(p).resolve(), (repo_arg, frozen_arg, evidence_arg))
if mode == "sources":
    if frozen.exists():
        raise SystemExit("Source snapshot exists.")
    files = [p for folder in ("src/OpticalSystemDesign.Core", "labs/InitialStructure/src", "tools/diagnostics/F8ColdStart")
             for p in (repo / folder).rglob("*") if p.is_file() and not {"bin", "obj", ".git", "__pycache__"} & set(p.relative_to(repo).parts)]
    files += [p for name in ("Directory.Build.props", "Directory.Build.targets", "ProductVersion.props", "global.json", "NuGet.Config",
                             "nuget.config", "labs/InitialStructure/Directory.Build.props") if (p := repo / name).is_file()]
    evidence.mkdir(parents=True, exist_ok=True)
    manifest = []
    with zipfile.ZipFile(evidence / "source.zip", "x", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(set(files)):
            relative = path.relative_to(repo)
            data = path.read_bytes()
            destination = frozen / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
            archive.writestr(relative.as_posix(), data)
            manifest.append({"path": relative.as_posix(), "sha256": hashlib.sha256(data).hexdigest()})
    (evidence / "source-sha256.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"Captured {len(manifest)} files.")
elif mode == "runtime":
    shutil.copytree(frozen / "tools/diagnostics/F8ColdStart/bin/Release/net10.0", evidence / "runtime")
    manifest = [{"path": p.relative_to(evidence).as_posix(), "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
                for p in sorted((evidence / "runtime").rglob("*")) if p.is_file()]
    (evidence / "runtime-sha256.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    protocol = {"version": "f8-cold-paired-v1", "seeds": list(range(42,47)), "arms": ["control","candidate"],
                "budget": {"functions":600,"gradients":600,"rays":10000000,"seconds":600},
                "cold": "Fresh process and RNG per seed; source shapes erased; no saved candidate/checkpoint imports; historical acquisition is not reused.",
                "input": "Same original task seed44 input as a specification envelope; only RNG seed changes.",
                "comparison": "Both arms use the same sphere/A4/A6 compensation and limits; control samples original minimum back focus, candidate explores original legal terminal slack.",
                "execution": "Two bounded workers by arm, fresh normal-priority single-computation-thread process each run; no wall-time speedup claims.",
                "gates": "Original physical apertures, reference/stop/image, spectra/fields, material/curvature/profile permissions and TopologyValidator unchanged.",
                "excluded": "F4 retains its original specification-conflict record. No DeepSeek, GUI, commits or publishing."}
    if (evidence / "protocol-design.json").exists():
        protocol = json.loads((evidence / "protocol-design.json").read_text(encoding="utf-8"))
    (evidence / "protocol.json").write_text(json.dumps(protocol,indent=2),encoding="utf-8")
    print("Common runtime and protocol frozen.")
else:
    raise SystemExit("Unknown mode.")
