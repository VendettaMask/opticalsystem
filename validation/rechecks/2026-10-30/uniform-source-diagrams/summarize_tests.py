"""Keep the latest result for each scoped case, including renamed contracts."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent
renames = {
    "LayoutCaptionKeepsLogoSourceMetricsAndReflowsWithoutClipping":
        "LayoutCaptionKeepsFixedSourceMetricsAndRejectsOverflowWithoutReflow",
    "EveryPlotFamilyKeepsLongCaptionsReadableAcrossResize":
        "EveryPlotFamilyKeepsItsSourceCaptionOrExplicitlyRejectsOversizeAcrossResize",
    "UnfittedLayoutReproducesOverflowAndFittedLayoutRemovesIt":
        "ScrollHostPreservesTheOriginalSheetAndKeepsItsOffscreenFooterReachable",
}
latest = {}
runs = []
for file in ["source-initial.trx", "integration-initial.trx", "integration-regressions.trx",
             "caption-diagnostic.trx", "caption-final.trx"]:
    tree = ET.parse(root / file)
    outcomes = {}
    for item in tree.findall(".//{*}UnitTestResult"):
        name = item.attrib["testName"]
        for old, new in renames.items():
            name = name.replace(old, new)
        result = item.attrib["outcome"]
        outcomes[result] = outcomes.get(result, 0) + 1
        latest[name] = {"name": name, "outcome": result, "run": file}
    runs.append({"file": file, "outcomes": outcomes})
assert len(latest) == 183, len(latest)
assert all(item["outcome"] == "Passed" for item in latest.values())
report = {"scope": "Focused original-sheet, live scrolling/interaction, native PNG and four-format report regressions; not a full-suite run",
          "uniqueLatestCases": len(latest), "latestPassed": len(latest), "renamedContracts": renames,
          "runs": runs, "latest": sorted(latest.values(), key=lambda item: item["name"])}
(root / "test-summary.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"{len(latest)} unique scoped cases: latest results all Passed; original failures retained.")
