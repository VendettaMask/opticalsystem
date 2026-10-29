"""Summarize completed tests without running optics or copying native reference arrays."""
from pathlib import Path
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / "artifacts/validation/beam-fft-psf-20261029"
OUT = ROOT / "validation/rechecks/2026-10-29/beam-fft-psf"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def results(name):
    tree = ET.parse(RUN / name)
    counters = tree.find("t:ResultSummary/t:Counters", NS).attrib
    rows = tree.findall("t:Results/t:UnitTestResult", NS)
    return counters, rows


def beam_rows(name):
    _, rows = results(name)
    found = {}
    for row in rows:
        text = row.findtext("t:Output/t:StdOut", "", NS)
        for line in text.splitlines():
            if line.strip().startswith("{"):
                data = json.loads(line)
                if "actualPeak" in data:
                    found[data["id"]] = data
    if len(found) != 2:
        raise ValueError(f"Expected two completed beam comparisons in {name}")
    return found


OUT.mkdir(parents=True, exist_ok=True)
policy_path = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"
original_policy = subprocess.check_output(["git", "show", "HEAD:" + policy_path], cwd=ROOT)
working_policy = (ROOT / policy_path).read_bytes()
if working_policy.replace(b"\r\n", b"\n") != original_policy.replace(b"\r\n", b"\n"):
    raise ValueError("The comparison tolerance file changed")

before = beam_rows("before-optical.trx")
after = beam_rows("targeted.trx")
final_names = ["targeted.trx", "spectral-final.trx", "protection-focused.trx"]
identities = set()
tests = []
for name in final_names:
    counters, rows = results(name)
    if any(row.attrib["outcome"] != "Passed" for row in rows):
        raise ValueError(f"Incomplete or failed final test run: {name}")
    identities.update(row.attrib["testName"] for row in rows)
    tests.append({"file": name, "counters": counters, "sha256": digest(RUN / name)})

manifest_path = ROOT / "validation/zemax/2026-r1/afocal-psf-contract-2026-10-28/manifest.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
prior = json.loads((ROOT / "validation/rechecks/2026-10-28/precision-check/afocal-comparison.json")
                   .read_text(encoding="utf-8-sig"))
cases = []
for key, current in after.items():
    row = next(r for r in manifest["cases"] if r["id"] == key)
    previous = next(r for r in prior["cases"] if r["id"] == key)
    cases.append({"id": key, "requestFingerprint": previous["requestFingerprint"],
                  "request": row["request"], "before": before[key], "after": current,
                  "peakAbsoluteDifference": abs(current["actualPeak"] - current["nativePeak"]),
                  "peakRelativeDifference": abs(current["actualPeak"] / current["nativePeak"] - 1),
                  "remainingPointwiseViolations": current["violations"]})

core_paths = [
    "tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll",
    "tools/diagnostics/BeamFftPsf20261029/bin/Release/net10.0/OpticalSystemDesign.Core.dll"]
core_identities = [{"path": path, "sha256": digest(ROOT / path)} for path in core_paths]
if len({item["sha256"] for item in core_identities}) != 1:
    raise ValueError("Final test projects do not retain the same Core binary")

evidence_files = sorted(RUN.glob("*.trx")) + [RUN / "frozen-integrity.json"]
with zipfile.ZipFile(OUT / "evidence.zip", "w", zipfile.ZIP_DEFLATED) as archive:
    for path in evidence_files:
        archive.write(path, path.name)

record = {
    "date": "2026-10-29", "baseCommit": subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
    "scope": "Two existing Beam Expander FFT PSF grids; unchanged original requests, axes, reference bytes and tolerances. No native recapture.",
    "officialSources": [
        "https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/FFT_PSF.html",
        "https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Apodization_Type.html"],
    "comparisonPolicyCheckoutSha256": digest(ROOT / policy_path),
    "comparisonPolicyLfSha256": hashlib.sha256(working_policy.replace(b"\r\n", b"\n")).hexdigest(),
    "policyContentMatchesHEAD": True,
    "nativeManifestSha256": digest(manifest_path),
    "nativeArchive": manifest["sourceArchive"], "nativeArchiveSha256": manifest["sourceArchiveSha256"],
    "beamSourceSha256": digest(ROOT / "validation/zemax/2026-r1/afocal-repair-2026-10-24/captures/sources/Afocal/Beam Expander.ZMX"),
    "coreBinaries": core_identities, "cases": cases,
    "sourceIdentities": [{"path": p, "sha256": digest(ROOT / p)} for p in [
        "src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs",
        "src/OpticalSystemDesign.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs",
        "tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalPsfContractTests.cs",
        "tests/OpticalSystemDesign.ZemaxComparison.Tests/FftPsfNormalizationTests.cs"]],
    "finalTests": tests, "distinctPassingTestNames": len(identities),
    "frozenIntegrity": json.loads((RUN / "frozen-integrity.json").read_text(encoding="utf-8-sig")),
    "earlierFailures": {
        "before.trx": "Three optical Gaussian failures and two missing materialized source fixtures; fixture materialization was rerun before before-optical.trx.",
        "before-optical.trx": "Five real optical failures; uniform pupil control passed.",
        "after.trx": "Peak defect fixed; proposed stronger all-pixel gate still failed at 48/140 pixels. These residuals remain open; final peak-specific test applies the unchanged absolute+relative tolerance at the peak and audits all pixels.",
        "spectral.trx": "An additional direct profile/raster point equality assertion failed because the existing cross-section has its own interpolation. Replaced with the intended independent ideal-Strehl and no-double-normalization checks; production interpolation unchanged.",
        "defaultAppBuild": "Main test project build stopped before tests: concurrent report edits referenced missing DesignReportWordWriter/DesignReportPdfWriter and ReportExportResult.MarkdownPath. Original five optical test files were compiled separately, without modifying their assertions."},
    "limitations": ["Smaller off-center pointwise residuals remain; no full-grid pointwise closure or absolute radiometry claim.",
                    "No complete 150-request matrix, 95-file corpus, native GUI, CUDA, installer or release validation.",
                    "Shared workspace includes unrelated ongoing report/Delano changes; this evidence certifies only the stated FFT scope."],
    "evidenceArchiveSha256": digest(OUT / "evidence.zip"),
    "evidenceMembers": [{"file": p.name, "sha256": digest(p), "bytes": p.stat().st_size} for p in evidence_files]
}
(OUT / "verification.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"distinctPassingTests": len(identities), "peakDifferences": [r["peakAbsoluteDifference"] for r in cases],
                  "policyUnchanged": True, "frozenFilesUnchanged": record["frozenIntegrity"]["unchanged"]}))
