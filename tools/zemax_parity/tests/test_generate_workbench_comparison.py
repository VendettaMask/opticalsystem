import importlib.util
import tempfile
import unittest
from unittest import mock
from pathlib import Path

import numpy as np
from PIL import Image


MODULE_PATH = Path(__file__).parents[1] / "generate_workbench_comparison.py"
SPEC = importlib.util.spec_from_file_location("generate_workbench_comparison", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class NumericMappingSemanticsTests(unittest.TestCase):
    def test_centroid_sphere_fit_is_not_zemax_remove_tilt(self) -> None:
        reason = MODULE.numeric_mapping_exclusion({
            "analysis": "Centroid Sphere Wavefront",
            "zemaxAnalysis": "WavefrontMap",
        })

        self.assertIsNotNone(reason)
        self.assertIn("only removes linear X and Y tilt", reason)

    def test_best_fit_sphere_is_not_zemax_wavefront_map(self) -> None:
        reason = MODULE.numeric_mapping_exclusion({
            "analysis": "Best Fit Sphere Wavefront",
            "zemaxAnalysis": "WavefrontMap",
        })

        self.assertIsNotNone(reason)
        self.assertIn("surface-sag/manufacturing", reason)

    def test_true_wavefront_map_mapping_remains_numeric(self) -> None:
        reason = MODULE.numeric_mapping_exclusion({
            "analysis": "Wavefront",
            "zemaxAnalysis": "WavefrontMap",
        })

        self.assertIsNone(reason)

    def test_contrast_loss_uses_loss_grids_not_phase_grids(self) -> None:
        self.assertEqual(
            ((0, 3), (1, 1)),
            MODULE.GRID_PAIR_INDICES[("Contrast Loss Map", "ContrastLoss")],
        )

    def test_exclusion_reason_is_stable_for_report_regeneration(self) -> None:
        reason = MODULE.numeric_mapping_exclusion({
            "analysis": "Best Fit Sphere Wavefront",
            "zemaxAnalysis": "WavefrontMap",
            "reason": "old report wording",
        })

        self.assertEqual(
            MODULE.NON_EQUIVALENT_NUMERIC_MAPPINGS[
                ("Best Fit Sphere Wavefront", "WavefrontMap")
            ],
            reason,
        )

    def test_rms_wavefront_series_mapping_is_not_inherited_from_old_report(self) -> None:
        details = MODULE.stable_curve_details(
            ("RMS Wavefront vs Field", "RMSField"),
            [{"referenceReversed": True}],
        )

        self.assertEqual("Poly", details[0]["currentSeries"])
        self.assertEqual("多面体", details[0]["zemaxSeries"])
        self.assertFalse(any(item.get("referenceReversed") for item in details))

    def test_opd_pupil_direction_is_not_inherited_from_old_values(self) -> None:
        details = MODULE.stable_curve_details(
            ("Optical Path Difference", "OpticalPathFan"),
            [{"label": "curve 21", "referenceReversed": True}],
        )

        self.assertFalse(details[0]["referenceReversed"])

    def test_ray_aimed_pupil_aberration_uses_documented_near_zero_floor(self) -> None:
        floor = MODULE.CURVE_NRMSE_ABSOLUTE_FLOORS["Pupil Aberration"]
        value, maximum, _ = MODULE.nrmse(
            np.zeros(3),
            np.asarray([-1e-6, 0.0, 1e-6]),
            floor,
        )

        self.assertEqual(1e-4, floor)
        self.assertLess(value, 0.01)
        self.assertEqual(1e-6, maximum)

    def test_wavefront_even_grid_restores_zemax_center_index(self) -> None:
        view = {
            "rows": [{"metric": "采样", "value": "64 x 64"}],
            "seriesList": [{
                "points": [
                    {"x": 0.0, "y": 0.0, "value": 1.0},
                    {"x": -1.0, "y": 0.0, "value": 2.0},
                    {"x": 1.0, "y": 0.0, "value": 3.0},
                ]
            }],
            "plotPanes": [],
        }

        grid = MODULE.zemax_centered_wavefront_grids(view)[0]

        self.assertEqual((64, 64), grid.shape)
        self.assertEqual(1.0, grid[32, 32])
        self.assertEqual(2.0, grid[32, 1])
        self.assertEqual(3.0, grid[32, 63])
        self.assertTrue(np.isnan(grid[32, 0]))

    def test_five_field_two_direction_pages_keep_zemax_style_layout(self) -> None:
        panes = [{"series": []} for _ in range(10)]

        self.assertTrue(MODULE.is_five_field_two_direction_layout("Ray Fan", panes, 2))
        self.assertTrue(
            MODULE.is_five_field_two_direction_layout(
                "Optical Path Difference",
                panes,
                2,
            )
        )
        self.assertTrue(
            MODULE.is_five_field_two_direction_layout("Pupil Aberration", panes, 2)
        )
        self.assertEqual((3, 3), MODULE.pane_grid_shape("Ray Fan", panes, 2))
        self.assertEqual((0, 0), MODULE.five_field_position(0))
        self.assertEqual((1, 1), MODULE.five_field_position(2))
        self.assertEqual((2, 2), MODULE.five_field_position(4))

    def test_generic_plot_panes_keep_requested_columns(self) -> None:
        panes = [{"series": []}, {"series": []}]

        self.assertEqual(
            (1, 2),
            MODULE.pane_grid_shape("Field Curvature and Distortion", panes, 2),
        )

    def test_current_page_prefers_plot_panes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "page.png"
            view = {"plotPanes": [{"series": []}], "plotPaneColumns": 1}

            with mock.patch.object(
                MODULE,
                "render_current_plot_panes",
                return_value=True,
            ) as render_panes:
                MODULE.render_current_page(path, "Ray Fan", view)

        render_panes.assert_called_once()

    def test_current_page_renders_plot_panes_png(self) -> None:
        series = {
            "xAxisLabel": "P_y",
            "yAxisLabel": "W (waves)",
            "kind": "line",
            "name": "0.4400 um",
            "colorIndex": 1,
            "points": [
                {"x": -1.0, "y": -0.1},
                {"x": 0.0, "y": 0.0},
                {"x": 1.0, "y": 0.1},
            ],
        }
        view = {
            "plotPanes": [
                {
                    "title": "Field Curvature",
                    "series": [series],
                    "plotOptions": {"xMinimum": -1, "xMaximum": 1},
                },
                {
                    "title": "Distortion",
                    "series": [series],
                    "plotOptions": {"xMinimum": -1, "xMaximum": 1},
                },
            ],
            "plotPaneColumns": 2,
        }
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "page.png"

            MODULE.render_current_page(path, "Field Curvature and Distortion", view)

            with Image.open(path) as image:
                self.assertGreater(image.width, 200)
                self.assertGreater(image.height, 100)


if __name__ == "__main__":
    unittest.main()
