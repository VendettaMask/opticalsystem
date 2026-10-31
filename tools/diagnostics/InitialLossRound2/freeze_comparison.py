"""Freeze evidence only. Optical computation is exclusively the C# runner/Core."""
import hashlib
import json
from pathlib import Path
import shutil
import sys
import zipfile

frozen, destination = (Path(value).resolve() for value in sys.argv[1:])
if destination.exists():
    raise SystemExit("Existing comparison evidence is never overwritten.")
destination.mkdir(parents=True)
runtime = frozen / "tools/diagnostics/InitialLossRound2/bin/Release/net10.0"
shutil.copytree(runtime, destination / "runtime")
sources = []
for folder in ("src/OpticalSystemDesign.Core", "labs/InitialStructure/src", "tools/diagnostics/InitialLossRound2"):
    sources.extend(p for p in (frozen / folder).rglob("*") if p.is_file()
                   and not {"bin", "obj", ".git", "__pycache__"} & set(p.relative_to(frozen).parts))
sources.extend(p for name in ("Directory.Build.props", "Directory.Build.targets", "ProductVersion.props", "global.json",
                             "NuGet.Config", "nuget.config", "labs/InitialStructure/Directory.Build.props")
               if (p := frozen / name).is_file())
manifest = []
with zipfile.ZipFile(destination / "source.zip", "w", zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(set(sources)):
        data = path.read_bytes()
        relative = path.relative_to(frozen).as_posix()
        archive.writestr(relative, data)
        manifest.append({"path": relative, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
(destination / "source-sha256.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
binaries = [{"path": p.relative_to(destination).as_posix(), "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
            for p in sorted((destination / "runtime").rglob("*")) if p.is_file()]
(destination / "runtime-sha256.json").write_text(json.dumps(binaries, indent=2), encoding="utf-8")
protocol = {
    "version": "initial-loss-round2-paired-1", "seeds": list(range(42, 47)),
    "arms": ["legacy-final", "joint-final"], "cases": ["competition-8-F8-full25", "original-half21"],
    "excluded": {"competition-8-F4-full25": "Preserve the original fixed-aperture specification-conflict record; do not relax or rerun."},
    "execution": "Serial fresh process per case, Normal process priority, one computation thread, headless, common frozen Core and runtime. No DeepSeek. No GUI. No commit/push.",
    "input": "Original frozen task plans and acquisition costs from initial-loss-compensation/control/seed42-46.",
    "unifiedBudget": {"functions": 600, "gradients": 600, "rays": 10000000, "seconds": 300},
    "computeScope": "Explicit increased-resource experiment, not an original 2M-ray/110s budget claim.",
    "policies": {"shared": "One retained start; lifetime400functions; evolution40updates/1Mrays/60s; competition40updates/5Mrays, F8 stage60s and half21 stage120s in both arms; screen50krays; one finalist. Global wall deadline300s includes acquisition; cancellation/finalization overrun is reported, not hidden.",
                 "F8": "Parent restoration with original profile permission available to both arms; candidate explicitly releases A4 on editable standard faces; same curvature/axial/coefficient bounds. Envelope preconditioning disabled after invalid-ray development probes.",
                 "half21": "Only three original deletion positions; qualified parent immutable; no profile operation.",
                 "qualification": "Original TopologyValidator; original 50/150um, distortion, physical apertures, field union, spectra, throughput, independent rim, geometry and manufacturing tolerances."},
    "interpretation": "Compare whole explicit strategy bundles, not an isolated linear solver. A new qualified design requires independent original-spec validation. Preserve all incomplete and failed runs."
}
(destination / "experiment-protocol.json").write_text(json.dumps(protocol, indent=2, ensure_ascii=False), encoding="utf-8")
print(json.dumps({"sources": len(manifest), "binaries": len(binaries), "comparison": str(destination)}))
