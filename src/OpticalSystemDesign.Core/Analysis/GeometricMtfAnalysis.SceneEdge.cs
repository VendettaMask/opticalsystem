using System.Numerics;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed record SceneEdgeMtfField(double ObjectXMillimeters, double ObjectYMillimeters,
    double ObjectDistanceMillimeters, double ChartLocalXMillimeters, double ChartLocalYMillimeters,
    double ImageDistanceFromFirstSurfaceMillimeters, double MappingWavelengthNanometers,
    double? AngleFromTangentialDegrees);
public sealed record SceneEdgeMtfReference(SceneEdgeMtfField Field, int PupilAxisSamples, int LaunchedRays,
    int AcceptedRays, IReadOnlyList<SfrSample> Optical, IReadOnlyList<SfrSample> WithPixelAperture);

public sealed partial class GeometricMtfAnalysis
{
    public const string SceneEdgeAlgorithm = "GeometricMtfSceneEdge/1; continuous finite entrance-pupil chief-ray field; projected geometric OTF; diffraction off; square active area";

    /// <summary>Formal geometric OTF along the measured detector normal at the frozen image plane.
    /// Uses the shared forward spot trace and its image-plane intensity convention.</summary>
    public static SceneEdgeMtfReference EvaluateSceneEdge(Optic source, EmissiveCheckerboardSettings chart,
        IReadOnlyList<SceneEmissionNode> spectrum, LinearSfrEdgeGeometry edge, double pitchMicrometers,
        double fillFactor, int pupilAxisSamples = 129, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(spectrum);
        ArgumentNullException.ThrowIfNull(edge);
        using var scope = ComputationCancellation.Push(cancellationToken);
        var normalLength = Math.Sqrt(edge.NormalX * edge.NormalX + edge.NormalY * edge.NormalY);
        if (source.ImageSpaceAfocal || source.SurfaceGroup.Items.Count < 3
            || !double.IsFinite(edge.XMillimeters) || !double.IsFinite(edge.YMillimeters)
            || !double.IsFinite(normalLength) || Math.Abs(normalLength - 1) > 1e-9
            || !double.IsFinite(pitchMicrometers) || pitchMicrometers <= 0
            || !double.IsFinite(fillFactor) || fillFactor is < .01 or > 1 || pupilAxisSamples is < 3 or > 257)
            throw new ArgumentException("A focal detector, unit detector normal, positive pitch and valid sampling are required.");

        EmissiveCheckerboard.Validate(chart, spectrum.Select(n => new EmissiveCheckerboardBand(n.WavelengthNanometers, n.BrightRadiance, n.DarkRadiance)).ToArray());
        var waves = spectrum.Where(n => n.BrightRadiance != n.DarkRadiance)
            .Select(n => new Wavelength { Nanometers = n.WavelengthNanometers, Weight = Math.Abs(n.BrightRadiance - n.DarkRadiance), IsPrimary = false })
            .ToArray();
        if (waves.Length == 0)
            throw new AnalysisDataUnavailableException("Scene geometric MTF", "the source has no edge contrast");
        if (spectrum.Any(n => n.BrightRadiance > n.DarkRadiance) && spectrum.Any(n => n.BrightRadiance < n.DarkRadiance))
            throw new NotSupportedException("Scene MTF requires common edge polarity across spectral nodes.");
        var mappingWave = waves.OrderByDescending(w => w.Weight)
            .ThenBy(w => Math.Abs(w.Nanometers - spectrum.Average(n => n.WavelengthNanometers))).First();
        mappingWave.IsPrimary = true;

        var optic = source.Clone();
        var surfaces = optic.SurfaceGroup.Items;
        DirectCameraRenderer.Inspect(surfaces.ToArray(), waves.Select(w => w.Nanometers).ToArray());
        var firstZ = surfaces[1].CoordinateSystem.Origin.Z;
        var imageDistance = surfaces[^1].CoordinateSystem.Origin.Z - firstZ;
        var pose = new CoordinateSystem(new(chart.CenterXMillimeters, chart.CenterYMillimeters,
            firstZ - chart.DistanceMillimeters), chart.RotationXDegrees, chart.RotationYDegrees, chart.RotationZDegrees);
        var chartNormal = pose.ToGlobalDirection(new(0, 0, 1));
        if (Math.Abs(chartNormal.Z) < 1e-6)
            throw new AnalysisDataUnavailableException("Scene geometric MTF", "the target plane is grazing");

        // The temporary object surface is an axial plane through the actual chart point.
        // Moving it translates the downstream stack as a whole; lens spacing and focus are unchanged.
        optic.Fields.Clear();
        optic.Wavelengths.Clear();
        foreach (var wave in waves) optic.Wavelengths.Add(wave);
        optic.FieldDefinition = FieldDefinitionKind.ObjectHeight;
        surfaces[0].Geometry = new PlaneGeometry();
        surfaces[0].CoordinateSystem = CoordinateSystem.Global;
        surfaces[0].PhysicalAperture = null;
        surfaces[^1].PhysicalAperture = null;
        // Scene transport treats the mechanical semi-diameter as the clear aperture when none is explicit.
        foreach (var surface in surfaces.Skip(1).SkipLast(1))
            surface.PhysicalAperture ??= new CircularAperture(surface.SemiDiameter);
        var distance = chart.DistanceMillimeters;
        var objectField = (X: 0.0, Y: 0.0);
        Vector3D chartPoint = default;
        var converged = false;
        for (var iteration = 0; iteration < 24; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!double.IsFinite(distance) || distance <= 1)
                break;
            surfaces[0].Thickness = distance;
            optic.SurfaceGroup.Renumber();
            // Scene transport does not use the saved field vignetting factors or an alternate focus.
            objectField = optic.SequentialRayTracer.RayGenerator.ResolveRealImageFieldCoordinates(
                edge.XMillimeters, edge.YMillimeters, geometricReference: true);
            var origin = surfaces[0].CoordinateSystem.Origin;
            var x = origin.X + objectField.X;
            var y = origin.Y + objectField.Y;
            var depth = chart.DistanceMillimeters
                + (chartNormal.X * (x - pose.Origin.X) + chartNormal.Y * (y - pose.Origin.Y)) / chartNormal.Z;
            chartPoint = pose.ToLocalPoint(new(x, y, firstZ - distance));
            if (Math.Abs(depth - distance) <= 1e-7)
            {
                converged = true;
                break;
            }
            distance = depth;
        }
        if (!converged || Math.Abs(chartPoint.X) > chart.Columns * chart.CellSizeMillimeters / 2
            || Math.Abs(chartPoint.Y) > chart.Rows * chart.CellSizeMillimeters / 2)
            throw new AnalysisDataUnavailableException("Scene geometric MTF", "the ROI chief ray did not map inside the finite target");

        optic.Fields.Add(new FieldPoint { X = objectField.X, Y = objectField.Y });
        var field = SpotAnalysisEngine.DefinedFields(optic);
        var spot = SpotAnalysisEngine.Generate(optic, field, waves, pupilAxisSamples, "uniform",
            reference: "absolute", cancellationToken: cancellationToken);
        var pitch = pitchMicrometers * .001;
        var frequency = Enumerable.Range(0, 129).Select(i => i / (256d * pitch)).ToArray();
        var noDiffraction = frequency.Select(_ => 1d).ToArray();
        var otf = new Complex[frequency.Length];
        var dc = 0d;
        foreach (var wave in spot.Fields.Single().Wavelengths)
        {
            if (wave.Rays.Count == 0) continue;
            var projected = wave.Rays.Select(r => (r.X - edge.XMillimeters) * edge.NormalX
                + (r.Y - edge.YMillimeters) * edge.NormalY).ToArray();
            var intensities = wave.Rays.Select(r => r.Intensity).ToArray();
            // Keep lateral-colour phase at the same object point. Weight the DC by source contrast and traced throughput.
            var weight = wave.Wavelength.Weight * intensities.Sum();
            var response = ComputeOtf(projected, intensities, frequency, noDiffraction);
            for (var i = 0; i < otf.Length; i++) otf[i] += weight * response[i];
            dc += weight;
        }
        if (!(dc > 0) || !double.IsFinite(dc))
            throw new AnalysisDataUnavailableException("Scene geometric MTF", "no weighted rays reached the saved image plane");

        var activeSide = pitch * Math.Sqrt(fillFactor);
        var optical = frequency.Select((f, i) => new SfrSample(i / 256d, f, Math.Clamp(otf[i].Magnitude / dc, 0, 1))).ToArray();
        var pixel = optical.Select(p => p with { Response = p.Response * Math.Abs(
            Sinc(p.CyclesPerMillimeter * activeSide * edge.NormalX) * Sinc(p.CyclesPerMillimeter * activeSide * edge.NormalY)) }).ToArray();
        var radius = Math.Sqrt(edge.XMillimeters * edge.XMillimeters + edge.YMillimeters * edge.YMillimeters);
        double? tangentAngle = radius <= 1e-9 ? null : Math.Acos(Math.Clamp(Math.Abs(
            (edge.XMillimeters * edge.NormalX + edge.YMillimeters * edge.NormalY) / radius), 0, 1)) * 180 / Math.PI;
        return new(new(objectField.X, objectField.Y, distance, chartPoint.X, chartPoint.Y, imageDistance,
            mappingWave.Nanometers, tangentAngle), pupilAxisSamples, spot.RayCount, spot.RayCount - spot.VignettedRayCount,
            Array.AsReadOnly(optical), Array.AsReadOnly(pixel));
    }

    private static double Sinc(double cycles) => Math.Abs(cycles) <= 1e-12 ? 1 : Math.Sin(Math.PI * cycles) / (Math.PI * cycles);
}
