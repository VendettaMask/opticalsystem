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


class PhysicalCoordinateComparisonTests(unittest.TestCase):
    @staticmethod
    def curve(xs=(0, 1, 2), ys=(0, 1, 2)):
        return {"seriesList": [{"name": "field", "xQuantity": "defocus", "xUnit": "millimeter",
                               "yQuantity": "modulation", "yUnit": "dimensionless",
                               "points": [{"x": x, "y": y} for x, y in zip(xs, ys)]}]}

    @staticmethod
    def reference(xs=(0, 1, 2), ys=(0, 1, 2)):
        return {"dataSeries": [{"x": list(xs), "y": [[y] for y in ys], "seriesLabels": ["field"]}]}

    @staticmethod
    def mapping():
        return {"axisContract": ["defocus", "millimeter", "modulation", "dimensionless"],
                "details": [{"currentSeries": "field", "zemaxSeries": "field", "valueAxis": "y"}]}

    def test_hundredfold_coordinate_error_cannot_pass(self):
        details, _ = MODULE.compare_curves("synthetic", self.curve((0, 100, 200)), self.reference(), self.mapping())
        self.assertEqual("coordinate-mismatch", MODULE.classification(details)[0])

    def test_nonuniform_samples_interpolate_on_physical_coordinates(self):
        details, _ = MODULE.compare_curves("synthetic", self.curve((0, .2, 2), (0, .2, 2)), self.reference(), self.mapping())
        self.assertEqual("high-agreement", MODULE.classification(details)[0])
        self.assertLess(details[0]["valueNrmse"], 1e-12)

    def test_missing_named_series_does_not_use_another_field(self):
        current = self.curve()
        current["seriesList"][0]["name"] = "wrong field"
        details, _ = MODULE.compare_curves("synthetic", current, self.reference(), self.mapping())
        self.assertEqual("not-compared", MODULE.classification(details)[0])
        self.assertIn("missing", details[0]["error"])

    def test_insufficient_or_nonfinite_values_fail(self):
        for x, y in [([0], [1]), ([0, 1], [0, float("nan")]), ([0, 0, 1], [1, 2, 3])]:
            details, _ = MODULE.compare_curves("synthetic", self.curve(x, y), self.reference(), self.mapping())
            self.assertEqual("not-compared", MODULE.classification(details)[0])

    def test_unit_conversion_uses_typed_metadata(self):
        current = self.curve((0, 1000, 2000))
        current["seriesList"][0]["xUnit"] = "micrometer"
        details, _ = MODULE.compare_curves("synthetic", current, self.reference(), self.mapping())
        self.assertEqual("high-agreement", MODULE.classification(details)[0])
        current["seriesList"][0]["xUnit"] = "degree"
        details, _ = MODULE.compare_curves("synthetic", current, self.reference(), self.mapping())
        self.assertEqual("not-compared", MODULE.classification(details)[0])

    def test_decreasing_storage_order_is_not_a_physical_mirror(self):
        details, _ = MODULE.compare_curves("synthetic", self.curve((2, 1, 0), (2, 1, 0)), self.reference(), self.mapping())
        self.assertEqual("high-agreement", MODULE.classification(details)[0])

    def test_mtf_field_mapping_uses_twenty_cycles_group(self):
        details = MODULE.stable_curve_details(("Fourier MTF vs Field", "FftMtfvsField"), [])
        self.assertEqual([1, 1], [item["zemaxGroup"] for item in details])
        candidates = [{"name": "子午", "group": 0}, {"name": "子午", "group": 1}]
        self.assertEqual(1, MODULE.select_named(candidates, set(), "子午", group=1)[0])

    @staticmethod
    def grid(mirror=False, coordinate_scale=1):
        values = np.asarray([[0., 1, 4], [2, 0, 8], [1, 2, 3]])
        current = np.fliplr(values) if mirror else values
        view = {"seriesList": [{"xQuantity": "imageHeight", "yQuantity": "imageHeight",
                                "xUnit": "millimeter", "yUnit": "millimeter", "valueQuantity": "irradiance",
                                "valueUnit": "dimensionless", "points": [
            {"x": x * coordinate_scale, "y": y * coordinate_scale, "value": float(current[y, x])}
            for y in range(3) for x in range(3)]}]}
        reference = {"dataGrids": [{"minX": 0, "minY": 0, "dx": 1, "dy": 1, "values": values.tolist()}]}
        mapping = {"coordinateUnit": "millimeter", "details": [{"label": "grid"}]}
        return view, reference, mapping

    def test_asymmetric_mirrored_grid_cannot_choose_best_flip(self):
        details, _ = MODULE.compare_grids(*self.grid(mirror=True))
        self.assertEqual("identity", details[0]["orientation"])
        self.assertEqual("different", MODULE.classification(details)[0])

    def test_grid_coordinate_scale_error_is_rejected(self):
        details, _ = MODULE.compare_grids(*self.grid(coordinate_scale=100))
        self.assertEqual("coordinate-mismatch", MODULE.classification(details)[0])

    def test_grid_coverage_measures_area_without_extrapolating_edge_pixels(self):
        details, plots = MODULE.compare_grids(*self.grid(coordinate_scale=.999))
        self.assertGreater(details[0]["coordinateCoverage"], .99)
        self.assertTrue(np.isnan(plots[0][1][-1]).all())

    def test_missing_interior_grid_data_reduces_coverage(self):
        view, reference, mapping = self.grid()
        view["seriesList"][0]["points"][4]["value"] = float("nan")
        details, _ = MODULE.compare_grids(view, reference, mapping)
        self.assertEqual("coordinate-mismatch", MODULE.classification(details)[0])

    def test_missing_grid_is_not_replaced_by_the_last_grid(self):
        view, reference, mapping = self.grid()
        mapping["details"].append({"label": "required second grid"})
        details, _ = MODULE.compare_grids(view, reference, mapping)
        self.assertEqual("not-compared", MODULE.classification(details)[0])
        self.assertIn("missing", details[-1]["error"])

    def test_grid_needs_coordinate_metadata_and_declared_transform(self):
        view, reference, mapping = self.grid()
        del reference["dataGrids"][0]["dx"]
        details, _ = MODULE.compare_grids(view, reference, mapping)
        self.assertEqual("not-compared", MODULE.classification(details)[0])
        view, reference, mapping = self.grid()
        mapping["details"][0]["coordinateTransform"] = "flip-x"
        details, _ = MODULE.compare_grids(view, reference, mapping)
        self.assertEqual("not-compared", MODULE.classification(details)[0])

    def test_one_bad_series_cannot_hide_behind_the_median(self):
        details = [{"valueNrmse": 0.01}] * 20 + [{"valueNrmse": 0.8}]
        self.assertEqual("different", MODULE.classification(details)[0])


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
        coordinates = (np.arange(64) - 32) / 31
        values = coordinates[None, :] + 2 * coordinates[:, None]
        view = {"seriesList": [{"xQuantity": "pupilCoordinate", "yQuantity": "pupilCoordinate",
                                "xUnit": "dimensionless", "yUnit": "dimensionless",
                                "valueQuantity": "wavefrontError", "valueUnit": "wave",
                                "points": [{"x": float(x), "y": float(y), "value": float(x + 2*y)}
                                           for y in coordinates for x in coordinates]}]}
        reference = {"dataGrids": [{"minX": 0, "minY": 0, "dx": 1, "dy": 1,
                                    "values": values.tolist()}]}
        mapping = {"analysis": "Wavefront", "zemaxAnalysis": "WavefrontMap",
                   "details": [{"label": "wavefront"}]}
        details, plots = MODULE.compare_grids(view, reference, mapping)
        self.assertEqual("high-agreement", MODULE.classification(details)[0])
        self.assertAlmostEqual(0, plots[0][1][32, 32])
        self.assertTrue(np.isnan(plots[0][2][32, 0]))

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
