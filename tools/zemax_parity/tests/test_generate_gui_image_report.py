import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).parents[1] / "generate_gui_image_report.py"
SPEC = importlib.util.spec_from_file_location("generate_gui_image_report", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class GuiImageReportTests(unittest.TestCase):
    def test_report_uses_actual_gui_capture_and_marks_missing_reference(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            capture_dir = root / "report" / "images" / "gui-current"
            capture_dir.mkdir(parents=True)
            (capture_dir / "001-single-ray-trace.png").write_bytes(b"png")
            manifest = capture_dir / "capture-manifest.json"
            manifest.write_text(json.dumps({
                "runs": [
                    {
                        "index": 1,
                        "analysis": "单光线追迹",
                        "canonicalAnalysis": "Single Ray Trace",
                        "status": "captured",
                        "image": "001-single-ray-trace.png",
                    },
                    {
                        "index": 38,
                        "analysis": "入射角-像高（扫描瞳孔）",
                        "canonicalAnalysis": "Angle vs Image Height - Through Pupil",
                        "status": "analysis-error",
                        "image": None,
                    },
                ]
            }), encoding="utf-8")
            zemax_dir = root / "baseline" / "analyses" / "009-raytrace"
            zemax_dir.mkdir(parents=True)
            (zemax_dir / "screenshot.png").write_bytes(b"png")

            report = MODULE.build_report(manifest, root / "baseline", root / "report")
            rendered = MODULE.render_html(report)

            self.assertEqual(2, report["summary"]["total"])
            self.assertEqual(1, report["summary"]["captured"])
            self.assertEqual(1, report["summary"]["noEquivalent"])
            self.assertIn("images/gui-current/001-single-ray-trace.png", rendered)
            self.assertNotIn("images/current/", rendered)
            self.assertEqual(2, rendered.count('<section class="card">'))


if __name__ == "__main__":
    unittest.main()
