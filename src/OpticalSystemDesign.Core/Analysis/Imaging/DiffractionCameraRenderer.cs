using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public sealed record SceneDiffractionSettings(int PupilSamplesAcross = 12, int SceneSubdivisions = 1,
    int PsfRadiusPixels = 4, int SourceGuardPixels = 24);
public sealed record SceneDiffractionDiagnostics(int SurfaceSamples, long ScalarCellEvaluations,
    long OccludedPupilCells, double SourceAreaSquareMillimeters, double CollectedPowerWatts,
    IReadOnlyList<int> SamplesPerObject, double MaximumGeometricSpreadPixels = 0, int RequiredSourceGuardPixels = 0,
    string Algorithm = "ScalarHuygensSurfaceCells/1", long AvoidedAuxiliaryRays = 0, long SourceCandidateVisits = 0);
public sealed record DiffractionCameraRenderResult(DirectCameraRenderResult Image, SceneDiffractionDiagnostics Diffraction);
public sealed record CameraDetectorRegion(int X, int Y, int Width, int Height);
public sealed record DiffractionCameraRegionalResult(CameraDetectorRegion Region, int FullWidth, int FullHeight,
    DiffractionCameraRenderResult Result);

/// <summary>Spatially incoherent 3D surface quadrature; each source point/band uses one coherent pupil sum.
/// Actual finite-conjugate paths and masks precede diffraction. No geometric-image convolution or crop renormalization.</summary>
public static class DiffractionCameraRenderer
{
    public const long MaximumCellEvaluations = 200_000_000;
    public const int MaximumSurfaceSamples = 65_536;
    // Separate bounded allowance for local convergence, never a physical/error tolerance or a per-tile reset.
    public const long MaximumRegionalRays = 67_108_864;
    public const long MaximumRegionalSurfaceInteractions = 400_000_000;
    public const long MaximumRegionalCandidateVisits = 1_048_576;
    public static void Validate(SceneDiffractionSettings settings)
    {
        if (settings.PupilSamplesAcross is < 4 or > 64 || settings.SceneSubdivisions is < 1 or > 4
            || settings.PsfRadiusPixels is < 1 or > 32 || settings.SourceGuardPixels < settings.PsfRadiusPixels
            || settings.SourceGuardPixels > 32) throw new ArgumentException("衍射采样需为瞳面 4–64、场景细分 1–4，窗口半径 1–32，源保护带不小于窗口半径且不超过 32。");
    }

    public static DiffractionCameraRenderResult Render(Optic optic, DirectCameraScene scene,
        DirectCameraRenderSettings detector, SceneDiffractionSettings settings)
        => RenderCore(optic, scene, detector, settings, null);

    /// <summary>One diagnostic region in the ORIGINAL sensor coordinate system. Full-render limits remain unchanged;
    /// this API does not tile/stitch a full result or reset a budget within a render.</summary>
    public static DiffractionCameraRegionalResult RenderRegion(Optic optic, DirectCameraScene scene,
        DirectCameraRenderSettings detector, SceneDiffractionSettings settings, CameraDetectorRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (region.X < 0 || region.Y < 0 || region.Width < 4 || region.Height < 4
            || (long)region.X + region.Width > detector.Width || (long)region.Y + region.Height > detector.Height)
            throw new ArgumentException("局部检查区域须位于原传感器内，且每边至少 4 像元。");
        return new(region, detector.Width, detector.Height, RenderCore(optic, scene, detector, settings, region));
    }

    private static DiffractionCameraRenderResult RenderCore(Optic optic, DirectCameraScene scene,
        DirectCameraRenderSettings detector, SceneDiffractionSettings settings, CameraDetectorRegion? region)
    {
        ArgumentNullException.ThrowIfNull(optic); ArgumentNullException.ThrowIfNull(scene); Validate(settings);
        CameraSensorPipeline.ValidateDimensions(detector.Width, detector.Height); CameraPixelAperture.Validate(detector.PixelSampling);
        if (!double.IsFinite(detector.PixelPitchMicrometers) || detector.PixelPitchMicrometers is < .1 or > 100)
            throw new ArgumentException("Invalid physical camera pitch.");
        var copy = optic.Clone(); var surfaces = copy.SurfaceGroup.Items.ToArray();
        DirectCameraRenderer.Inspect(surfaces, scene.WavelengthsNanometers);
        if (copy.Apodization is not null || !copy.Polarization.Unpolarized || copy.ImageSpaceAfocal)
            throw new NotSupportedException("三维标量衍射不支持切趾、偏振或无焦输出。");
        var state = new Transport(surfaces, detector.BareInterfaceFresnel,
            region is null ? DirectCameraRenderer.MaximumRays : MaximumRegionalRays,
            region is null ? DirectCameraRenderer.MaximumSurfaceInteractions : MaximumRegionalSurfaceInteractions);
        var front = surfaces[1]; var rear = surfaces[^2]; var image = surfaces[^1];
        double Radius(OpticalSurface s) => s.PhysicalAperture is CircularAperture a ? a.Radius : s.SemiDiameter;
        var frontRadius = Radius(front); var rearRadius = Radius(rear);
        var frontSag = front.Geometry.Sag(frontRadius, 0); var rearSag = rear.Geometry.Sag(rearRadius, 0);
        var frontPlane = front.CoordinateSystem.Origin.Z + Math.Min(0, frontSag);
        var rearPlane = rear.CoordinateSystem.Origin.Z + Math.Max(0, rearSag);
        var imageZ = image.CoordinateSystem.Origin.Z;
        if (!double.IsFinite(frontPlane) || !double.IsFinite(rearPlane) || imageZ <= rearPlane + 1e-4)
            throw new NotSupportedException("衍射需要位于最后净口径像方的有限探测面。");
        var pitch = detector.PixelPitchMicrometers * .001;
        var nodes = CameraPixelAperture.Quadrature(detector.PixelSampling);
        var outputRegion = region ?? new(0, 0, detector.Width, detector.Height);
        var pixels = checked(outputRegion.Width * outputRegion.Height); var bandCount = scene.WavelengthsNanometers.Count;
        var powers = Enumerable.Range(0, bandCount).Select(_ => new double[pixels]).ToArray();
        var preview = new double[bandCount, outputRegion.Height, outputRegion.Width];
        var counts = new int[scene.ObjectIds.Count]; int samples = 0; long evaluations = 0, masked = 0, passed = 0, visible = 0;
        long shadowQueries = 0, shadowed = 0, avoidedAuxiliary = 0, sourceQueries = 0; double sampledArea = 0, collected = 0, maximumSpread = 0;
        var requiredGuard = settings.PsfRadiusPixels;
        var subdivision = settings.SceneSubdivisions; var guard = settings.SourceGuardPixels;
        var columns = checked((outputRegion.Width + 2 * guard) * subdivision);
        var rows = checked((outputRegion.Height + 2 * guard) * subdivision);
        var candidates = (long)columns * rows * scene.ObjectIds.Count;
        if (region is null && candidates > MaximumSurfaceSamples)
            throw new ArgumentException("三维源表面采样超过 65536 个候选；请降低分辨率、保护带或场景细分。");
        if (region is not null && candidates > MaximumRegionalCandidateVisits)
            throw new ArgumentException("局部源候选查询超过 1048576 次，未降低采样。");
        var referenceWavelength = scene.WavelengthsNanometers.MinBy(w => Math.Abs(w - 550));
        var pupil = PupilCells(settings.PupilSamplesAcross);
        for (var row = 0; row < rows; row++) for (var column = 0; column < columns; column++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var sx = (column + .5) / subdivision - guard - detector.Width / 2d + outputRegion.X;
            var sy = detector.Height / 2d + guard - (row + .5) / subdivision - outputRegion.Y;
            RealRay? Chief(double x, double y) => state.Trace(new(new(x * pitch, y * pitch, imageZ),
                new Vector3D(-x * pitch, -y * pitch, rearPlane - imageZ).NormalizeDirection(), referenceWavelength), reverse: true);
            var chief = Chief(sx, sy); if (chief is null) continue;
            const double derivativeStep = .01;
            var xPlus = Chief(sx + derivativeStep, sy); var xMinus = Chief(sx - derivativeStep, sy);
            var yPlus = Chief(sx, sy + derivativeStep); var yMinus = Chief(sx, sy - derivativeStep);
            if (xPlus is null || xMinus is null || yPlus is null || yMinus is null)
                throw new InvalidOperationException("源表面映射在光阑边界失效；请调整场景采样范围，不作几何回退。");
            var nearest = scene.Intersect(chief);
            for (var objectIndex = 0; objectIndex < scene.ObjectIds.Count; objectIndex++)
            {
                sourceQueries++;
                var hit = scene.IntersectObject(scene.ObjectIds[objectIndex], chief); if (hit is null) continue;
                Vector3D Tangent(RealRay ray)
                {
                    var denominator = Dot(hit.Normal, ray.Direction);
                    if (Math.Abs(denominator) < 1e-10) throw new InvalidOperationException("源表面为掠入射，有限面积映射不可用。");
                    return ray.Origin + ray.Direction * (Dot(hit.Normal, hit.Position - ray.Origin) / denominator);
                }
                var du = (Tangent(xPlus) - Tangent(xMinus)) / (2 * derivativeStep * subdivision);
                var dv = (Tangent(yPlus) - Tangent(yMinus)) / (2 * derivativeStep * subdivision);
                var area = Cross(du, dv).Length;
                if (!double.IsFinite(area) || area <= 0) throw new InvalidOperationException("无效源表面面积映射。");
                samples++; counts[objectIndex]++; sampledArea += area;
                if (samples > MaximumSurfaceSamples) throw new ArgumentException("实际源表面点超过 65536，未降低采样。");
                for (var b = 0; b < bandCount; b++)
                {
                    var wavelength = scene.WavelengthsNanometers[b];
                    var frontLighting = scene.Evaluate(hit, hit.Normal, b);
                    shadowQueries += frontLighting.ShadowQueries; shadowed += frontLighting.BlockedLights;
                    DirectSceneSample? backLighting = null;
                    DirectSceneSample BackLighting()
                    {
                        if (!scene.IsTwoSided(hit.ObjectId)) return default;
                        if (backLighting is { } cached) return cached;
                        var value = scene.Evaluate(hit, -hit.Normal, b); backLighting = value;
                        shadowQueries += value.ShadowQueries; shadowed += value.BlockedLights; return value;
                    }
                    var lighting = Dot(hit.Normal, -chief.Direction) > 0 ? frontLighting : BackLighting();
                    var outX = (int)Math.Floor(sx + detector.Width / 2d) - outputRegion.X;
                    var outY = (int)Math.Floor(detector.Height / 2d - sy) - outputRegion.Y;
                    if (nearest?.ObjectId == hit.ObjectId && (uint)outX < outputRegion.Width && (uint)outY < outputRegion.Height)
                        preview[b, outY, outX] += lighting.Reflectance / (subdivision * subdivision);
                    if (frontLighting.RadianceWattsPerSquareMeterSteradian <= 0 && BackLighting().RadianceWattsPerSquareMeterSteradian <= 0) continue;
                    var cells = new List<ScalarHuygensCell>(pupil.Count);
                    var geometric = new List<Vector3D>(pupil.Count);
                    var pending = region is null ? null : new List<PupilContribution>(pupil.Count);
                    var objectDistance = frontPlane - hit.Position.Z;
                    if (objectDistance <= 1e-4) throw new NotSupportedException("源表面必须完整位于第一镜面物方。");
                    var bound = frontRadius + Math.Sqrt(hit.Position.X * hit.Position.X + hit.Position.Y * hit.Position.Y)
                        * Math.Abs(frontSag) / objectDistance;
                    var cellWidth = 2 * bound / settings.PupilSamplesAcross;
                    RealRay AtPlane(RealRay ray)
                    {
                        var d = (rearPlane - ray.Origin.Z) / ray.Direction.Z;
                        if (d < -1e-7 || ray.Direction.Z <= 0) throw new NotSupportedException("衍射输出平面投影无效。");
                        return ray with { Origin = ray.Origin + ray.Direction * d, OpticalPathDifference = ray.OpticalPathDifference + d };
                    }
                    void AddCell(PupilContribution c)
                    {
                        RealRay Auxiliary(double dx, double dy)
                        {
                            var ray = state.Trace(new(hit.Position, (c.Target + new Vector3D(dx, dy, 0) - hit.Position).NormalizeDirection(), wavelength),
                                reverse: false, ignoreAperture: true);
                            return ray is null ? throw new InvalidOperationException("瞳面面积映射失效；不作几何回退。") : AtPlane(ray);
                        }
                        var h = cellWidth * .001;
                        var edgeU = (Auxiliary(h, 0).Origin - Auxiliary(-h, 0).Origin) * (cellWidth * Math.Sqrt(c.Fraction) / (2 * h));
                        var edgeV = (Auxiliary(0, h).Origin - Auxiliary(0, -h).Origin) * (cellWidth * Math.Sqrt(c.Fraction) / (2 * h));
                        var exitArea = Cross(edgeU, edgeV).Length;
                        if (!double.IsFinite(exitArea) || exitArea <= 1e-20) throw new NotSupportedException("瞳面焦散/奇异面积映射尚不支持。");
                        cells.Add(new(c.Center.Origin, c.Center.Direction, edgeU, edgeV, exitArea,
                            Math.Sqrt(c.Power / exitArea), c.Center.OpticalPathDifference));
                    }
                    foreach (var p in pupil)
                    {
                        var target = new Vector3D(p.X * bound, p.Y * bound, frontPlane);
                        var delta = target - hit.Position; var distance = delta.Length; var direction = delta / distance;
                        var signedCosine = Dot(hit.Normal, direction);
                        var radiance = signedCosine > 0 ? frontLighting.RadianceWattsPerSquareMeterSteradian : BackLighting().RadianceWattsPerSquareMeterSteradian;
                        var cosine = Math.Abs(signedCosine);
                        if (radiance <= 0 || cosine <= 0) continue;
                        var output = state.Trace(new(hit.Position, direction, wavelength), reverse: false);
                        if (output is null) continue; passed++;
                        if (scene.Intersect(new(hit.Position + direction * 1e-5, direction, wavelength), distance - 2e-5) is not null)
                        { masked++; continue; }
                        visible++;
                        var center = AtPlane(output);
                        if (center.Direction.Z < .95)
                            throw new NotSupportedException("三维衍射首版采用小角度标量场，像方光线与轴夹角须小于约 18°；不回退或改动镜头。");
                        var inputArea = cellWidth * cellWidth * p.Fraction;
                        var power = radiance * area * 1e-6 * cosine
                            * direction.Z * inputArea / (distance * distance) * output.Intensity;
                        collected += power;
                        var contribution = new PupilContribution(target, center, p.Fraction, power);
                        if (pending is null) AddCell(contribution); else pending.Add(contribution);
                        var toImage = (imageZ - center.Origin.Z) / center.Direction.Z;
                        geometric.Add(center.Origin + center.Direction * toImage);
                    }
                    if (geometric.Count == 0) continue;
                    maximumSpread = Math.Max(maximumSpread, geometric.Max(p => Math.Max(Math.Abs(p.X / pitch - sx), Math.Abs(p.Y / pitch - sy))));
                    // Only remove coordinate floating-point roundoff before taking a whole-pixel ceiling.
                    var roundoff = 64 * 2.2204460492503131e-16 * Math.Max(Math.Max(detector.Width, detector.Height), maximumSpread);
                    requiredGuard = settings.PsfRadiusPixels + (int)Math.Ceiling(Math.Max(0, maximumSpread - roundoff));
                    if (settings.SourceGuardPixels < requiredGuard)
                        throw new ArgumentException($"当前物深/像差需要至少 {requiredGuard} 像元源保护带（现为 {settings.SourceGuardPixels}，最大几何偏移 {maximumSpread:G6} 像元，源格位置 {sx:G5}/{sy:G5}）；请显式增加保护带，超出 32 则此配置暂不支持。未改动处方或改用几何成像。");
                    var lowX = Math.Max(outputRegion.X, (int)Math.Floor(geometric.Min(p => p.X) / pitch + detector.Width / 2d) - settings.PsfRadiusPixels);
                    var highX = Math.Min(outputRegion.X + outputRegion.Width - 1, (int)Math.Ceiling(geometric.Max(p => p.X) / pitch + detector.Width / 2d) + settings.PsfRadiusPixels);
                    var lowY = Math.Max(outputRegion.Y, (int)Math.Floor(detector.Height / 2d - geometric.Max(p => p.Y) / pitch) - settings.PsfRadiusPixels);
                    var highY = Math.Min(outputRegion.Y + outputRegion.Height - 1, (int)Math.Ceiling(detector.Height / 2d - geometric.Min(p => p.Y) / pitch) + settings.PsfRadiusPixels);
                    if (pending is not null)
                    {
                        // All primary rays, source cells, masks and the exact original geometric window were retained.
                        // Only an EMPTY intersection has no field samples: its four finite-difference rays per cell
                        // cannot affect this region. Never prune a nonzero tail inside the declared PSF window.
                        if (highX < lowX || highY < lowY) { avoidedAuxiliary += 4L * pending.Count; continue; }
                        foreach (var contribution in pending) AddCell(contribution);
                    }
                    var work = (long)Math.Max(0, highX - lowX + 1) * Math.Max(0, highY - lowY + 1) * nodes.Count * cells.Count;
                    evaluations += work;
                    if (evaluations > MaximumCellEvaluations) throw new ArgumentException("三维衍射超过两亿次复场单元求和；请降低分辨率、场景/瞳面采样或窗口。");
                    var activeArea = pitch * pitch * (detector.PixelSampling?.FillFactor ?? 1);
                    var prepared = region is null ? null : ScalarHuygensIntegral.Prepare(cells, wavelength);
                    for (var y = lowY; y <= highY; y++)
                    {
                        ComputationCancellation.ThrowIfCancellationRequested();
                        for (var x = lowX; x <= highX; x++) foreach (var node in nodes)
                        {
                            var point = new Vector3D((x + .5 - detector.Width / 2d + node.XOffsetPixels) * pitch,
                                (detector.Height / 2d - y - .5 + node.YOffsetPixels) * pitch, imageZ);
                            if (image.PhysicalAperture is not null && !image.PhysicalAperture.Contains(image.CoordinateSystem.ToLocalPoint(point))) continue;
                            powers[b][(y - outputRegion.Y) * outputRegion.Width + x - outputRegion.X] += (prepared is null
                                ? ScalarHuygensIntegral.IrradianceWattsPerSquareMillimeter(cells, point, wavelength)
                                : prepared.IrradianceWattsPerSquareMillimeter(point))
                                * activeArea * node.Weight;
                        }
                    }
                }
            }
        }
        var powerFrame = new SpectralDetectorPower(outputRegion.Width, outputRegion.Height, detector.PixelPitchMicrometers, detector.PixelPitchMicrometers,
            DetectorRowOrder.PositiveLocalYFirst, Enumerable.Range(0, bandCount).Select(b => new SpectralDetectorPowerBand(scene.WavelengthsNanometers[b], powers[b])),
            detector.PixelSampling?.FillFactor ?? 1);
        return new(new(powerFrame, new(preview), state.Launched, passed, visible, state.Interactions, shadowQueries, shadowed, rearPlane, rearRadius),
            new(samples, evaluations, masked, sampledArea, collected, Array.AsReadOnly(counts), maximumSpread, requiredGuard,
                AvoidedAuxiliaryRays: avoidedAuxiliary, SourceCandidateVisits: sourceQueries));
    }

    private readonly record struct PupilCell(double X, double Y, double Fraction);
    private readonly record struct PupilContribution(Vector3D Target, RealRay Center, double Fraction, double Power);
    private static IReadOnlyList<PupilCell> PupilCells(int count)
    {
        var cells = new List<PupilCell>(); const int subdivisions = 8;
        for (var row = 0; row < count; row++) for (var col = 0; col < count; col++)
        {
            double sx = 0, sy = 0; var inside = 0;
            for (var v = 0; v < subdivisions; v++) for (var u = 0; u < subdivisions; u++)
            {
                var x = -1 + 2d * (col + (u + .5) / subdivisions) / count;
                var y = -1 + 2d * (row + (v + .5) / subdivisions) / count;
                if (x * x + y * y > 1) continue; inside++; sx += x; sy += y;
            }
            if (inside > 0) cells.Add(new(sx / inside, sy / inside, inside / (double)(subdivisions * subdivisions)));
        }
        return cells;
    }

    private sealed class Transport(OpticalSurface[] surfaces, bool fresnel, long maximumRays, long maximumInteractions)
    {
        private readonly OpticalSurface[] _reverse = surfaces.Select((s, i) => i > 0 && i < surfaces.Length - 1 && s.InteractionModel is ThinLensInteractionModel
            ? DirectCameraRenderer.ReverseThinLens(s) : s).ToArray();
        public long Launched { get; private set; }
        public long Interactions { get; private set; }
        public RealRay? Trace(RealRay ray, bool reverse, bool ignoreAperture = false)
        {
            ComputationCancellation.ThrowIfCancellationRequested(); Launched++;
            if (Launched > maximumRays) throw new ArgumentException($"三维衍射追迹超过本次 {maximumRays} 条光线工作上限，未降低采样。");
            for (var step = 1; step < surfaces.Length - 1; step++)
            {
                Interactions++;
                if (Interactions > maximumInteractions) throw new ArgumentException($"三维衍射追迹超过本次 {maximumInteractions} 次表面交互上限，未降低采样。");
                var i = reverse ? surfaces.Length - 1 - step : step;
                var s = surfaces[i]; var before = surfaces[reverse ? i : i - 1].MaterialAfter;
                var after = surfaces[reverse ? i - 1 : i].MaterialAfter;
                var trace = (reverse ? _reverse[i] : s).TraceRayValue(ray, before, after, 0, 0, ignoreAperture);
                if (trace.StopTracing || trace.Sample.Vignetted || trace.InteractionKind != RayInteractionKind.Transmitted) return null;
                var local = s.CoordinateSystem.ToLocalPoint(trace.Ray.Origin);
                if (!ignoreAperture && s.PhysicalAperture is null && local.X * local.X + local.Y * local.Y > s.SemiDiameter * s.SemiDiameter) return null;
                var factor = 1d;
                if (fresnel && s.CoatingModel is NoneCoatingModel)
                {
                    var normal = s.CoordinateSystem.ToGlobalDirection(s.Geometry.SurfaceNormal(local));
                    var f = FresnelPower.EvaluateHomogeneousPower(new(before.RefractiveIndex(ray.WavelengthNanometers), before.ExtinctionCoefficient(ray.WavelengthNanometers)),
                        new(after.RefractiveIndex(ray.WavelengthNanometers), after.ExtinctionCoefficient(ray.WavelengthNanometers)),
                        Math.Clamp(Math.Abs(Dot(normal, ray.Direction.NormalizeDirection())), 1e-15, 1));
                    factor = (f.TransmissionS.Magnitude * f.TransmissionS.Magnitude + f.TransmissionP.Magnitude * f.TransmissionP.Magnitude) / 2;
                }
                ray = trace.Ray.Normalize() with { Intensity = trace.Ray.Intensity * factor };
            }
            return ray;
        }
    }
    private static double Dot(Vector3D a, Vector3D b) => DirectCameraScene.Dot(a, b);
    private static Vector3D Cross(Vector3D a, Vector3D b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
