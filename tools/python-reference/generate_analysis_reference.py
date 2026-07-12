import argparse
import json
from pathlib import Path

import matplotlib

matplotlib.use("Agg")

import matplotlib.pyplot as plt
import numpy as np
from scipy.ndimage import zoom
from scipy.signal import fftconvolve

from optiland.analysis.distortion import Distortion
from optiland.analysis.field_curvature import FieldCurvature
from optiland.analysis.grid_distortion import GridDistortion
from optiland.analysis.encircled_energy import EncircledEnergy
from optiland.analysis.rms_vs_field import RmsSpotSizeVsField, RmsWavefrontErrorVsField
from optiland.analysis.angle_vs_height import (
    FieldIncidentAngleVsHeight,
    PupilIncidentAngleVsHeight,
)
from optiland.analysis.spot_diagram import SpotDiagram
from optiland.analysis.ray_fan import RayFan, BestFitRayFan
from optiland.analysis.through_focus_spot_diagram import ThroughFocusSpotDiagram
from optiland.analysis.through_focus_mtf import ThroughFocusMTF
from optiland.analysis.irradiance import IncoherentIrradiance
from optiland.analysis.intensity import RadiantIntensity
from optiland.analysis.pupil_aberration import PupilAberration
from optiland.analysis.y_ybar import YYbar
from optiland.wavefront.opd import OPD
from optiland.wavefront.zernike_opd import ZernikeOPD
from optiland.psf.fft import FFTPSF
from optiland.mtf.fft import FFTMTF
from optiland.mtf.geometric import GeometricMTF
from optiland.mtf.sampled import SampledMTF
from optiland.distribution import create_distribution
from optiland.wavefront.strategy import BestFitSphereStrategy
from optiland.rays import PolarizationState
from optiland.physical_apertures import RectangularAperture
from optiland.samples.objectives import CookeTriplet, TessarLens


def array(value):
    return np.asarray(value, dtype=float).tolist()


def plot_metadata(axes):
    legend = axes.get_legend()
    aspect = axes.get_aspect()
    return {
        "title": axes.get_title(),
        "x_label": axes.get_xlabel(),
        "y_label": axes.get_ylabel(),
        "legend": [] if legend is None else [text.get_text() for text in legend.get_texts()],
        "aspect": float(aspect) if isinstance(aspect, (float, int)) else str(aspect),
        "x_symmetric": bool(abs(abs(axes.get_xlim()[0]) - abs(axes.get_xlim()[1])) < 1e-12),
        "y_min": float(axes.get_ylim()[0]),
        "top_spine_visible": bool(axes.spines["top"].get_visible()),
        "right_spine_visible": bool(axes.spines["right"].get_visible()),
    }


def save_plot(figure, plot_dir, filename):
    if plot_dir is not None:
        plot_dir.mkdir(parents=True, exist_ok=True)
        figure.savefig(plot_dir / filename, dpi=140)
    plt.close(figure)


def jones_pupil_data(optic, grid_size=9):
    optic.set_polarization(PolarizationState(is_polarized=False))
    optic.surface_group.set_fresnel_coatings()
    wavelength = optic.wavelengths.primary_wavelength.value
    axis = np.linspace(-1.0, 1.0, grid_size)
    px_grid, py_grid = np.meshgrid(axis, axis)
    px = px_grid.flatten()
    py = py_grid.flatten()
    rays = optic.trace_generic(Hx=0, Hy=0, Px=px, Py=py, wavelength=wavelength)

    k = np.stack([rays.L, rays.M, rays.N], axis=1)
    k = k / np.linalg.norm(k, axis=1)[:, None]
    x_axis = np.broadcast_to(np.array([1.0, 0.0, 0.0]), k.shape)
    v = np.cross(k, x_axis)
    v = v / (np.linalg.norm(v, axis=1)[:, None] + 1e-15)
    u = np.cross(v, k)
    u = u / (np.linalg.norm(u, axis=1)[:, None] + 1e-15)

    p_x_in = rays.p[:, :, 0]
    p_y_in = rays.p[:, :, 1]
    jxx = np.sum(u * p_x_in, axis=1)
    jxy = np.sum(u * p_y_in, axis=1)
    jyx = np.sum(v * p_x_in, axis=1)
    jyy = np.sum(v * p_y_in, axis=1)
    mask = px * px + py * py <= 1.0

    def component(values, part):
        selected = np.real(values) if part == "real" else np.imag(values)
        return [float(value) if valid else None for value, valid in zip(selected, mask)]

    return {
        "field": [0, 0],
        "wavelength": float(wavelength),
        "grid_size": grid_size,
        "px": array(px),
        "py": array(py),
        "valid": mask.tolist(),
        "jxx_real": component(jxx, "real"),
        "jxx_imag": component(jxx, "imag"),
        "jxy_real": component(jxy, "real"),
        "jxy_imag": component(jxy, "imag"),
        "jyx_real": component(jyx, "real"),
        "jyx_imag": component(jyx, "imag"),
        "jyy_real": component(jyy, "real"),
        "jyy_imag": component(jyy, "imag"),
    }


def image_test_chart(width=16, height=16):
    values = np.zeros((3, height, width), dtype=float)
    patches = [
        (0.85, 0.12, 0.10), (0.12, 0.62, 0.22), (0.10, 0.30, 0.88),
        (0.95, 0.72, 0.10), (0.72, 0.16, 0.78), (0.08, 0.72, 0.78),
    ]
    for row in range(height):
        for column in range(width):
            checker = 0.08 if ((row // 4) + (column // 4)) % 2 == 0 else 0.92
            color = patches[min(len(patches) - 1, column * len(patches) // width)] if row < height // 2 else (checker,) * 3
            values[:, row, column] = color
    center_x = width // 2
    center_y = height * 3 // 4
    radius = min(width, height) / 7.0
    for row in range(height):
        for column in range(width):
            distance = np.sqrt((column - center_x) ** 2 + (row - center_y) ** 2)
            if abs(distance - radius) <= 1.2 or abs(column - center_x) <= 1 or abs(row - center_y) <= 1:
                values[:, row, column] = 1.0
    return values


def polynomial_features(x, y, degree):
    features = []
    for order in range(degree + 1):
        for x_power in range(order + 1):
            features.append((x ** x_power) * (y ** (order - x_power)))
    return np.stack(features, axis=1)


def distortion_grid(optic, wavelength, image_shape, num_grid_points=7, degree=3):
    linear = np.linspace(-1.0, 1.0, num_grid_points)
    gx, gy = np.meshgrid(linear, linear)
    gx_flat = gx.flatten()
    gy_flat = gy.flatten()
    rays = optic.trace_generic(Hx=gx_flat, Hy=gy_flat, Px=0, Py=0, wavelength=wavelength)
    center = optic.trace_generic(Hx=0, Hy=0, Px=0, Py=0, wavelength=wavelength)
    real_x = np.asarray(rays.x) - float(np.asarray(center.x)[0])
    real_y = np.asarray(rays.y) - float(np.asarray(center.y)[0])
    scale_x = max(float(np.max(np.abs(real_x))), 1e-30)
    scale_y = max(float(np.max(np.abs(real_y))), 1e-30)
    design = polynomial_features(real_x / scale_x, real_y / scale_y, degree)
    coefficient_x = np.linalg.lstsq(design, gx_flat, rcond=None)[0]
    coefficient_y = np.linalg.lstsq(design, gy_flat, rcond=None)[0]
    height, width = image_shape
    target_y = np.linspace(np.max(real_y), np.min(real_y), height)
    target_x = np.linspace(np.min(real_x), np.max(real_x), width)
    grid_x, grid_y = np.meshgrid(target_x, target_y)
    target = polynomial_features(grid_x.flatten() / scale_x, grid_y.flatten() / scale_y, degree)
    mapped_x = (target @ coefficient_x).reshape(height, width)
    mapped_y = -(target @ coefficient_y).reshape(height, width)
    return np.stack((mapped_x, mapped_y), axis=-1)


def bilinear_warp(image, grid):
    height, width = grid.shape[:2]
    output = np.zeros((height, width), dtype=float)
    for row in range(height):
        for column in range(width):
            source_x = ((grid[row, column, 0] + 1) * image.shape[1] - 1) / 2
            source_y = ((grid[row, column, 1] + 1) * image.shape[0] - 1) / 2
            x0 = int(np.floor(source_x))
            y0 = int(np.floor(source_y))
            fx = source_x - x0
            fy = source_y - y0

            def value(y, x):
                return image[y, x] if 0 <= y < image.shape[0] and 0 <= x < image.shape[1] else 0.0

            output[row, column] = (
                (1 - fy) * ((1 - fx) * value(y0, x0) + fx * value(y0, x0 + 1))
                + fy * ((1 - fx) * value(y0 + 1, x0) + fx * value(y0 + 1, x0 + 1))
            )
    return output


def image_simulation_data(optic):
    wavelengths = [0.65, 0.55, 0.45]
    source = image_test_chart()
    padding = 2
    padded = np.pad(source, ((0, 0), (padding, padding), (padding, padding)), mode="reflect")
    processed = []
    blurred_channels = []
    distortion_grids = []
    for channel, wavelength in enumerate(wavelengths):
        psfs = []
        for field_y in np.linspace(-1, 1, 2):
            for field_x in np.linspace(-1, 1, 2):
                raw = FFTPSF(
                    optic,
                    field=(field_x, field_y),
                    wavelength=wavelength,
                    num_rays=8,
                    grid_size=16,
                    strategy="chief_ray",
                    remove_tilt=False,
                ).psf
                psfs.append(np.asarray(raw) / np.sum(raw))
        psfs = np.stack(psfs)
        flattened = psfs.reshape(len(psfs), -1)
        mean = flattened.mean(axis=0)
        centered = flattened - mean
        u, singular, vt = np.linalg.svd(centered, full_matrices=False)
        eigen = vt[:3].reshape(3, 16, 16)
        coefficients = (u[:, :3] * singular[:3]).T.reshape(3, 2, 2)
        coefficients = zoom(coefficients, (1, padded.shape[1] / 2, padded.shape[2] / 2), order=1)
        blurred = fftconvolve(padded[channel], mean.reshape(16, 16), mode="same", axes=(-2, -1))
        blurred += fftconvolve(
            padded[channel][None] * coefficients,
            eigen,
            mode="same",
            axes=(-2, -1),
        ).sum(axis=0)
        grid = distortion_grid(optic, wavelength, blurred.shape)
        blurred_channels.append(blurred)
        distortion_grids.append(grid)
        processed.append(bilinear_warp(blurred, grid))
    simulated = np.maximum(np.stack(processed)[:, padding:-padding, padding:-padding], 0.0)
    return {
        "source": array(source),
        "simulated": array(simulated),
        "blurred": array(np.stack(blurred_channels)),
        "distortion_grids": array(np.stack(distortion_grids)),
        "shape": list(simulated.shape),
        "maximum": float(simulated.max()),
        "mean_absolute_change": float(np.mean(np.abs(simulated - source))),
    }


def analyze(name, optic, plot_dir):
    opd = OPD(
        optic,
        field=(0, 1),
        wavelength="primary",
        num_rays=5,
        strategy="chief_ray",
    )
    opd_data = opd.get_data(opd.fields[0], opd.wavelengths[0])
    wavefront_result = {
        "field": [0, 1],
        "wavelength": float(opd.wavelengths[0]),
        "normalized_pupil_x": array(opd.distribution.x),
        "normalized_pupil_y": array(opd.distribution.y),
        "pupil_x": array(opd_data.pupil_x),
        "pupil_y": array(opd_data.pupil_y),
        "pupil_z": array(opd_data.pupil_z),
        "opd": array(opd_data.opd),
        "intensity": array(opd_data.intensity),
        "radius": float(opd_data.radius),
        "rms": float(opd.rms()),
    }
    zernike = ZernikeOPD(
        optic,
        field=(0, 1),
        wavelength="primary",
        num_rings=5,
        zernike_type="fringe",
        num_terms=15,
        strategy="chief_ray",
    )
    zernike_result = {
        "field": [0, 1],
        "wavelength": float(zernike.wavelengths[0]),
        "indices": [[int(index["n"]), int(index["m"])] for index in zernike.zernike.indices],
        "coefficients": array(zernike.coeffs),
    }
    fft_psf = FFTPSF(
        optic,
        field=(0, 1),
        wavelength="primary",
        num_rays=16,
        grid_size=32,
        strategy="chief_ray",
        remove_tilt=False,
    )
    psf_result = {
        "field": [0, 1],
        "wavelength": float(fft_psf.wavelengths[0]),
        "num_rays": int(fft_psf.num_rays),
        "grid_size": int(fft_psf.grid_size),
        "working_fno": float(fft_psf._get_working_FNO()),
        "psf": array(fft_psf.psf),
        "strehl": float(fft_psf.strehl_ratio()),
    }
    fft_mtf = FFTMTF(
        optic,
        fields=[(0, 1)],
        wavelength="primary",
        num_rays=16,
        grid_size=32,
        strategy="chief_ray",
        remove_tilt=False,
    )
    mtf_result = {
        "field": [0, 1],
        "wavelength": float(fft_mtf.resolved_wavelength),
        "frequency": array(fft_mtf.freq),
        "tangential": array(fft_mtf.mtf[0][0]),
        "sagittal": array(fft_mtf.mtf[0][1]),
        "cutoff": float(fft_mtf.max_freq),
    }

    pupil_aberration = PupilAberration(optic, num_points=17)
    pupil_result = {
        "fields": [list(field) for field in pupil_aberration.fields],
        "wavelengths": array(pupil_aberration.wavelengths),
        "px": array(pupil_aberration.data["Px"]),
        "py": array(pupil_aberration.data["Py"]),
        "x": [
            [array(pupil_aberration.data[f"{field}"][f"{wavelength}"]["x"]) for wavelength in pupil_aberration.wavelengths]
            for field in pupil_aberration.fields
        ],
        "y": [
            [array(pupil_aberration.data[f"{field}"][f"{wavelength}"]["y"]) for wavelength in pupil_aberration.wavelengths]
            for field in pupil_aberration.fields
        ],
    }
    figure, axes = pupil_aberration.view()
    flat_axes = np.asarray(axes).flatten()
    pupil_result["panes"] = [
        {
            "title": axes_item.get_title(),
            "x_label": axes_item.get_xlabel(),
            "y_label": axes_item.get_ylabel(),
            "x_lim": list(axes_item.get_xlim()),
            "y_lim": list(axes_item.get_ylim()),
        }
        for axes_item in flat_axes
    ]
    save_plot(figure, plot_dir, f"{name}-pupil-aberration.png")

    yybar = YYbar(optic)
    figure, axes = yybar.view()
    yybar_result = {
        "wavelength": float(yybar.wavelengths[0]),
        "ya": array(yybar.data["ya"]),
        "yb": array(yybar.data["yb"]),
        "presentation": plot_metadata(axes),
        "line_labels": [line.get_label() for line in axes.lines[:-2]],
    }
    save_plot(figure, plot_dir, f"{name}-yybar.png")

    through_focus = ThroughFocusSpotDiagram(
        optic,
        delta_focus=0.1,
        num_steps=3,
        num_rings=3,
        distribution="hexapolar",
    )
    primary_index = optic.wavelengths.primary_index
    through_focus_x = []
    through_focus_y = []
    for step_data in through_focus.results:
        step_x = []
        step_y = []
        for field_data in step_data:
            reference = field_data[min(primary_index, len(field_data) - 1)]
            valid = np.asarray(reference.intensity) != 0
            cx = np.mean(np.asarray(reference.x)[valid])
            cy = np.mean(np.asarray(reference.y)[valid])
            step_x.append([array(wave.x - cx) for wave in field_data])
            step_y.append([array(wave.y - cy) for wave in field_data])
        through_focus_x.append(step_x)
        through_focus_y.append(step_y)
    figure, axes = through_focus.view()
    through_focus_result = {
        "fields": [list(field) for field in through_focus.fields],
        "wavelengths": array(through_focus.wavelengths),
        "defocus": [float(position - through_focus.nominal_focus) for position in through_focus.positions],
        "x": through_focus_x,
        "y": through_focus_y,
        "panes": [
            {
                "title": axes[index].get_title(),
                "x_label": axes[index].get_xlabel(),
                "y_label": axes[index].get_ylabel(),
                "x_lim": list(axes[index].get_xlim()),
                "y_lim": list(axes[index].get_ylim()),
            }
            for index in range(len(axes))
        ],
    }
    save_plot(figure, plot_dir, f"{name}-through-focus-spot.png")

    through_focus_mtf = ThroughFocusMTF(
        optic,
        spatial_frequency=20,
        delta_focus=0.1,
        num_steps=5,
        fields="all",
        wavelength="primary",
        num_rays=16,
    )
    figure, axes = through_focus_mtf.view()
    through_focus_mtf_result = {
        "fields": [list(field) for field in through_focus_mtf.fields],
        "wavelength": float(through_focus_mtf.wavelength),
        "defocus": array(np.asarray(through_focus_mtf.positions) - through_focus_mtf.nominal_focus),
        "tangential": [
            [float(through_focus_mtf.results[step][field]["tangential"]) for step in range(through_focus_mtf.num_steps)]
            for field in range(len(through_focus_mtf.fields))
        ],
        "sagittal": [
            [float(through_focus_mtf.results[step][field]["sagittal"]) for step in range(through_focus_mtf.num_steps)]
            for field in range(len(through_focus_mtf.fields))
        ],
        "series_x": [array(line.get_xdata()) for line in axes.lines],
        "series_y": [array(line.get_ydata()) for line in axes.lines],
        "line_labels": [line.get_label() for line in axes.lines],
        "presentation": plot_metadata(axes),
    }
    save_plot(figure, plot_dir, f"{name}-through-focus-mtf.png")

    ray_fan = RayFan(optic, num_points=17)
    ray_fan_result = {
        "fields": [list(field) for field in ray_fan.fields],
        "wavelengths": array(ray_fan.wavelengths),
        "px": array(ray_fan.data["Px"]),
        "py": array(ray_fan.data["Py"]),
        "x": [
            [array(ray_fan.data[f"{field}"][f"{wavelength}"]["x"]) for wavelength in ray_fan.wavelengths]
            for field in ray_fan.fields
        ],
        "y": [
            [array(ray_fan.data[f"{field}"][f"{wavelength}"]["y"]) for wavelength in ray_fan.wavelengths]
            for field in ray_fan.fields
        ],
        "intensity_x": [
            [array(ray_fan.data[f"{field}"][f"{wavelength}"]["intensity_x"]) for wavelength in ray_fan.wavelengths]
            for field in ray_fan.fields
        ],
        "intensity_y": [
            [array(ray_fan.data[f"{field}"][f"{wavelength}"]["intensity_y"]) for wavelength in ray_fan.wavelengths]
            for field in ray_fan.fields
        ],
    }
    figure, axes = ray_fan.view()
    ray_fan_result["panes"] = [
        {
            "title": axes[index].get_title(),
            "x_label": axes[index].get_xlabel(),
            "y_label": axes[index].get_ylabel(),
            "x_lim": list(axes[index].get_xlim()),
            "y_lim": list(axes[index].get_ylim()),
        }
        for index in range(len(axes))
    ]
    save_plot(figure, plot_dir, f"{name}-ray-fan.png")

    best_fit_ray_fan = BestFitRayFan(optic, num_points=17, num_rays_for_fit=5)
    fit_distribution = create_distribution("hexapolar")
    fit_distribution.generate_points(5)
    best_fit_centers = []
    best_fit_radii = []
    for field in best_fit_ray_fan.fields:
        strategy = BestFitSphereStrategy(optic, fit_distribution)
        wavefront_data = strategy.compute_wavefront_data(field, optic.primary_wavelength)
        best_fit_centers.append(list(strategy.center))
        best_fit_radii.append(float(wavefront_data.radius))
    figure, axes = best_fit_ray_fan.view()
    best_fit_ray_fan_result = {
        "fields": [list(field) for field in best_fit_ray_fan.fields],
        "wavelengths": array(best_fit_ray_fan.wavelengths),
        "px": array(best_fit_ray_fan.data["Px"]),
        "py": array(best_fit_ray_fan.data["Py"]),
        "centers": best_fit_centers,
        "radii": best_fit_radii,
        "x": [
            [array(best_fit_ray_fan.data[f"{field}"][f"{wavelength}"]["x"]) for wavelength in best_fit_ray_fan.wavelengths]
            for field in best_fit_ray_fan.fields
        ],
        "y": [
            [array(best_fit_ray_fan.data[f"{field}"][f"{wavelength}"]["y"]) for wavelength in best_fit_ray_fan.wavelengths]
            for field in best_fit_ray_fan.fields
        ],
        "intensity_x": [
            [array(best_fit_ray_fan.data[f"{field}"][f"{wavelength}"]["intensity_x"]) for wavelength in best_fit_ray_fan.wavelengths]
            for field in best_fit_ray_fan.fields
        ],
        "intensity_y": [
            [array(best_fit_ray_fan.data[f"{field}"][f"{wavelength}"]["intensity_y"]) for wavelength in best_fit_ray_fan.wavelengths]
            for field in best_fit_ray_fan.fields
        ],
        "panes": [
            {
                "title": axes[index].get_title(),
                "x_label": axes[index].get_xlabel(),
                "y_label": axes[index].get_ylabel(),
                "x_lim": list(axes[index].get_xlim()),
                "y_lim": list(axes[index].get_ylim()),
            }
            for index in range(len(axes))
        ],
    }
    save_plot(figure, plot_dir, f"{name}-best-fit-ray-fan.png")

    spot = SpotDiagram(optic, num_rings=6, distribution="hexapolar")
    centered_spot = spot._center_spots(spot.data)
    figure, axes = spot.view()
    spot_result = {
        "fields": [list(field) for field in spot.fields],
        "wavelengths": array(spot.wavelengths),
        "x": [[array(wave.x) for wave in field] for field in centered_spot],
        "y": [[array(wave.y) for wave in field] for field in centered_spot],
        "intensity": [[array(wave.intensity) for wave in field] for field in centered_spot],
        "panes": [
            {
                "title": axes[index].get_title(),
                "x_label": axes[index].get_xlabel(),
                "y_label": axes[index].get_ylabel(),
                "x_lim": list(axes[index].get_xlim()),
                "y_lim": list(axes[index].get_ylim()),
            }
            for index in range(len(spot.fields))
        ],
        "legend": [text.get_text() for text in figure.legends[0].get_texts()],
    }
    save_plot(figure, plot_dir, f"{name}-spot-diagram.png")

    encircled = EncircledEnergy(
        optic,
        num_rays=3,
        distribution="hexapolar",
        num_points=33,
    )
    figure, axes = encircled.view()
    encircled_result = {
        "fields": [list(field) for field in encircled.fields],
        "wavelength": float(encircled.wavelengths[0]),
        "radius": [array(line.get_xdata()) for line in axes.lines],
        "energy": [array(line.get_ydata()) for line in axes.lines],
        "presentation": plot_metadata(axes),
    }
    save_plot(figure, plot_dir, f"{name}-encircled-energy.png")

    rms = RmsSpotSizeVsField(
        optic,
        num_fields=9,
        num_rings=3,
        distribution="hexapolar",
    )
    figure, axes = rms.view()
    rms_result = {
        "field": array(rms._field[:, 1]),
        "wavelengths": array(rms.wavelengths),
        "spot_size": array(rms._spot_size),
        "presentation": plot_metadata(axes),
    }
    save_plot(figure, plot_dir, f"{name}-rms-vs-field.png")

    rms_wavefront = RmsWavefrontErrorVsField(
        optic,
        num_fields=9,
        num_rays=5,
        distribution="hexapolar",
    )
    rms_wavefront_result = {
        "field": array(rms_wavefront._field[:, 1]),
        "wavelengths": array(rms_wavefront.wavelengths),
        "wavefront_error": array(rms_wavefront._wavefront_error),
    }

    angle_pupil = PupilIncidentAngleVsHeight(
        optic,
        surface_idx=-1,
        axis=1,
        wavelength="primary",
        field=(0, 0),
        num_points=17,
    )
    angle_pupil_data = next(iter(angle_pupil.data.values()))
    angle_pupil_result = {
        "height": array(angle_pupil_data["height"]),
        "angle_radians": array(angle_pupil_data["angle"]),
        "scan_range": array(angle_pupil_data["scan_range"]),
        "fixed_coordinates": angle_pupil_data["fixed_coordinates"],
    }

    angle_field = FieldIncidentAngleVsHeight(
        optic,
        surface_idx=-1,
        axis=1,
        wavelength="primary",
        pupil=(0, 0),
        num_points=17,
    )
    angle_field_data = next(iter(angle_field.data.values()))
    angle_field_result = {
        "height": array(angle_field_data["height"]),
        "angle_radians": array(angle_field_data["angle"]),
        "scan_range": array(angle_field_data["scan_range"]),
        "fixed_coordinates": angle_field_data["fixed_coordinates"],
    }

    distortion = Distortion(optic, num_points=17, distortion_type="f-tan")
    figure, axes = distortion.view()
    distortion_result = {
        "wavelengths": array(distortion.wavelengths),
        "field": np.linspace(1e-10, optic.fields.max_field, 17).tolist(),
        "series": [array(values) for values in distortion.data],
        "presentation": plot_metadata(axes),
    }
    save_plot(figure, plot_dir, f"{name}-distortion.png")

    distortion_f_theta = Distortion(optic, num_points=17, distortion_type="f-theta")
    distortion_f_theta_result = {
        "wavelengths": array(distortion_f_theta.wavelengths),
        "field": np.linspace(1e-10, optic.fields.max_field, 17).tolist(),
        "series": [array(values) for values in distortion_f_theta.data],
    }

    grid = GridDistortion(optic, num_points=10, distortion_type="f-tan")
    figure, axes = grid.view()
    grid_result = {
        "wavelength": float(grid.wavelengths[0]),
        "xp": array(grid.data["xp"]),
        "yp": array(grid.data["yp"]),
        "xr": array(grid.data["xr"]),
        "yr": array(grid.data["yr"]),
        "max_distortion": float(grid.data["max_distortion"]),
        "presentation": plot_metadata(axes),
    }
    save_plot(figure, plot_dir, f"{name}-grid-distortion.png")

    grid_f_theta = GridDistortion(optic, num_points=10, distortion_type="f-theta")
    grid_f_theta_result = {
        "wavelength": float(grid_f_theta.wavelengths[0]),
        "xp": array(grid_f_theta.data["xp"]),
        "yp": array(grid_f_theta.data["yp"]),
        "xr": array(grid_f_theta.data["xr"]),
        "yr": array(grid_f_theta.data["yr"]),
        "max_distortion": float(grid_f_theta.data["max_distortion"]),
    }

    curvature = FieldCurvature(optic, num_points=17)
    figure, axes = curvature.view()
    curvature_result = {
        "wavelengths": array(curvature.wavelengths),
        "field": np.linspace(0, optic.fields.max_field, 17).tolist(),
        "tangential": [array(values[0]) for values in curvature.data],
        "sagittal": [array(values[1]) for values in curvature.data],
        "presentation": plot_metadata(axes),
    }
    save_plot(figure, plot_dir, f"{name}-field-curvature.png")

    image_simulation_result = image_simulation_data(optic)

    detector_half_width = 21.0 if name == "cooke" else 1.75
    detector_half_height = detector_half_width
    optic.surface_group.surfaces[-1].aperture = RectangularAperture(
        -detector_half_width,
        detector_half_width,
        -detector_half_height,
        detector_half_height,
    )
    irradiance = IncoherentIrradiance(
        optic,
        num_rays=3,
        res=(15, 13),
        fields="all",
        wavelengths="all",
        distribution="hexapolar",
    )
    figure, axes = irradiance.view(normalize=True)
    irradiance_result = {
        "detector_half_width": detector_half_width,
        "fields": [list(field) for field in irradiance.fields],
        "wavelengths": array(irradiance.wavelengths),
        "x_edges": array(irradiance.data[0][0][1]),
        "y_edges": array(irradiance.data[0][0][2]),
        "irradiance": [
            [array(entry[0]) for entry in field_block]
            for field_block in irradiance.data
        ],
        "normalized": [
            [
                array(entry[0] / float(np.max(entry[0])))
                if float(np.max(entry[0])) > 0
                else array(entry[0])
                for entry in field_block
            ]
            for field_block in irradiance.data
        ],
        "peaks": [[float(value) for value in field] for field in irradiance.peak_irradiance()],
        "titles": [[axes[f, w].get_title() for w in range(axes.shape[1])] for f in range(axes.shape[0])],
        "presentation": [[plot_metadata(axes[f, w]) for w in range(axes.shape[1])] for f in range(axes.shape[0])],
    }
    save_plot(figure, plot_dir, f"{name}-incoherent-irradiance.png")

    radiant_intensity = RadiantIntensity(
        optic,
        num_angular_bins_X=15,
        num_angular_bins_Y=13,
        angle_X_min=-30,
        angle_X_max=30,
        angle_Y_min=-30,
        angle_Y_max=30,
        use_absolute_units=True,
        reference_surface_index=-1,
        num_rays=3,
        distribution="hexapolar",
    )
    figure, axes = radiant_intensity.view(normalize=False)
    radiant_intensity_result = {
        "fields": [list(field) for field in radiant_intensity.fields],
        "wavelengths": array(radiant_intensity.wavelengths),
        "x_edges": array(radiant_intensity.data[0][0][1]),
        "y_edges": array(radiant_intensity.data[0][0][2]),
        "x_centers": array(radiant_intensity.data[0][0][3]),
        "y_centers": array(radiant_intensity.data[0][0][4]),
        "intensity": [
            [array(entry[0]) for entry in field_block]
            for field_block in radiant_intensity.data
        ],
        "peaks": [
            [float(value) for value in field]
            for field in radiant_intensity.peak_intensity_values()
        ],
        "map_titles": [
            [axes[f, w][0].get_title() for w in range(axes.shape[1])]
            for f in range(axes.shape[0])
        ],
        "cross_titles": [
            [axes[f, w][1].get_title() for w in range(axes.shape[1])]
            for f in range(axes.shape[0])
        ],
        "map_presentation": [
            [plot_metadata(axes[f, w][0]) for w in range(axes.shape[1])]
            for f in range(axes.shape[0])
        ],
        "cross_presentation": [
            [plot_metadata(axes[f, w][1]) for w in range(axes.shape[1])]
            for f in range(axes.shape[0])
        ],
    }
    save_plot(figure, plot_dir, f"{name}-radiant-intensity.png")

    geometric_mtf = GeometricMTF(
        optic,
        num_rays=9,
        distribution="uniform",
        num_points=33,
    )
    figure, axes = geometric_mtf.view()
    geometric_mtf_result = {
        "fields": [list(field) for field in geometric_mtf.fields],
        "wavelength": float(geometric_mtf.wavelengths[0]),
        "max_frequency": float(geometric_mtf.max_freq),
        "frequency": array(geometric_mtf.freq),
        "tangential": [array(item[0]) for item in geometric_mtf.mtf],
        "sagittal": [array(item[1]) for item in geometric_mtf.mtf],
        "diffraction_limit": array(geometric_mtf.diff_limited_mtf),
        "line_labels": [line.get_label() for line in axes.lines],
        "presentation": plot_metadata(axes),
    }
    save_plot(figure, plot_dir, f"{name}-geometric-mtf.png")

    sampled_frequency = np.linspace(
        0,
        1 / (optic.primary_wavelength * 1e-3 * optic.paraxial.FNO()),
        33,
    )
    sampled_tangential = []
    sampled_sagittal = []
    for field in optic.fields.get_field_coords():
        sampled = SampledMTF(
            optic,
            field,
            optic.primary_wavelength,
            num_rays=16,
            distribution="uniform",
            zernike_terms=37,
            zernike_type="fringe",
        )
        sampled_tangential.append(array(sampled.calculate_mtf([(value, 0.0) for value in sampled_frequency])))
        sampled_sagittal.append(array(sampled.calculate_mtf([(0.0, value) for value in sampled_frequency])))
    sampled_mtf_result = {
        "fields": [list(field) for field in optic.fields.get_field_coords()],
        "wavelength": float(optic.primary_wavelength),
        "frequency": array(sampled_frequency),
        "tangential": sampled_tangential,
        "sagittal": sampled_sagittal,
    }
    jones_result = jones_pupil_data(optic)

    return {
        "fft_psf": psf_result,
        "fft_mtf": mtf_result,
        "zernike": zernike_result,
        "wavefront": wavefront_result,
        "pupil_aberration": pupil_result,
        "yybar": yybar_result,
        "through_focus_spot": through_focus_result,
        "through_focus_mtf": through_focus_mtf_result,
        "ray_fan": ray_fan_result,
        "best_fit_ray_fan": best_fit_ray_fan_result,
        "spot_diagram": spot_result,
        "encircled_energy": encircled_result,
        "rms_vs_field": rms_result,
        "rms_wavefront_vs_field": rms_wavefront_result,
        "angle_vs_height_pupil": angle_pupil_result,
        "angle_vs_height_field": angle_field_result,
        "distortion": distortion_result,
        "distortion_f_theta": distortion_f_theta_result,
        "grid_distortion": grid_result,
        "grid_distortion_f_theta": grid_f_theta_result,
        "field_curvature": curvature_result,
        "jones_pupil": jones_result,
        "image_simulation": image_simulation_result,
        "incoherent_irradiance": irradiance_result,
        "radiant_intensity": radiant_intensity_result,
        "geometric_mtf": geometric_mtf_result,
        "sampled_mtf": sampled_mtf_result,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    parser.add_argument("--plot-dir", type=Path)
    args = parser.parse_args()
    result = {
        "optiland_version": "0.5.8",
        "cooke": analyze("cooke", CookeTriplet(), args.plot_dir),
        "tessar": analyze("tessar", TessarLens(), args.plot_dir),
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, allow_nan=False), encoding="utf-8")


if __name__ == "__main__":
    main()
