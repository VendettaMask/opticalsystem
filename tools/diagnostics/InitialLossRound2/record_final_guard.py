"""Record the post-freeze invalid-option guard without rewriting experiment sources."""
import difflib
import hashlib
import json
from pathlib import Path
import sys
import zipfile

repo, comparison = (Path(value).resolve() for value in sys.argv[1:])
target = comparison / "post-freeze-permission-guard.json"
if target.exists():
    raise SystemExit("Guard audit exists.")
paths = ["labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/StructureEvolutionSearch.cs",
         "labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/StructureEvolutionSearch.Proposals.cs"]
records = []
patch = []
with zipfile.ZipFile(comparison / "source.zip") as frozen:
    for relative in paths:
        original = frozen.read(relative)
        current = (repo / relative).read_bytes()
        records.append({"path": relative, "frozenSha256": hashlib.sha256(original).hexdigest(),
                        "currentSha256": hashlib.sha256(current).hexdigest()})
        patch += difflib.unified_diff(original.decode("utf-8").splitlines(True), current.decode("utf-8").splitlines(True),
                                     fromfile="frozen/" + relative, tofile="current/" + relative)
        saved_path = comparison / "post-freeze-guard-source" / relative
        saved_path.parent.mkdir(parents=True, exist_ok=True)
        saved_path.write_bytes(current)
inputs = []
for arm in ("legacy-final", "joint-final"):
    for seed in range(42, 47):
        for case in ("competition-8-F8-full25", "original-half21"):
            path = comparison / arm / f"seed{seed}" / f"{case}.input.json"
            data = json.loads(path.read_text(encoding="utf-8-sig"))
            enabled = data["options"]["evolution"].get("transmissionProfileVersion") is not None
            reduction_only = data["plan"]["options"].get("qualifiedReductionOnly") is True
            assert not (enabled and reduction_only), "This input would change execution under the new guard."
            inputs.append({"path": path.relative_to(comparison).as_posix(), "profileRelease": enabled,
                           "qualifiedReductionOnly": reduction_only, "newGuardChangesExecution": False})
(comparison / "post-freeze-permission-guard.patch").write_text("".join(patch), encoding="utf-8")
target.write_text(json.dumps({"scope": "Reject contradictory profile release on reduction-only plans; no change on any of the 20 valid frozen inputs.",
                              "files": records, "inputs": inputs}, indent=2), encoding="utf-8")
print(f"Guard cannot change any of {len(inputs)} frozen executions.")
