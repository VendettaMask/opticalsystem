"""Record scoped source/output identity and verify bundled-font provenance/copy receipts."""
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
from xml.etree import ElementTree as ET

evidence = Path(__file__).resolve().parent
repo = evidence.parents[3]
app = repo / "src/OpticalSystemDesign.App"
fonts = app / "Assets/Fonts/Typography"
build = app / "bin/Release/net10.0"
tests = repo / "tests/OpticalSystemDesign.Tests"
test_build = tests / "bin/Release/net10.0"

def digest(path):
    data = path.read_bytes()
    return {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}

manifest = json.loads((fonts / "manifest.json").read_text(encoding="utf-8-sig"))
assets = {}
for name, entry in manifest["files"].items():
    path = fonts / name
    actual = digest(path)
    assert actual["bytes"] == entry["bytes"] and actual["sha256"] == entry["sha256"], name
    data = path.read_bytes()
    blob = hashlib.sha1(f"blob {len(data)}\0".encode() + data).hexdigest()
    assert blob == entry["gitBlobSha1"], name
    assets[name] = dict(actual, computed_git_blob=blob)
assert len(assets) == 10
licenses = {}
for path in list(fonts.glob("*-OFL.txt")) + [app / "Assets/Fonts/OFL-LICENSE.txt", app / "Assets/Fonts/FUSION-PIXEL-FONT-OFL.txt"]:
    relative = path.relative_to(app)
    assert digest(path) == digest(build / relative), str(relative)
    licenses[str(relative)] = digest(path)
assert len(licenses) == 7

source_names = ["App.cs", "OpticalSystemDesign.App.csproj", "Services/ProductFonts.cs", "Services/ProductFonts.Registry.cs",
    "Services/DisplayTypography.cs", "Services/DocumentTypography.cs", "Services/DesignReportPdfWriter.cs",
    "Services/DesignReportPdfWriter.Paper.cs", "Services/DesignReportHtmlWriter.cs", "Services/DesignReportHtmlWriter.Paper.cs",
    "Services/DesignReportWordWriter.cs", "Services/DesignReportWordWriter.Fonts.cs", "Services/DesignReportWordWriter.Math.cs",
    "Panels/UserGuidePdfExporter.cs", "Panels/LensEditorPanel.cs", "Theming/ThemeRegistry.cs", "Theming/IsekaiThemeStyles.cs",
    "Manufacturing/Rendering/OpticalDrawingRenderer.Element.cs", "Manufacturing/Rendering/OpticalDrawingRenderer.System.cs",
    "Manufacturing/Rendering/OpticalDrawingRenderer.Text.cs", "Manufacturing/Rendering/OpticalDrawingRendererCore.State.cs",
    "Assets/Reports/EngineeringReport.html", "Assets/Fonts/Typography/manifest.json"]
sources = {str((app / name).relative_to(repo)): digest(app / name) for name in source_names}
for name in ("UnifiedFontManagementTests.cs", "IsekaiTypographyPolicyTests.cs", "MarkdownReportImageTests.cs", "PdfPaperReportTests.cs"):
    sources[str((tests / name).relative_to(repo))] = digest(tests / name)

assemblies = {str(path.relative_to(repo)): digest(path) for path in (build / "OpticalSystemDesign.App.dll",
    test_build / "OpticalSystemDesign.App.dll", test_build / "OpticalSystemDesign.Tests.dll")}
assert digest(build / "OpticalSystemDesign.App.dll") == digest(test_build / "OpticalSystemDesign.App.dll")

def counts(name):
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = ET.parse(evidence / name).find("t:ResultSummary/t:Counters", ns).attrib
    return {k: int(counters[k]) for k in ("total", "passed", "failed", "notExecuted")}

checks = {name: counts(name) for name in ("unified-font-accepted.trx", "unified-popup-accepted.trx")}
assert checks["unified-font-accepted.trx"]["passed"] == 112
assert checks["unified-popup-accepted.trx"]["passed"] == 1
assert all(c["failed"] == 0 and c["notExecuted"] == 0 for c in checks.values())
receipt = {"captured_utc": datetime.now(timezone.utc).isoformat(), "scope": "Font management and portable-output regression; no full product certification",
    "passed_distinct_cases": 113, "runs": checks,
    "accepted_112_test_assembly_sha256_before_extra_popup_test": "12afd34ac49a53805ad35db79c5a1dff825801f1be04370f42b1aefa6eeb756a",
    "extra_popup_test_assembly": str((test_build / "OpticalSystemDesign.Tests.dll").relative_to(repo)),
    "scoped_sources": sources, "manifest_assets": assets, "output_licenses": licenses, "assemblies": assemblies,
    "artifacts": {str(path.relative_to(evidence)): digest(path) for path in sorted((evidence / "artifacts").iterdir()) if path.is_file()},
    "pdf_audit": digest(evidence / "pdf-font-audit.json")}
(evidence / "tested-identities.json").write_text(json.dumps(receipt, indent=2), encoding="utf-8")
print(f"Verified {len(assets)} font manifest entries, {len(licenses)} output licenses, 113 distinct passed cases; wrote tested-identities.json")
