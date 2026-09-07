using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class FlatStartZemaxReferenceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GeneratedSphericalRepresentativeMatchesFrozenZemax2026R1DenseRays(int elements)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OptilandWorkbench.slnx"))) root = root.Parent;
        var evidence = Path.Combine(root!.FullName, "validation/zemax-2026-r1/initial-structure-20260907");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "manifest.json")));
        var folder = Path.Combine(evidence, $"{elements}-element");
        foreach (var name in new[] { "candidate.json", "candidate.ZMX", "native-rays.json" })
        {
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, name)))).ToLowerInvariant();
            Assert.Equal(manifest.RootElement.GetProperty("Files").GetProperty($"{elements}-element/{name}").GetString(), actual);
        }
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var candidate = JsonSerializer.Deserialize<CandidateSnapshot>(File.ReadAllText(Path.Combine(folder, "candidate.json")), options)!;
        var optic = Optic.FromSnapshot(candidate.Optic);
        using var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "native-rays.json")));
        var native = capture.RootElement;
        Assert.Equal(26, native.GetProperty("Major").GetInt32());
        Assert.Equal(1, native.GetProperty("Minor").GetInt32());
        Assert.Equal(260127, native.GetProperty("Version").GetInt32());
        Assert.Equal("mm", native.GetProperty("Unit").GetString());
        Assert.Equal(1, native.GetProperty("WavelengthCount").GetInt32());
        Assert.Equal(optic.Wavelengths[0].Nanometers / 1000, native.GetProperty("WavelengthMicrometers").GetDouble());
        Assert.All(candidate.FlatRootOptic.Surfaces, surface => Assert.Equal(0, surface.Radius));
        Assert.Equal(elements, candidate.Lineage.ElementCount);
        Assert.InRange(Math.Abs(optic.Paraxial.EstimateEffectiveFocalLength() - native.GetProperty("EFL").GetDouble()), 0, 1e-8);
        var inputs = native.GetProperty("Inputs").EnumerateArray().ToArray();
        var rays = native.GetProperty("Rays").EnumerateArray().ToArray();
        Assert.Equal(582, inputs.Length);
        Assert.Equal(inputs.Length, rays.Length);
        foreach (var field in new[] { 0, .25, .5, .7, .85, 1 })
        {
            var indices = Enumerable.Range(0, inputs.Length).Where(index => inputs[index][0].GetDouble() == field).ToArray();
            Assert.Equal(97, indices.Length);
            var pupils = indices.Select(index => new PupilSample(inputs[index][1].GetDouble(), inputs[index][2].GetDouble(), 1)).ToArray();
            var result = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, field, pupils, 1,
                reference: "absolute", includeSurfaceTransmission: false);
            Assert.Equal(0, result.VignettedRayCount);
            Assert.Equal(97, result.RayCount);
            for (var index = 0; index < indices.Length; index++)
            {
                var ray = rays[indices[index]];
                Assert.Equal(0, ray.GetProperty("Error").GetInt32());
                Assert.Equal(0, ray.GetProperty("Vignette").GetInt32());
                var actual = result.Wavelengths[0].Rays[index];
                Assert.InRange(Math.Abs(actual.X - ray.GetProperty("X").GetDouble()), 0, 1e-8);
                Assert.InRange(Math.Abs(actual.Y - ray.GetProperty("Y").GetDouble()), 0, 1e-8);
            }
        }
    }
}
