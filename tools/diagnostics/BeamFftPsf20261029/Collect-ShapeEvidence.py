"""Seal the completed shape-repair tests, identities and diagnosis, without native arrays."""
from pathlib import Path
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / "artifacts/validation/beam-fft-shape-20261029"
OUT = ROOT / "validation/rechecks/2026-10-29/beam-fft-psf-shape"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(name):
    return json.loads((RUN / name).read_text(encoding="utf-8-sig"))


def tests(name):
    tree = ET.parse(RUN / name)
    return tree.find("t:ResultSummary/t:Counters", NS).attrib, tree.findall("t:Results/t:UnitTestResult", NS)


def beam_results(name):
    found = {}
    for row in tests(name)[1]:
        for line in row.findtext("t:Output/t:StdOut", "", NS).splitlines():
            if line.strip().startswith("{"):
                value = json.loads(line)
                if "actualPeak" in value:
                    found[value["id"]] = value
    if len(found) != 2:
        raise ValueError("Expected two beam comparisons")
    return found


prior_path = ROOT / "validation/rechecks/2026-10-29/beam-fft-psf/verification.json"
prior = json.loads(prior_path.read_text(encoding="utf-8"))
manifest_path = ROOT / "validation/zemax/2026-r1/afocal-psf-contract-2026-10-28/manifest.json"
assert sha(manifest_path) == prior["nativeManifestSha256"]
policy_path = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"
policy = (ROOT / policy_path).read_bytes().replace(b"\r\n", b"\n")
assert policy == subprocess.check_output(["git", "show", "HEAD:" + policy_path], cwd=ROOT).replace(b"\r\n", b"\n")
assert hashlib.sha256(policy).hexdigest() == prior["comparisonPolicyLfSha256"]
source_path = ROOT / "validation/zemax/2026-r1/afocal-repair-2026-10-24/captures/sources/Afocal/Beam Expander.ZMX"
assert sha(source_path) == prior["beamSourceSha256"]

before = beam_results("shape-before.trx")
after = beam_results("shape-targeted.trx")
cases = []
for key, actual in after.items():
    assert actual["violations"] == 0 and actual["samples"] == 16384 and actual["metric"]["Coverage"] == 1
    original = next(row for row in prior["cases"] if row["id"] == key)
    cases.append({"id": key, "requestFingerprint": original["requestFingerprint"],
                  "request": original["request"], "before": before[key], "after": actual,
                  "peakAbsoluteDifference": abs(actual["actualPeak"] - actual["nativePeak"])})

core_paths = ["tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
              "tools/diagnostics/BeamFftPsf20261029/bin/Release/net10.0/OpticalSystemDesign.Core.dll"]
core = [{"path": path, "sha256": sha(ROOT / path)} for path in core_paths]
assert len({row["sha256"] for row in core}) == 1
identities = set()
final = []
for name in ["shape-targeted.trx", "shape-protection.trx"]:
    counters, rows = tests(name)
    assert all(row.attrib["outcome"] == "Passed" for row in rows)
    identities.update(row.attrib["testName"] for row in rows)
    final.append({"file": name, "counters": counters, "sha256": sha(RUN / name)})
frozen = read("frozen-integrity.json")
assert frozen["unchanged"] and frozen["matched"] == 2668 and frozen["workingTree"]["unchanged"]

OUT.mkdir(parents=True, exist_ok=True)
members = list(sorted(RUN.glob("*.trx"))) + [RUN / "decomposition.json", RUN / "frozen-integrity.json"]
with zipfile.ZipFile(OUT / "evidence.zip", "w", zipfile.ZIP_DEFLATED) as zip:
    for path in members:
        zip.write(path, path.name)
sources = ["src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs",
           "src/OpticalSystemDesign.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs",
           "tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalPsfContractTests.cs",
           "tests/OpticalSystemDesign.ZemaxComparison.Tests/BeamPsfShapeDiagnostics.cs",
           "tests/OpticalSystemDesign.ZemaxComparison.Tests/FftPsfNormalizationTests.cs",
           "tests/OpticalSystemDesign.ZemaxComparison.Tests/FftPupilAmplitudeInterpolationTests.cs"]
record = {"date": "2026-10-29", "baseCommit": prior["baseCommit"],
          "scope": "Remaining Beam Expander FFT PSF shape residual on the exact original automatic/fixed grids; no prescription, grid, phase, accepted-node or tolerance changes.",
          "priorPeakStageVerificationSha256": sha(prior_path), "coreBinaries": core,
          "sourceIdentities": [{"path": path, "sha256": sha(ROOT / path)} for path in sources],
          "cases": cases, "finalTests": final, "distinctPassingTests": len(identities),
          "comparisonPolicyLfSha256": hashlib.sha256(policy).hexdigest(),
          "policyUnchanged": True, "prescriptionUnchanged": True,
          "beamSourceSha256": sha(source_path), "nativeManifestSha256": sha(manifest_path),
          "nativeArchive": prior["nativeArchive"], "nativeArchiveSha256": prior["nativeArchiveSha256"],
          "decomposition": read("decomposition.json"), "frozenIntegrity": frozen,
          "implementation": "Explicit scalar native FFT amplitude interpolation in the shared Core; mean field amplitudes on eligible even interior nodes including chief, preserving masks and phase. High-level compatible scalar PSF selects it; low-level defaults and polarized/energy/MTF paths retain their contracts.",
          "limitations": ["Two selected captured PSF requests and one independent frozen 32/64 complex control; not arbitrary optics or absolute radiometry.",
                          "No complete 150-request matrix, 95-file corpus, new native capture, GUI/CUDA test, installation or release.",
                          "Shared workspace has unrelated ongoing changes; optical tests certify only their stated scope."],
          "evidenceArchiveSha256": sha(OUT / "evidence.zip"),
          "evidenceMembers": [{"file": p.name, "bytes": p.stat().st_size, "sha256": sha(p)} for p in members]}
app = RUN / "app-build.json"
if app.exists():
    record["appBuild"] = read("app-build.json")
    assert record["appBuild"]["coreSha256"] == core[0]["sha256"]
documentation = RUN / "documentation.json"
if documentation.exists():
    doc = read("documentation.json")
    record["documentation"] = {key: doc[key] for key in ["Documents", "BrokenLinks", "MarkdownFenceIssues", "LocalOnlyLinks"]}
(OUT / "verification.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"distinctPassingTests": len(identities),
                  "maximumAbsoluteErrors": [row["after"]["maximumAbsolute"] for row in cases],
                  "pointwiseViolations": [row["after"]["violations"] for row in cases]}))
