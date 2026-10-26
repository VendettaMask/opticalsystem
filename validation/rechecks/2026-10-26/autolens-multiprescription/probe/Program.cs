// External validation executable only. All C# physics/loss/derivatives call the product.
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
if (args.Length == 4 && args[0] == "assess")
{
    Environment.ExitCode = EndpointComparison.Run(args[1], args[2], args[3]);
    return;
}
using var source = JsonDocument.Parse(File.ReadAllText(args[0]));
var spec = AutoLensReferenceExamples.Mobile().Specification;
var reports = new List<object>();
var allClock = Stopwatch.StartNew();
foreach (var item in source.RootElement.GetProperty("cases").EnumerateArray())
{
    var name = item.GetProperty("name").GetString();
    if (item.TryGetProperty("error", out var upstreamError))
    {
        reports.Add(new { name, upstreamError = upstreamError.GetString() });
        continue;
    }
    var optic = Import(item);
    var parameters = item.GetProperty("parameters").EnumerateArray().Select(row => new OpticalDesignParameter(
        row.GetProperty("surface").GetInt32(), Enum.Parse<OpticalDesignParameterKind>(row.GetProperty("kind").GetString()!),
        CoefficientIndex: row.GetProperty("coefficient").GetInt32())
    { OriginOffset = row.GetProperty("kind").GetString() == "AxialPosition" ? 20000 : 0 }).ToArray();
    var productParameters = AutoLensReferenceStart.Parameters(optic, spec, item.GetProperty("optimizeMaterials").GetBoolean());
    if (!parameters.SequenceEqual(productParameters)) throw new InvalidDataException("External all-parameter map differs from product parameter order: " + name);
    var inputs = Inputs(item.GetProperty("fields"));
    var craInputs = Inputs(item.GetProperty("craFields"));
    var bendInputs = Inputs(item.GetProperty("bendFields"));
    var expected = item.GetProperty("losses").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
    foreach (var mode in new[] { DesignComputeMode.Cpu, DesignComputeMode.Gpu })
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var trace = FixedInputOpticalMetrics.Evaluate(optic, parameters, inputs, item.GetProperty("bendCosine").GetDouble(), mode);
            var cra = FixedInputOpticalMetrics.Evaluate(optic, parameters, craInputs, item.GetProperty("bendCosine").GetDouble(), mode);
            var bend = FixedInputOpticalMetrics.Evaluate(optic, parameters, bendInputs, item.GetProperty("bendCosine").GetDouble(), mode);
            var rows = item.GetProperty("fields").EnumerateArray().ToArray();
            var maskMismatches = new List<object>();
            var outputErrors = new double[4];
            var actualValid = 0;
            var expectedValid = 0;
            var outputMismatchCount = 0;
            for (var fi = 0; fi < rows.Length; fi++)
            {
                var outputs = rows[fi].GetProperty("outputs").EnumerateArray().ToArray();
                for (var ri = 0; ri < outputs.Length; ri++)
                {
                    var original = outputs[ri];
                    var actual = trace.Fields[fi].Rays[ri];
                    var valid = original.GetProperty("valid").GetBoolean();
                    if (actual.Valid) actualValid++;
                    if (valid) expectedValid++;
                    if (actual.Valid != valid) maskMismatches.Add(new { field = fi, ray = ri, expected = valid, actual = actual.Valid });
                    if (!valid || !actual.Valid) continue;
                    var values = new[] { actual.X.Value, actual.Y.Value, actual.DirectionZ.Value, actual.BendPenalty.Value };
                    var keys = new[] { "x", "y", "directionZ", "bend" };
                    for (var oi = 0; oi < 4; oi++)
                    {
                        var target = original.GetProperty(keys[oi]).GetDouble();
                        var error = Math.Abs(values[oi] - target);
                        outputErrors[oi] = Math.Max(outputErrors[oi], error);
                        if (!double.IsFinite(values[oi]) || error > 5e-8 * Math.Max(1, Math.Abs(target))) outputMismatchCount++;
                    }
                }
            }
            var probes = new Dictionary<string, DifferentialScalar>();
            for (var output = 0; output < 4; output++) probes[new[] { "traceX", "traceY", "traceDirectionZ", "traceBend" }[output]] = Weighted(trace, rows, output);
            probes["image-pinhole"] = AutoLensReferenceLoss.Image(trace, false);
            probes["image-centroid"] = AutoLensReferenceLoss.Image(trace, true);
            probes["geometry"] = AutoLensReferenceLoss.Geometry(optic, parameters, true, item.GetProperty("optimizeMaterials").GetBoolean());
            probes["ray-regularization"] = AutoLensReferenceLoss.RayRegularization(cra, bend, item.GetProperty("craCosine").GetDouble());
            probes["total-pinhole"] = probes["image-pinhole"] + .1 * (probes["geometry"] + probes["ray-regularization"]);
            probes["total-centroid"] = probes["image-centroid"] + .1 * (probes["geometry"] + probes["ray-regularization"]);
            var comparisons = probes.Select(pair => Compare(pair.Key, pair.Value, expected[pair.Key], optic, parameters)).ToArray();
            var finiteDifferences = mode == DesignComputeMode.Cpu && !item.GetProperty("boundary").GetBoolean()
                ? FiniteDifference(optic, parameters, inputs, rows, item.GetProperty("bendCosine").GetDouble(), probes) : [];
            var report = new { name, mode = mode.ToString(), boundary = item.GetProperty("boundary").GetBoolean(),
                parameters = parameters.Length, parameterKinds = parameters.GroupBy(p => p.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                rays = inputs.Fields.Sum(f => f.Rays.Count), actualValid, expectedValid, maskMismatchCount = maskMismatches.Count,
                maskMismatches, outputErrors, outputMismatchCount, comparisons, finiteDifferences,
                execution = trace.Execution, craExecution = cra.Execution, bendExecution = bend.Execution,
                seconds = clock.Elapsed.TotalSeconds };
            reports.Add(report);
            Console.WriteLine($"{name} {mode}: masks={maskMismatches.Count} outputs={outputMismatchCount} valid={actualValid}/{expectedValid} params={parameters.Length} {clock.Elapsed.TotalSeconds:F2}s");
        }
        catch (Exception error)
        {
            reports.Add(new { name, mode = mode.ToString(), boundary = item.GetProperty("boundary").GetBoolean(), error = error.ToString(), seconds = clock.Elapsed.TotalSeconds });
            Console.WriteLine($"{name} {mode}: {error.GetType().Name}: {error.Message}");
        }
    }
}
var assemblies = new[] { typeof(Optic).Assembly, typeof(FlatStartDesignService).Assembly, Assembly.GetExecutingAssembly() };
File.WriteAllText(args[1], JsonSerializer.Serialize(new { scope = "Direct fixed-input upstream vs current product CPU/CUDA, all optimizer coordinates, no training updates",
    createdUtc = DateTimeOffset.UtcNow, seconds = allClock.Elapsed.TotalSeconds, gpu = DesignGpu.Status,
    inputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))),
    binaries = assemblies.ToDictionary(a => Path.GetFileName(a.Location), a => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location)))), reports }, json) + Environment.NewLine);

Optic Import(JsonElement item)
{
    var input = item.GetProperty("surfaces").EnumerateArray().ToArray();
    var optic = AutoLensReferenceStart.Create(spec, (input.Length - 1) / 2, null, AutoLensReferenceMaterials.ProjectionNames);
    var surfaces = new List<OpticalSurface> { new() { Label = "Object", Thickness = 20000, Material = "Air" } };
    for (var i = 0; i < input.Length; i++)
    {
        var row = input[i];
        var originalMaterial = row.GetProperty("material").GetString()!;
        var material = originalMaterial == "air" ? "Air" : "AutoLensE58:" + originalMaterial;
        if (originalMaterial != "air" && item.GetProperty("optimizeMaterials").GetBoolean())
        {
            material = "ProbeRelaxed-" + i;
            optic.Materials.Register(OpticalDesignParameters.CreateRelaxedGlass(material, row.GetProperty("nd").GetDouble(), row.GetProperty("abbe").GetDouble()));
        }
        var next = i + 1 < input.Length ? input[i + 1].GetProperty("d").GetDouble() : item.GetProperty("sensor").GetDouble();
        surfaces.Add(new() { Label = i == 0 ? "Aperture" : "Aspheric", IsStop = i == 0, Material = material,
            SemiDiameter = row.GetProperty("radius").GetDouble(), Thickness = next - row.GetProperty("d").GetDouble() });
    }
    surfaces.Add(new() { Label = "Image", Material = "Air" });
    optic.SurfaceGroup.ImportLegacySurfaces(surfaces, optic.Materials);
    // Upstream r is an actual clipping aperture, not only a displayed/mechanical semi-diameter.
    for (var i = 0; i < input.Length; i++) RotationalDesignGeometry.SetRadius(optic, i + 1, input[i].GetProperty("radius").GetDouble());
    for (var i = 1; i < input.Length; i++)
    {
        var row = input[i];
        var curvature = row.GetProperty("curvature").GetDouble();
        optic.SurfaceGroup.Items[i + 1].Geometry = new EvenAsphereGeometry(curvature == 0 ? 0 : 1 / curvature,
            row.GetProperty("conic").GetDouble(), new[] { 0.0 }.Concat(row.GetProperty("ai").EnumerateArray().Select(v => v.GetDouble())).ToArray());
    }
    optic.SurfaceGroup.Items[0].PhysicalAperture = null;
    optic.SurfaceGroup.Items[^1].PhysicalAperture = null;
    OpticalDesignParameters.SynchronizeIncidentMedia(optic);
    optic.SurfaceGroup.Renumber();
    return optic;
}

static FixedInputRaySet Inputs(JsonElement fields) => new(fields.EnumerateArray().Select(field => new FixedInputRayField(
    field.GetProperty("ring").GetInt32(), field.GetProperty("arm").GetInt32(), field.GetProperty("wave").GetInt32(),
    field.GetProperty("ideal")[0].GetDouble(), field.GetProperty("ideal")[1].GetDouble(),
    field.GetProperty("inputs").EnumerateArray().Select(ray =>
    {
        var o = ray.GetProperty("origin");
        var d = ray.GetProperty("direction");
        return new RealRay(new(o[0].GetDouble(), o[1].GetDouble(), o[2].GetDouble() + 20000),
            new(d[0].GetDouble(), d[1].GetDouble(), d[2].GetDouble()), new[] { 656.0, 587, 486 }[field.GetProperty("wave").GetInt32()]);
    }).ToArray())).ToArray());

static DifferentialScalar Weighted(FixedInputOpticalResult trace, JsonElement[] fields, int output)
{
    DifferentialScalar sum = 0;
    for (var fi = 0; fi < trace.Fields.Count; fi++)
        for (var ri = 0; ri < trace.Fields[fi].Rays.Count; ri++)
        {
            var ray = trace.Fields[fi].Rays[ri];
            if (ray.Valid) sum += fields[fi].GetProperty("weights")[ri].GetDouble() * (output switch { 0 => ray.X, 1 => ray.Y, 2 => ray.DirectionZ, _ => ray.BendPenalty });
        }
    return sum;
}

static double Characteristic(Optic optic, OpticalDesignParameter parameter)
{
    var radius = Math.Max(1, optic.SurfaceGroup.Items[parameter.SurfaceIndex].SemiDiameter);
    return parameter.Kind switch { OpticalDesignParameterKind.Curvature => 1 / radius,
        OpticalDesignParameterKind.EvenAsphereCoefficient => 1 / Math.Pow(radius, 2 * (parameter.CoefficientIndex + 1)),
        OpticalDesignParameterKind.MaterialAbbe => 50, _ => 1 };
}

static object Compare(string name, DifferentialScalar actual, JsonElement expected, Optic optic, OpticalDesignParameter[] parameters)
{
    var value = expected.GetProperty("value").GetDouble();
    var target = expected.GetProperty("gradient").EnumerateArray().Select(v => v.GetDouble()).ToArray();
    var gradients = parameters.Select((p, i) => new { slot = i, p.SurfaceIndex, kind = p.Kind.ToString(), p.CoefficientIndex,
        upstream = target[i], actual = actual.Derivative(i), absoluteError = Math.Abs(actual.Derivative(i) - target[i]),
        normalizedError = Math.Abs(actual.Derivative(i) - target[i]) / Math.Max(1, Math.Abs(target[i])),
        scaledError = Math.Abs(actual.Derivative(i) - target[i]) * Characteristic(optic, p) }).ToArray();
    return new { name, expected = value, actual = actual.Value, valueAbsoluteError = Math.Abs(actual.Value - value),
        valueNormalizedError = Math.Abs(actual.Value - value) / Math.Max(1, Math.Abs(value)),
        maximumGradientAbsoluteError = gradients.Max(g => g.absoluteError), maximumGradientNormalizedError = gradients.Max(g => g.normalizedError),
        maximumGradientScaledError = gradients.Max(g => g.scaledError), gradientMismatchCount = gradients.Count(g => !double.IsFinite(g.actual) || g.normalizedError > 5e-6),
        upstreamNonfiniteRawGradientSlots = expected.GetProperty("nonfiniteRawGradientSlots").EnumerateArray().Select(v => v.GetInt32()).ToArray(),
        gradients, worstGradients = gradients.OrderByDescending(g => g.normalizedError).Take(8).ToArray() };
}

static object[] FiniteDifference(Optic optic, OpticalDesignParameter[] parameters, FixedInputRaySet inputs, JsonElement[] fields,
    double gate, IReadOnlyDictionary<string, DifferentialScalar> probes)
{
    var template = optic.ToSnapshot();
    var center = OpticalDesignParameters.Read(optic, parameters);
    var result = new List<object>();
    var analytic = probes["traceX"] + .37 * probes["traceY"] + .1 * probes["traceDirectionZ"] + .01 * probes["traceBend"];
    foreach (var seed in new[] { 17, 71, 101 })
    {
        var random = new Random(seed);
        var delta = parameters.Select(p => (random.NextDouble() > .5 ? 1 : -1) * Characteristic(optic, p) *
            (p.Kind switch { OpticalDesignParameterKind.Curvature => 1e-6, OpticalDesignParameterKind.Conic => 1e-5,
                OpticalDesignParameterKind.MaterialIndex => 1e-6, OpticalDesignParameterKind.MaterialAbbe => 1e-5, _ => 1e-6 })).ToArray();
        var outputs = new List<FixedInputOpticalResult>();
        foreach (var sign in new[] { -1, 1 })
        {
            var perturbed = OpticalDesignParameters.Apply(template, parameters, center.Zip(delta).Select(pair => pair.First + sign * pair.Second).ToArray());
            // The API requires a nonempty parameter map. One image coordinate keeps the
            // nominal finite-difference evaluation small; none of its derivatives is used.
            outputs.Add(FixedInputOpticalMetrics.Evaluate(perturbed, [parameters[^1]], inputs, gate, DesignComputeMode.Cpu));
        }
        var plus = outputs[1];
        var minus = outputs[0];
        var sameMasks = plus.Fields.SelectMany(f => f.Rays).Select(r => r.Valid).SequenceEqual(minus.Fields.SelectMany(f => f.Rays).Select(r => r.Valid));
        double Value(FixedInputOpticalResult trace) => Weighted(trace, fields, 0).Value + .37 * Weighted(trace, fields, 1).Value
            + .1 * Weighted(trace, fields, 2).Value + .01 * Weighted(trace, fields, 3).Value;
        var observed = (Value(plus) - Value(minus)) / 2;
        var expected = delta.Select((v, i) => v * analytic.Derivative(i)).Sum();
        result.Add(new { seed, allParametersPerturbed = delta.Length, sameMasks, predicted = expected, finiteDifference = observed,
            absoluteError = Math.Abs(observed - expected), relativeError = Math.Abs(observed - expected) / Math.Max(1e-8, Math.Max(Math.Abs(observed), Math.Abs(expected))) });
    }
    return result.ToArray();
}
