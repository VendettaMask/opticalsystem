"""Create a metadata-only review ledger. Never rewrites evidence, notices or product sources."""
import hashlib
import json
import pathlib
import re
import subprocess
import zipfile

root = pathlib.Path.cwd()
raw = root / "artifacts/validation/documentation-maintenance-20261102"
out = root / "validation/rechecks/2026-11-02/documentation-maintenance"
read = lambda p: json.loads(p.read_text(encoding="utf-8-sig"))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
before, after = read(raw / "before.json"), read(raw / "after.json")
assert not after["BrokenLinks"] and not after["MarkdownFenceIssues"] and not after["GuideRepeatedParagraphs"] and not after["LifecycleIssues"]
tests = read(raw / "tests-final.json"); assert tests["Passed"] == tests["Cases"] and tests["Failed"] == 0
old = {e["Path"]: e for e in before["Inventory"]}; new = {e["Path"]: e for e in after["Inventory"]}
owned = ["README.md", "README_EN.md", "docs/ARCHITECTURE.md", "docs/ARCHITECTURE_CONVERGENCE_PLAN.md",
         "docs/AUTOLENS_OFFICIAL_EXAMPLES.md", "docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md", "docs/BUILD_AND_RELEASE.md",
         "docs/CODE_READING_MAP.md", "docs/COMPUTATION_GUIDE.md", "docs/INITIAL_STRUCTURE_LAB_PLAN.md",
         "docs/INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md", "docs/LENS_LIBRARY.md", "docs/MAC_OTA.md",
         "docs/OPEN_ISSUES_2026-10-08.md", "docs/OPTIMIZATION_ALGORITHMS.md", "docs/PARITY_MATRIX.md", "docs/README.md",
         "docs/UI_DESIGN_SPEC.md", "docs/MONOCHROME_SLANTED_EDGE_SFR.md", "docs/ANSYS_SCENE_SFR_IMPLEMENTATION_PLAN_2026-11-02.md",
         "docs/CURRENT_STATUS.md", "docs/SCENE_SLANTED_EDGE_SFR.md"]
created = ["docs/DOCUMENTATION_MAINTENANCE.md", "docs/history/README.md", "validation/rechecks/2026-11-02/documentation-maintenance/README.md"]
for p in owned + created:
    assert (root / p).is_file() and sha(root / p) == new[p]["Sha256"], "Owned document changed after final audit: " + p
catalog = read(root / "docs/documentation-lifecycle.json")
retired = {e["Path"]: e for e in catalog["RetiredDocuments"]}
assert all(p not in new and not (root / p).exists() and (root / e["Replacement"]).is_file() for p, e in retired.items())
rows = []
for name in sorted(old.keys() | new.keys()):
    a, b = old.get(name), new.get(name)
    if name in retired: disposition = "RetiredAfterReplacementReview"
    elif name in owned and a and b and a["Sha256"] != b["Sha256"]: disposition = "UpdatedExistingDocument"
    elif name in owned and not a: disposition = "ReviewedIncomingDocument"
    elif name in created: disposition = "NewIndexRuleOrMaintenanceReceipt"
    elif not a: disposition = "IncomingDuringReview"
    elif not b: disposition = "RemovedOutsideOwnedScope"
    elif a["Sha256"] != b["Sha256"]: disposition = "ChangedOutsideOwnedScope"
    else: disposition = "Retained"
    rows.append({"path": name, "before": a, "after": b, "disposition": disposition,
                 "reviewDepth": "Content/source cross-check and links" if name in owned else "Inventory/lifecycle/local references; binary/evidence bodies not re-certified"})
assert len([r for r in rows if r["disposition"] == "UpdatedExistingDocument"]) == 21
# Confirm the unchanged computation coverage against the actual application descriptor map.
source = (root / "src/OpticalSystemDesign.Application/Services/WorkbenchAnalysisCatalog.cs").read_text(encoding="utf-8-sig")
names = set(re.findall(r'\["([^"]+)"\]\s*=', source.split("DisplayNamesByKey =", 1)[1].split("};", 1)[0]))
coverage = (root / "docs/COMPUTATION_COVERAGE.md").read_text(encoding="utf-8-sig")
documented = set(re.findall(r"^\|.*?\(([^()]+)\)\s*\|", coverage, re.M)); assert names == documented
preserved = [r for r in rows if r["before"] and r["before"]["Category"] in ("Evidence", "ThirdParty")]
drift = [r["path"] for r in preserved if not r["after"] or r["before"]["Sha256"] != r["after"]["Sha256"]]
assert not any(p in owned + created for p in drift)
# A metadata receipt replaces neither a numerical reference nor an archive of original document contents.
ledger = {"date": "2026-11-02", "beforeDocuments": before["Documents"], "afterDocuments": after["Documents"],
          "documents": rows, "retired": list(retired.values()), "existingEvidenceOrNoticeDriftOutsideOwnedScope": drift,
          "scope": "Complete inventory/fingerprints and maintained-guide local links; targeted semantic review of current claims. No blanket historical/binary content certification."}
out.mkdir(parents=True, exist_ok=True)
(out / "review-ledger.json").write_text(json.dumps(ledger, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
scope = owned + created + list(retired) + ["docs/documentation-lifecycle.json", "scripts/verify-documentation.ps1", "scripts/tests/verify-documentation.tests.ps1"]
check = subprocess.run(["git", "diff", "--check", "--", *scope], capture_output=True, text=True)
(raw / "scoped-diff-check.log").write_text(check.stdout + check.stderr, encoding="utf-8"); assert check.returncode == 0
with zipfile.ZipFile(out / "evidence.zip", "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for p in sorted(raw.rglob("*")):
        if p.is_file() and p.name != "receipt-check.log": z.write(p, p.relative_to(root).as_posix())
sources = scope + ["tools/diagnostics/DocumentationMaintenance20261102/review.py", "tools/diagnostics/DocumentationMaintenance20261102/reorganize_index.py", "tools/diagnostics/DocumentationMaintenance20261102/finalize.py"]
receipt = {"date": "2026-11-02", "sourceCommit": after["SourceCommit"], "includesPreexistingUncommittedWork": True,
           "beforeDocuments": before["Documents"], "afterDocuments": after["Documents"], "categories": after["Categories"],
           "updatedExistingDocuments": [r["path"] for r in rows if r["disposition"] == "UpdatedExistingDocument"],
           "reviewedIncomingDocuments": [r["path"] for r in rows if r["disposition"] == "ReviewedIncomingDocument"],
           "removed": list(retired.values()), "newIndicesRulesAndReceipt": created,
           "checks": {"brokenLinks": 0, "codeFenceIssues": 0, "lifecycleIssues": 0, "guideRepeatedParagraphGroups": 0,
                      "toolCasesPassed": tests["Passed"], "toolCasesFailed": tests["Failed"], "analysisCoverageEntries": len(names), "scopedWhitespace": "passed"},
           "retainedEvidenceAndNoticeEntriesAtStart": len(preserved), "changedOutsideOwnedScope": drift,
           "sourceSha256": {p: sha(root / p) for p in sources if (root / p).is_file()},
           "reviewLedgerSha256": sha(out / "review-ledger.json"), "evidenceSha256": sha(out / "evidence.zip"),
           "limitations": ["Remote URLs not fetched; PDF/Office bodies inventoried and fingerprinted", "Historical bodies not all reinterpreted or rewritten; unique inputs/failures and consumer relationships preserved", "No optical recalc, product code change, native desktop, build/release or push by this task", "Other tasks were editing scenario SFR/coating code and documentation; their changes retained. Global whitespace log is distinct from owned-document checks."]}
(out / "verification.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
for p, digest in receipt["sourceSha256"].items(): assert sha(root / p) == digest, p
with zipfile.ZipFile(out / "evidence.zip") as z: assert z.testzip() is None
print(json.dumps({"beforeDocuments": receipt["beforeDocuments"], "afterDocuments": receipt["afterDocuments"],
                  "updated": len(receipt["updatedExistingDocuments"]), "retired": len(retired), "checks": receipt["checks"],
                  "existingEvidenceOrNoticeChangesOutsideOwnedScope": len(drift)}))
