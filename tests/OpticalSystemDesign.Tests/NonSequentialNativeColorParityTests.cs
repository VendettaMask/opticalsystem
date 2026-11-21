using System.Numerics;
using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.NonSequential;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialNativeColorParityTests
{
    private static string NativeFile(string file) => Path.Combine(NonSequentialColorFringesTests.Root,
        "validation/rechecks/2026-11-21/nsc-color-parity/native", file);

    [Fact]
    public void SourceSpectrumSamplingAndCrystalBranchSelectionMatchTheNativePrescription()
    {
        var doc = NonSequentialColorFringesTests.Import().Document!;
        var source = (SourceParameters)doc.Objects[0].Parameters;
        Assert.True(source.Physics!.UseSobolSampling);
        Assert.All(doc.Objects.Skip(1).Take(2), o => Assert.False(o.Parameters.Waveplate!.TraceReflections));
        var reference = JsonSerializer.Deserialize<double[][]>(File.ReadAllText(NativeFile("source-spectrum.json")))!;
        Assert.Equal(20, reference.Length);
        for (var i = 0; i < 20; i++)
        {
            Assert.InRange(Math.Abs(source.Physics.Spectrum![i].Nanometers / 1000 - reference[i][0]), 0, 5.1e-7);
            // The reference uses a legacy interpolated observer; its small
            // difference from the original CIE 1 nm table is retained explicitly.
            Assert.InRange(Math.Abs(source.Physics.Spectrum[i].RelativePower - reference[i][1]), 0, 1e-5);
        }
    }

    [Theory]
    [InlineData(-3)] [InlineData(0)] [InlineData(3)]
    public void SharedPlaneJonesPhaseAndSelectedPowerMatchNativeSingleRayControls(double position)
    {
        using var data = JsonDocument.Parse(File.ReadAllText(NativeFile("ray-controls.json")));
        var rows = data.RootElement.EnumerateArray().Where(r => r.GetProperty("position").GetDouble() == position && r.GetProperty("plateLength").GetDouble() == .638).ToArray();
        var native = rows.Single(r => r.GetProperty("obj").GetInt32() == 4);
        var shared = rows.Single(r => r.GetProperty("obj").GetInt32() == 3 && r.GetProperty("face").GetInt32() == 1);
        double N(string name) => native.GetProperty(name).GetDouble();
        var ex = new Complex(N("exr"), N("exi")); var ey = new Complex(N("eyr"), N("eyi"));
        var doc = NonSequentialColorFringesTests.Import().Document!; var source = doc.Objects[0];
        var physics = ((SourceParameters)source.Parameters).Physics!;
        doc.Replace(source.Id, source with { Kind = NonSequentialObjectKind.SourceRay,
            LocalCoordinateSystem = new(new(0, position, -10), RotationZDegrees: 45),
            Parameters = new SourceRayParameters { Physics = physics with { Spectrum = null } } });
        var trace = new NonSequentialDocumentTracer().Trace(doc, new());
        var segments = trace.Branches.SelectMany(b => b.Segments).ToArray();
        Assert.Single(trace.Branches);
        Assert.Equal(1, trace.Detectors.Single().HitCountByWavelength!.Values.Sum(v => v.Sum()));
        var intersection = segments.First(s => s.End.Z > .5 && s.End.Z < .7);
        Assert.InRange(Math.Abs(intersection.End.Z - shared.GetProperty("z").GetDouble()), 0, 1e-7);
        var detector = segments.Single(s => s.ObjectId == doc.Objects[3].Id);
        var field = detector.IncidentField!.State.First.Field;
        var cross = field.X * Complex.Conjugate(field.Y);
        Assert.InRange(Math.Abs(2 * cross.Real - 2 * (ex * Complex.Conjugate(ey)).Real / N("power")), 0, 2e-7);
        // The two engines use opposite time-phase conventions.
        Assert.InRange(Math.Abs(2 * cross.Imaginary + 2 * (ex * Complex.Conjugate(ey)).Imaginary / N("power")), 0, 2e-7);
        // Native compressed ray fields are float32. The common-path internal
        // boundary's residual flux difference is 6.0e-7 W on a one-watt control.
        Assert.InRange(Math.Abs(trace.Detectors.Single().TotalPowerWatts - (ex + ey).SquaredMagnitude() / 2), 0, 1e-6);
        Assert.InRange(Math.Abs(trace.EnergyBalance.AccountedPowerWatts - 1), 0, 2e-12);
    }

    [Fact]
    public void NativeTrueColorRgbIsRecoveredFromItsActualTristimulusData()
    {
        using var source = JsonDocument.Parse(File.ReadAllText(NativeFile("detector.json")));
        using var image = JsonDocument.Parse(File.ReadAllText(NativeFile("viewer-rgb.json")));
        var xyz = new double[30000]; var expected = new double[30000];
        for (var c = 0; c < 3; c++)
        {
            var values = source.RootElement.GetProperty(new[] { "x", "y", "z" }[c]).Deserialize<double[]>()!;
            var rgb = image.RootElement.GetProperty(new[] { "r", "g", "b" }[c]).Deserialize<double[]>()!;
            for (var i = 0; i < 10000; i++) { xyz[i * 3 + c] = values[i]; expected[i * 3 + c] = rgb[i]; }
        }
        var converted = NonSequentialColorimetry.TristimulusRgb(xyz);
        var errors = converted.Zip(expected, (a, b) => Math.Abs(a * 255 - b)).ToArray();
        Assert.InRange(errors.Max(), 0, 1.2);
        Assert.InRange(Math.Sqrt(errors.Average(v => v * v)), 0, .7);
    }

    [Fact]
    public void SobolDirectionsHaveKnownPointsAndDyadicStratification()
    {
        var generator = new NonSequentialSobolSource(1);
        var expected = new[] { new[] { .5, .5, .5 }, new[] { .75, .25, .25 }, new[] { .25, .75, .75 }, new[] { .375, .375, .625 } };
        for (var i = 0; i < expected.Length; i++) { generator.BeginRay(i); Assert.Equal(expected[i], Enumerable.Range(0, 3).Select(_ => generator.NextDouble())); }
        var bins = new int[3, 1024];
        for (var i = 0; i < 1023; i++) { generator.BeginRay(i); for (var d = 0; d < 3; d++) bins[d, (int)(generator.NextDouble() * 1024)]++; }
        // The omitted all-zero point fills the first interval of each axis.
        for (var d = 0; d < 3; d++) { bins[d, 0]++; for (var i = 0; i < 1024; i++) Assert.Equal(1, bins[d, i]); }
    }
}

internal static class NativeColorComplexExtensions
{
    internal static double SquaredMagnitude(this Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;
}
