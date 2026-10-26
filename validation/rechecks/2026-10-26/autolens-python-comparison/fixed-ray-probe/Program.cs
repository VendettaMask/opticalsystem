// Development-only direct comparison with unchanged upstream Python output.
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
var source = document.RootElement;
var spec = AutoLensReferenceExamples.Mobile().Specification;
var optic = AutoLensReferenceStart.Create(spec, 6, null, AutoLensReferenceMaterials.ProjectionNames);
var parameters = source.GetProperty("parameters").EnumerateArray().Select(row =>
    new OpticalDesignParameter(row.GetProperty("surface").GetInt32(),
        Enum.Parse<OpticalDesignParameterKind>(row.GetProperty("kind").GetString()!))
    { OriginOffset = row.GetProperty("kind").GetString() == "AxialPosition" ? AutoLensReferenceStart.ObjectDistance : 0 }).ToArray();
var inputFields = source.GetProperty("fields").EnumerateArray().ToArray();
var inputs = new FixedInputRaySet(inputFields.Select(field => new FixedInputRayField(
    field.GetProperty("ring").GetInt32(), field.GetProperty("arm").GetInt32(), field.GetProperty("wave").GetInt32(),
    field.GetProperty("ideal")[0].GetDouble(), field.GetProperty("ideal")[1].GetDouble(),
    field.GetProperty("inputs").EnumerateArray().Select(ray =>
    {
        var origin = ray.GetProperty("origin");
        var direction = ray.GetProperty("direction");
        return new RealRay(new(origin[0].GetDouble(), origin[1].GetDouble(), origin[2].GetDouble() + 20000),
            new(direction[0].GetDouble(), direction[1].GetDouble(), direction[2].GetDouble()),
            new[] { 656.0, 587, 486 }[field.GetProperty("wave").GetInt32()]);
    }).ToArray())).ToArray());
var results = new List<object>();
foreach (var mode in new[] { DesignComputeMode.Cpu, DesignComputeMode.Gpu })
{
    if (mode == DesignComputeMode.Gpu && !DesignGpu.Status.IsAvailable) continue;
    var traced = FixedInputOpticalMetrics.Evaluate(optic, parameters, inputs, Math.Cos(20 * Math.PI / 180), mode);
    var mismatches = new List<object>();
    var errors = new List<double>();
    var validCount = 0;
    for (var fieldIndex = 0; fieldIndex < inputFields.Length; fieldIndex++)
    {
        var expected = inputFields[fieldIndex].GetProperty("outputs").EnumerateArray().ToArray();
        for (var rayIndex = 0; rayIndex < expected.Length; rayIndex++)
        {
            var actual = traced.Fields[fieldIndex].Rays[rayIndex];
            var original = expected[rayIndex];
            if (actual.Valid != original.GetProperty("valid").GetBoolean())
                mismatches.Add(new { fieldIndex, rayIndex, actual = actual.Valid, upstream = original.GetProperty("valid").GetBoolean() });
            if (!actual.Valid || !original.GetProperty("valid").GetBoolean()) continue;
            validCount++;
            errors.Add(Math.Max(Math.Abs(actual.X.Value - original.GetProperty("position")[0].GetDouble()),
                Math.Abs(actual.Y.Value - original.GetProperty("position")[1].GetDouble())));
        }
    }
    var losses = source.GetProperty("imageCases").EnumerateArray().Select(expected =>
    {
        var centroid = expected.GetProperty("centroid").GetBoolean();
        var actual = AutoLensReferenceLoss.Image(traced, centroid);
        var gradient = Enumerable.Range(0, parameters.Length).Select(actual.Derivative).ToArray();
        var expectedGradient = expected.GetProperty("gradient").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        return new { centroid, upstream = expected.GetProperty("loss").GetDouble(), actual = actual.Value,
            absoluteError = Math.Abs(actual.Value - expected.GetProperty("loss").GetDouble()),
            upstreamGradient = expectedGradient, gradient,
            maximumGradientAbsoluteError = gradient.Zip(expectedGradient).Max(p => Math.Abs(p.First - p.Second)) };
    }).ToArray();
    results.Add(new { mode = mode.ToString(), rays = inputs.Fields.Sum(f => f.Rays.Count), validCount,
        validityMismatches = mismatches, maximumPositionAbsoluteErrorMm = errors.Max(), losses,
        execution = traced.Execution });
}
var report = JsonSerializer.Serialize(new { scope = "Six-element initial lens; identical fixed rays and float64; not final optimized output", results },
    new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(args[1], report + Environment.NewLine);
Console.WriteLine(report);
