using System.Numerics;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public sealed record DirectCameraRenderSettings(int Width, int Height, double PixelPitchMicrometers,
    CameraPixelSampling? PixelSampling = null, int PupilRings = 2, int PupilArms = 8, bool BareInterfaceFresnel = true);
public sealed record DirectCameraRenderResult(SpectralDetectorPower DetectorPower, RgbImage VisibleReflectance,
    long LaunchedRays, long PassedLensRays, long SceneHitRays, long SurfaceInteractions,
    long ShadowQueries, long ShadowedLightSamples, double PupilPlaneZMillimeters, double PupilPlaneRadiusMillimeters);

/// <summary>Direct, non-polarized geometric camera transport through the actual shared-Core sequential surfaces.
/// Scene intersections use the NSC BVH. Pixel quadrature samples the actual active area; no 2D PSF fallback.</summary>
public static class DirectCameraRenderer
{
    public const long MaximumRays = 8_388_608;
    public const long MaximumSurfaceInteractions = 100_000_000;

    public static DirectCameraRenderResult Render(Optic optic, DirectCameraScene scene, DirectCameraRenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(optic); ArgumentNullException.ThrowIfNull(scene); ArgumentNullException.ThrowIfNull(settings);
        CameraSensorPipeline.ValidateDimensions(settings.Width, settings.Height); CameraPixelAperture.Validate(settings.PixelSampling);
        if (!double.IsFinite(settings.PixelPitchMicrometers) || settings.PixelPitchMicrometers is < .1 or > 100
            || settings.PupilRings is < 1 or > 8 || settings.PupilArms is < 4 or > 32)
            throw new ArgumentException("Invalid camera pixel pitch or pupil quadrature.");
        var copy = optic.Clone(); var surfaces = copy.SurfaceGroup.Items.ToArray();
        if (copy.Apodization is not null || !copy.Polarization.Unpolarized)
            throw new NotSupportedException("三维直射首版不支持切趾或偏振设置，请显式关闭后运行。");
        Inspect(surfaces, scene.WavelengthsNanometers);
        var pixelNodes = CameraPixelAperture.Quadrature(settings.PixelSampling);
        var pupilNodes = ApertureSampler.GenerateGaussianQuadrature(settings.PupilRings, settings.PupilArms);
        var rayBudget = checked((long)settings.Width * settings.Height * pixelNodes.Count * pupilNodes.Count * scene.WavelengthsNanometers.Count);
        if (rayBudget > MaximumRays || rayBudget * (surfaces.Length - 2) > MaximumSurfaceInteractions)
            throw new ArgumentException("三维场景追迹超过光线/表面工作上限，请降低分辨率或采样。");
        var image = surfaces[^1]; var rear = surfaces[^2]; var pixelPitch = settings.PixelPitchMicrometers * .001;
        var radius = rear.PhysicalAperture is CircularAperture aperture ? aperture.Radius : rear.SemiDiameter;
        var rearEdgeZ = rear.CoordinateSystem.Origin.Z + rear.Geometry.Sag(radius, 0);
        var rearMinimumZ = Math.Min(rearEdgeZ, rear.CoordinateSystem.Origin.Z);
        var rearMaximumZ = Math.Max(rearEdgeZ, rear.CoordinateSystem.Origin.Z);
        var imageZ = image.CoordinateSystem.Origin.Z;
        if (!double.IsFinite(rearEdgeZ) || imageZ - rearMaximumZ <= 1e-4)
            throw new NotSupportedException("探测面必须位于最后透镜完整净口径的像方，且具有有限间隔。");
        var planeZ = rearMaximumZ;
        var halfDiagonal = pixelPitch * Math.Sqrt(settings.Width * settings.Width + settings.Height * settings.Height) / 2;
        var ratioMaximum = (imageZ - planeZ) / (imageZ - rearMaximumZ);
        var ratioMinimum = (imageZ - planeZ) / (imageZ - rearMinimumZ);
        var pupilRadius = radius * ratioMaximum + halfDiagonal * (1 - ratioMinimum);
        var pupilArea = Math.PI * pupilRadius * pupilRadius;
        var fill = settings.PixelSampling?.FillFactor ?? 1;
        var activeAreaMeters = Math.Pow(settings.PixelPitchMicrometers * 1e-6, 2) * fill;
        var pixels = checked(settings.Width * settings.Height); var bands = scene.WavelengthsNanometers.Count;
        var power = Enumerable.Range(0, bands).Select(_ => new double[pixels]).ToArray();
        var reflectance = new double[bands, settings.Height, settings.Width];
        long launched = 0, passed = 0, hits = 0, interactions = 0, shadowQueries = 0, shadowed = 0;
        var reverse = surfaces.Select((s, index) => s.InteractionModel is ThinLensInteractionModel && index > 0 && index < surfaces.Length - 1
            ? ReverseThinLens(s) : s).ToArray();
        for (var y = 0; y < settings.Height; y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < settings.Width; x++) foreach (var pixel in pixelNodes)
                for (var b = 0; b < bands; b++) foreach (var pupil in pupilNodes)
                {
                    ComputationCancellation.ThrowIfCancellationRequested(); launched++;
                    var origin = new Vector3D((x + .5 - settings.Width / 2d + pixel.XOffsetPixels) * pixelPitch,
                        (settings.Height / 2d - y - .5 + pixel.YOffsetPixels) * pixelPitch, imageZ);
                    if (image.PhysicalAperture is not null && !image.PhysicalAperture.Contains(image.CoordinateSystem.ToLocalPoint(origin))) continue;
                    var target = new Vector3D(pupil.X * pupilRadius, pupil.Y * pupilRadius, planeZ);
                    var delta = target - origin; var distance = delta.Length;
                    var ray = new RealRay(origin, delta / distance, scene.WavelengthsNanometers[b]);
                    var geometryWeight = pixel.Weight * pupil.Weight * pupilArea * ray.Direction.Z * ray.Direction.Z / (distance * distance);
                    var alive = true;
                    for (var i = surfaces.Length - 2; i >= 1; i--)
                    {
                        var before = surfaces[i].MaterialAfter; var after = surfaces[i - 1].MaterialAfter;
                        interactions++;
                        var trace = reverse[i].TraceRayValue(ray, before, after, 0, 0);
                        if (trace.StopTracing || trace.Sample.Vignetted || trace.InteractionKind != RayInteractionKind.Transmitted)
                        { alive = false; break; }
                        var local = surfaces[i].CoordinateSystem.ToLocalPoint(trace.Ray.Origin);
                        if (local.X * local.X + local.Y * local.Y > surfaces[i].SemiDiameter * surfaces[i].SemiDiameter
                            && surfaces[i].PhysicalAperture is null) { alive = false; break; }
                        var segment = (trace.Ray.Origin - ray.Origin).Length;
                        if (segment > 2e-6 && scene.Intersect(ray, segment - 1e-6) is not null) { alive = false; break; }
                        var transmission = 1d;
                        if (settings.BareInterfaceFresnel && surfaces[i].CoatingModel is NoneCoatingModel)
                        {
                            var normal = surfaces[i].CoordinateSystem.ToGlobalDirection(surfaces[i].Geometry.SurfaceNormal(local));
                            var cosine = Math.Clamp(Math.Abs(DirectCameraScene.Dot(normal, ray.Direction.NormalizeDirection())), 1e-15, 1);
                            var f = FresnelPower.EvaluateHomogeneousPower(
                                new(before.RefractiveIndex(ray.WavelengthNanometers), before.ExtinctionCoefficient(ray.WavelengthNanometers)),
                                new(after.RefractiveIndex(ray.WavelengthNanometers), after.ExtinctionCoefficient(ray.WavelengthNanometers)), cosine);
                            transmission = (f.TransmissionS.Magnitude * f.TransmissionS.Magnitude + f.TransmissionP.Magnitude * f.TransmissionP.Magnitude) / 2;
                        }
                        ray = trace.Ray.Normalize() with { Intensity = trace.Ray.Intensity * transmission };
                    }
                    if (!alive) continue; passed++;
                    var hit = scene.Intersect(ray); if (hit is null) continue; hits++;
                    var sample = scene.Evaluate(hit, -ray.Direction, b);
                    shadowQueries += sample.ShadowQueries; shadowed += sample.BlockedLights;
                    var irradiance = sample.RadianceWattsPerSquareMeterSteradian * ray.Intensity * geometryWeight;
                    if (!double.IsFinite(irradiance) || irradiance < 0) throw new InvalidOperationException("Nonfinite direct image irradiance.");
                    power[b][y * settings.Width + x] += irradiance * activeAreaMeters;
                    reflectance[b, y, x] += sample.Reflectance * pixel.Weight * pupil.Weight;
                }
        }
        if (passed == 0) throw new InvalidOperationException("所有瞳面样本被镜头遮挡；请检查处方与像面，或增加瞳面环数以核实是否漏采有效通光区域。");
        var detector = new SpectralDetectorPower(settings.Width, settings.Height, settings.PixelPitchMicrometers, settings.PixelPitchMicrometers,
            DetectorRowOrder.PositiveLocalYFirst, Enumerable.Range(0, bands).Select(b => new SpectralDetectorPowerBand(scene.WavelengthsNanometers[b], power[b])), fill);
        return new(detector, new(reflectance), launched, passed, hits, interactions, shadowQueries, shadowed, planeZ, pupilRadius);
    }

    internal static OpticalSurface ReverseThinLens(OpticalSurface surface)
    {
        var clone = surface.Clone();
        clone.CoordinateSystem = new(surface.CoordinateSystem.Origin, RotationYDegrees: 180);
        return clone;
    }

    internal static void Inspect(OpticalSurface[] surfaces, IReadOnlyList<double> wavelengths)
    {
        if (surfaces.Length is < 3 or > 258) throw new NotSupportedException("需要有限实体表面的顺序镜头。");
        for (var i = 1; i < surfaces.Length; i++)
        {
            var s = surfaces[i]; var cs = s.CoordinateSystem;
            if (cs.Origin.X != 0 || cs.Origin.Y != 0 || cs.RotationXDegrees != 0 || cs.RotationYDegrees != 0 || cs.RotationZDegrees != 0
                || !double.IsFinite(cs.Origin.Z) || i > 1 && cs.Origin.Z < surfaces[i - 1].CoordinateSystem.Origin.Z)
                throw new NotSupportedException("三维直射首版支持共轴、非折返的顺序镜头。");
            if (i == surfaces.Length - 1) continue;
            if (s.InteractionModel is ThinLensInteractionModel && (s.Geometry is not PlaneGeometry
                || wavelengths.Any(w => s.MaterialAfter.RefractiveIndex(w) != surfaces[i - 1].MaterialAfter.RefractiveIndex(w))))
                throw new NotSupportedException("理想薄透镜只支持同介质平面。");
            if (s.IsReflective || s.ScatteringModel is not null || s.Geometry is not (PlaneGeometry or StandardGeometry)
                || s.Conic != 0 || s.CoatingModel is not (NoneCoatingModel or SimpleCoatingModel)
                || s.InteractionModel is not (RefractiveReflectiveInteractionModel or ThinLensInteractionModel)
                || s.MaterialAfter is GradientIndexMaterial || surfaces[i - 1].MaterialAfter is GradientIndexMaterial
                || s.PhysicalAperture is not (null or CircularAperture) || !double.IsFinite(s.SemiDiameter) || s.SemiDiameter <= 0)
                throw new NotSupportedException($"表面 {s.Number} 超出三维直射首版的球面/平面、圆孔径和透射范围。");
        }
        foreach (var wavelength in wavelengths)
            if (Math.Abs(surfaces[0].MaterialAfter.RefractiveIndex(wavelength) - 1) > 1e-12
                || Math.Abs(surfaces[^2].MaterialAfter.RefractiveIndex(wavelength) - 1) > 1e-12)
                throw new NotSupportedException("三维直射首版要求物方与像方外部介质为空气。");
    }
}

internal static class DirectCameraVectorExtensions
{
    internal static Vector3D NormalizeDirection(this Vector3D direction) => direction / direction.Length;
}
