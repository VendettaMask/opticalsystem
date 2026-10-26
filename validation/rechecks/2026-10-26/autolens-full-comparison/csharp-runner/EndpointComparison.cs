using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

internal static class EndpointComparison
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static int Run(string originalJson, string nativeSnapshot, string outputDirectory)
    {
        var directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        var original = ImportOriginal(originalJson);
        var native = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(File.ReadAllText(nativeSnapshot), Json)!);
        AutoLensReferenceMaterials.Register(native.Materials);
        var results = new[] { Assess("original-python", original), Assess("existing-csharp", native) };
        Save("common-analysis.json", new { Scope = "Persisted endpoints in one shared Core CPU/double evaluator; prescribed sensor planes, no refocus or aperture resizing",
            RequestedFullFieldDegrees = 90, WavelengthsNm = new[] { 656, 587, 486 },
            PupilIntegration = "10 Gaussian area nodes x 32 azimuths", IndependentChecks = "Center + 64 pupil-boundary positions",
            Axes = "Meridional Y, 21 field positions from 0 to 45 degrees", IncludeSurfaceTransmission = false, Results = results });
        Save("original-imported.optic.json", original.ToSnapshot());
        return 0;

        object Assess(string name, Optic optic)
        {
            optic.Fields.Clear();
            optic.Fields.Add(new() { Label = "Axis", Weight = 1 });
            optic.Fields.Add(new() { Label = "Target edge", YAngleDegrees = 45, Weight = 1 });
            optic.RayAimingEnabled = true;
            optic.InvalidateRayTraceCache();
            var before = ContentFingerprint.Compute(optic.ToSnapshot());
            var focal = optic.Paraxial.EstimateEffectiveFocalLength();
            var entranceDiameter = optic.Paraxial.EstimateEntrancePupilDiameter();
            var integral = ApertureSampler.GenerateGaussianQuadrature(10, 32).ToArray();
            var checks = new[] { new PupilSample(0, 0, 1) }.Concat(ApertureSampler.Generate(64, PupilSampling.Ring)).ToArray();
            var fields = new List<object>();
            var rms = new List<double>();
            long attempted = 0, valid = 0;
            using var measured = SequentialTraceMeasurement.Begin();
            using var compute = new SequentialGpuSession(DesignComputeMode.Cpu);
            for (var index = 0; index <= 20; index++)
            {
                var fraction = index / 20.0;
                try
                {
                    var result = SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, 0, fraction, integral, checks,
                        includeSurfaceTransmission: false);
                    var i = result.Integration;
                    var c = result.Checks;
                    attempted += i.RayCount + c.RayCount;
                    valid += i.RayCount - i.VignettedRayCount + c.RayCount - c.VignettedRayCount;
                    if (i.Metrics is { } metrics) rms.Add(metrics.RmsSpotRadius);
                    fields.Add(new { NormalizedField = fraction, HalfAngleDegrees = 45 * fraction,
                        IntegrationAttempted = i.RayCount, IntegrationValid = i.RayCount - i.VignettedRayCount,
                        CheckAttempted = c.RayCount, CheckValid = c.RayCount - c.VignettedRayCount,
                        RmsMm = i.Metrics?.RmsSpotRadius, MaximumRadiusMm = result.MaximumRadius,
                        Radius80Mm = i.Metrics?.Radius80,
                        Wavelengths = i.Wavelengths.Select(w => new { w.Nanometers, w.AttemptedRayCount, ValidRayCount = w.Rays.Count }),
                        CheckWavelengths = c.Wavelengths.Select(w => new { w.Nanometers, w.AttemptedRayCount, ValidRayCount = w.Rays.Count }),
                        Error = (string?)null });
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    fields.Add(new { NormalizedField = fraction, HalfAngleDegrees = 45 * fraction, Error = error.ToString() });
                }
            }
            var after = ContentFingerprint.Compute(optic.ToSnapshot());
            var data = new { Name = name, FocalLengthMm = focal, EntrancePupilDiameterMm = entranceDiameter,
                FNumber = focal / entranceDiameter, SensorPositionMm = optic.SurfaceGroup.Items[^1].CoordinateSystem.Origin.Z - 20000,
                TargetRelativeFocalError = Math.Abs(focal - 5) / 5, TargetRelativeFNumberError = Math.Abs(focal / entranceDiameter - 1.8) / 1.8,
                WorstValidFieldRmsMm = rms.Count == 0 ? (double?)null : rms.Max(), ValidFieldCount = rms.Count, ExpectedFieldCount = 21,
                AttemptedRays = attempted, ValidRays = valid, ValidRayFraction = attempted == 0 ? 0 : (double)valid / attempted,
                AllFieldsHaveMetrics = rms.Count == 21, SnapshotFingerprintBefore = before, SnapshotFingerprintAfter = after,
                PrescriptionUnchangedByAnalysis = before == after, RaysTraced = measured.RayCount, Fields = fields,
                Prescription = Describe(optic) };
            Save(name + "-analysis.json", data);
            Console.WriteLine($"{name}: EFL={focal:R} F/{focal / entranceDiameter:R}, valid fields={rms.Count}/21 valid rays={valid}/{attempted}");
            return data;
        }
        void Save<T>(string name, T value) => File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, Json) + Environment.NewLine);
    }

    internal static Optic ImportOriginal(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var input = document.RootElement;
        var rows = input.GetProperty("surfaces").EnumerateArray().ToArray();
        var optic = AutoLensReferenceStart.Create(AutoLensReferenceExamples.Mobile().Specification, 6, null, AutoLensReferenceMaterials.ProjectionNames);
        var surfaces = new List<OpticalSurface> { new() { Label = "Object", Material = "Air", Thickness = 20000 } };
        foreach (var row in rows)
        {
            var name = row.GetProperty("mat2").GetString()!;
            surfaces.Add(new() { Label = row.GetProperty("type").GetString()!, Material = name == "air" ? "Air" : "AutoLensE58:" + name,
                SemiDiameter = row.GetProperty("r").GetDouble(), Thickness = row.GetProperty("d_next").GetDouble(),
                IsStop = row.GetProperty("type").GetString() == "Aperture" });
        }
        surfaces.Add(new() { Label = "Image", Material = "Air" });
        optic.SurfaceGroup.ImportLegacySurfaces(surfaces, optic.Materials);
        // Upstream r clips rays. A legacy/mechanical semi-diameter alone does not
        // establish an actual aperture in the shared Core document model.
        for (var index = 0; index < rows.Length; index++)
            RotationalDesignGeometry.SetRadius(optic, index + 1, rows[index].GetProperty("r").GetDouble());
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            if (row.GetProperty("type").GetString() == "Aperture") continue;
            var radius = row.GetProperty("roc").GetDouble();
            var conic = row.TryGetProperty("k", out var k) ? k.GetDouble() : 0;
            var ai = row.TryGetProperty("ai", out var coefficients) ? coefficients.EnumerateArray().Select(v => v.GetDouble()).ToArray() : [];
            optic.SurfaceGroup.Items[index + 1].Geometry = ai.Length > 0
                ? new EvenAsphereGeometry(radius, conic, new[] { 0.0 }.Concat(ai).ToArray()) : new StandardGeometry(radius, conic);
        }
        optic.SurfaceGroup.Items[0].PhysicalAperture = null;
        optic.SurfaceGroup.Items[^1].PhysicalAperture = null;
        OpticalDesignParameters.SynchronizeIncidentMedia(optic);
        optic.SurfaceGroup.Renumber();
        return optic;
    }

    private static object Describe(Optic optic) => new
    {
        Surfaces = optic.SurfaceGroup.Items.Skip(1).SkipLast(1).Select(s => new
        {
            s.Number, s.IsStop, PositionMm = s.CoordinateSystem.Origin.Z - 20000, RadiusMm = s.SemiDiameter,
            Curvature = OpticalDesignParameters.Profile(s.Geometry).Curvature, Conic = OpticalDesignParameters.Profile(s.Geometry).Conic,
            Coefficients = OpticalDesignParameters.Profile(s.Geometry).Coefficients, Material = s.MaterialAfter.Name
        }), SensorPositionMm = optic.SurfaceGroup.Items[^1].CoordinateSystem.Origin.Z - 20000
    };
}
