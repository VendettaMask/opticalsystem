using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

if (args.Length != 2) throw new ArgumentException("<repository-root> <new-evidence-directory>");
var root = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Existing evidence is never overwritten.");
Directory.CreateDirectory(output);
try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
using var serial = ComputationParallelism.SuppressNestedParallelism();
var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};
var history = new List<object>(); var nulls = new List<object>(); var physical = new List<object>();
for (var seed = 42; seed <= 46; seed++)
{
    foreach (var name in new[] { "competition-8-F8-full25", "competition-8-F4-full25", "original-half21" })
    {
        var path = Path.Combine(root, $"validation/rechecks/2026-10-30/initial-structure-m2/formal/fixed-seed{seed}/{name}.seed-search.json");
        using var saved = JsonDocument.Parse(File.ReadAllBytes(path));
        var previous = saved.RootElement.GetProperty("result").Deserialize<ParaxialSeedSearchResult>(json)!;
        Require(saved.RootElement.GetProperty("sha256").GetString() == ContentFingerprint.Compute(previous), "Historical envelope content hash changed.");
        var parent = previous.Evolution!.Parents.Single();
        var source = TopologyCompiler.Compile(parent.Topology, previous.SourcePlan.Constraints);
        Require(parent.Assessment.OpticFingerprint == source.OpticFingerprint, "Historical source snapshot changed.");
        var current = TopologyValidator.Validate(source, previous.SourcePlan.Targets);
        Write($"{name}-seed{seed}.historical-assessment.json", new { previous = parent.Assessment, current });
        var same = ContentFingerprint.Compute(parent.Assessment) == ContentFingerprint.Compute(current);
        history.Add(new
        {
            name,
            seed,
            sourcePath = Path.GetRelativePath(root, path).Replace('\\', '/'),
            sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
            source.TopologyFingerprint,
            source.OpticFingerprint,
            current.ProtocolFingerprint,
            current.Completed,
            current.MeetsTargets,
            current.TracedRayCount,
            sameAssessment = same
        });
        Require(current.Completed && same, "Original complete CPU assessment differs from frozen M2 evidence.");
    }

    var parentSource = Parent();
    var transition = SpcTransitionCompiler.Create(parentSource, "crown", "null-" + seed);
    var epsilon = .0005 + .000125 * (seed - 42);
    foreach (var offset in new[] { 0d, epsilon, -epsilon })
    {
        using var rays = SequentialTraceMeasurement.Begin();
        var state = transition with { NullCurvatureOffsetPerMillimeter = offset };
        var source = SpcTransitionCompiler.Compile(state);
        var optic = source.CreateOptic();
        var comparison = parentSource.Topology.Wavelengths.Select(wave =>
        {
            var baseline = parentSource.CreateOptic().Paraxial.GetTransferMatrix(1, parentSource.SurfaceIndices["image"], wave.Nanometers);
            var current = optic.Paraxial.GetTransferMatrix(source.ReferenceIndex, source.ImageIndex, wave.Nanometers);
            var error = new[] { Math.Abs(baseline.A - current.A), Math.Abs(baseline.B - current.B), Math.Abs(baseline.C - current.C), Math.Abs(baseline.D - current.D) }.Max();
            Require(error < 1e-11, "Null-pair matrix preservation failed.");
            return new { wave.Nanometers, baseline, current, maximumAbsoluteMatrixDifference = error };
        }).ToArray();
        OpticalDesignParameter[] parameters = [new(source.NullFrontIndex, OpticalDesignParameterKind.Curvature), new(source.NullBackIndex, OpticalDesignParameterKind.Curvature)];
        var coordinates = OpticalDesignParameters.Read(optic, parameters);
        var ad = DifferentiableParaxialMetrics.TransferMatrix(optic, parameters, source.ReferenceIndex, source.ImageIndex);
        var derivativeErrors = new List<object>();
        for (var column = 0; column < parameters.Length; column++)
        {
            const double h = 1e-6;
            var plus = coordinates.ToArray(); var minus = coordinates.ToArray(); plus[column] += h; minus[column] -= h;
            var p = OpticalDesignParameters.Apply(optic.ToSnapshot(), parameters, plus).Paraxial.GetTransferMatrix(source.ReferenceIndex, source.ImageIndex);
            var m = OpticalDesignParameters.Apply(optic.ToSnapshot(), parameters, minus).Paraxial.GetTransferMatrix(source.ReferenceIndex, source.ImageIndex);
            var aError = Math.Abs(ad.A.Derivative(column) - (p.A - m.A) / (2 * h));
            var cError = Math.Abs(ad.C.Derivative(column) - (p.C - m.C) / (2 * h));
            Require(aError < 2e-8 && cError < 2e-9, "Null-interface derivative differs from central finite differences.");
            derivativeErrors.Add(new { column, aError, cError, aDerivative = ad.A.Derivative(column), cDerivative = ad.C.Derivative(column) });
        }
        var rejected = false;
        try { _ = TopologyCompiler.Import(optic.ToSnapshot()); }
        catch (TopologyValidationException) { rejected = true; }
        Require(rejected && rays.RayCount == 0, "Experimental transition entered ordinary acceptance or traced physical rays.");
        var sign = offset == 0 ? "zero" : offset > 0 ? "plus" : "minus";
        var stateFile = Path.Combine(output, $"seed{seed}-{sign}.spc-transition.json");
        await SpcTransitionFiles.SaveAsync(state, stateFile);
        var restored = await SpcTransitionFiles.LoadAsync(stateFile);
        Require(source.Fingerprint == SpcTransitionCompiler.Compile(restored).Fingerprint, "Saved experimental transition changed.");
        nulls.Add(new { seed, offset, source.Fingerprint, comparisons = comparison, derivativeErrors, ordinaryImportRejected = rejected, tracedRays = rays.RayCount });
    }
    foreach (var gap in new[] { 0d, .5 })
    {
        var source = SpcTransitionCompiler.Materialize(transition, .5 + .125 * (seed - 42), gap, "N-F2");
        Require(source.Topology.Stop == parentSource.Topology.Stop && source.Topology.Image == parentSource.Topology.Image
            && source.Topology.Reference == parentSource.Topology.Reference, "Original planes changed.");
        Require(ContentFingerprint.Compute(source.Topology.Elements[0]) == ContentFingerprint.Compute(parentSource.Topology.Elements[0]), "Original lens changed.");
        var targets = new TopologyTargets();
        var assessment = TopologyValidator.Validate(source, targets);
        Require(assessment.Completed && assessment.Targets == targets && assessment.StructuralConstraints == parentSource.Constraints, "Ordinary full physical/manufacturing assessment did not complete.");
        var name = $"seed{seed}-" + (gap == 0 ? "cemented" : "air");
        Write(name + ".physical.json", new { source.Topology, source.Constraints, source.Materials, source.TopologyFingerprint, source.OpticFingerprint, assessment });
        physical.Add(new
        {
            seed,
            gap,
            name,
            assessment.Completed,
            assessment.MeetsTargets,
            assessment.TracedRayCount,
            violations = assessment.Violations.Length,
            assessment.ProtocolFingerprint,
            worstRmsMillimeters = assessment.Fields.Max(field => field.RmsRadiusMillimeters),
            worstMaximumMillimeters = assessment.Fields.Max(field => field.MaximumRadiusMillimeters)
        });
    }
    Console.WriteLine($"Seed {seed}: historical replay 3, null states 3, ordinary materializations 2 completed.");
}
Write("verification.json", new { Identity = "initial-structure-m3-1-foundation-verification-v1", historical = history, nullStates = nulls, ordinaryMaterializations = physical });

void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, json));
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
static CompiledTopology Parent() => TopologyCompiler.Compile(new()
{
    Name = "Authored SPC foundation fixture",
    Stop = new("stop", 1, 2),
    Image = new("image", 55),
    Fields = [new("Axis", 0, 0), new("Edge", 0, 3)],
    Elements = [new() { Id = "crown", FrontSurfaceId = "front", BackSurfaceId = "common", FrontZMillimeters = 3, ThicknessMillimeters = 3, Material = "N-BK7",
        Front = new() { ClearSemiDiameterMillimeters = 5, Profile = new() { CurvaturePerMillimeter = 1d / 40 } },
        Back = new() { ClearSemiDiameterMillimeters = 5, Profile = new() { CurvaturePerMillimeter = -1d / 30 } } }]
});
