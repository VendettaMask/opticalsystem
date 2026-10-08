using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class SignedVignettingParityTests
{
    [Theory]
    [InlineData("negative")]
    [InlineData("mixed")]
    [InlineData("asymmetric")]
    [InlineData("finite-tessar")]
    public void OfficialTessarFieldControlsMatchEveryCapturedNativeLaunch(string control)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Validation", "Zemax", "SignedVignetting");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root, control + ".zmx")), ".zmx");
        using var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, control + "-launches.json")));
        var inputs = capture.RootElement.GetProperty("inputs");
        var launches = capture.RootElement.GetProperty("launches");
        var wave = capture.RootElement.GetProperty("wavelength").GetInt32();
        Assert.Equal(128, inputs.GetArrayLength());
        Assert.Equal(inputs.GetArrayLength(), launches.GetArrayLength());
        Assert.False(optic.RayAimingEnabled);
        for (var index = 0; index < inputs.GetArrayLength(); index++)
        {
            var input = inputs[index];
            var expected = launches[index];
            Assert.True(expected.GetProperty("success").GetBoolean());
            var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
                input.GetProperty("hx").GetDouble(), input.GetProperty("hy").GetDouble(),
                input.GetProperty("px").GetDouble(), input.GetProperty("py").GetDouble(),
                optic.Wavelengths[wave - 1].Micrometers, aimAtStop: optic.RayAimingEnabled).Rays.Single();
            // Native global coordinates use the Object surface as Z=0. Map
            // that origin to Core before projecting to the native launch plane.
            // Infinite-object clearance planes can still differ.
            var z = expected.GetProperty("z").GetDouble() + optic.SurfaceGroup.Items[0].CoordinateSystem.Origin.Z;
            var position = ray.Origin + ray.Direction * ((z - ray.Origin.Z) / ray.Direction.Z);
            Close(position.X, expected.GetProperty("x").GetDouble(), 2e-8);
            Close(position.Y, expected.GetProperty("y").GetDouble(), 2e-8);
            Close(ray.Direction.X, expected.GetProperty("l").GetDouble(), 1e-10);
            Close(ray.Direction.Y, expected.GetProperty("m").GetDouble(), 1e-10);
            Close(ray.Direction.Z, expected.GetProperty("n").GetDouble(), 1e-10);
        }
    }

    [Fact]
    public void MirroredOneSidedFieldsAreContinuousAndUseAllFiveMirroredFactors()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Y = 0 });
        optic.Fields.Add(new FieldPoint { Y = 10, VignetteDecenterX = .02, VignetteDecenterY = -.06,
            VignetteFactorX = .05, VignetteFactorY = .09, VignetteAngleDegrees = 12 });
        optic.Fields.Add(new FieldPoint { Y = 25, VignetteDecenterX = .08, VignetteDecenterY = -.1,
            VignetteFactorX = .12, VignetteFactorY = .31, VignetteAngleDegrees = 35 });
        var mirrored = Optic.FromSnapshot(optic.ToSnapshot());
        foreach (var field in mirrored.Fields)
        {
            field.Y = -field.Y;
            field.VignetteDecenterY = -field.VignetteDecenterY;
            field.VignetteAngleDegrees = -field.VignetteAngleDegrees;
        }
        foreach (var hy in new[] { 0, .1, .1999999999, .2000000001, .4, .6, .7, .85, 1 })
        {
            var positive = optic.SequentialRayTracer.RayGenerator.GetPupilVignetting(0, hy);
            var negative = mirrored.SequentialRayTracer.RayGenerator.GetPupilVignetting(0, -hy);
            Assert.Equal(positive.DecenterX, negative.DecenterX);
            Assert.Equal(-positive.DecenterY, negative.DecenterY);
            Assert.Equal(positive.CompressionX, negative.CompressionX);
            Assert.Equal(positive.CompressionY, negative.CompressionY);
            Assert.Equal(-positive.AngleDegrees, negative.AngleDegrees);
        }
        var before = mirrored.SequentialRayTracer.RayGenerator.GetPupilVignetting(0, -.1999999999);
        var after = mirrored.SequentialRayTracer.RayGenerator.GetPupilVignetting(0, -.2000000001);
        Close(before.CompressionY, .0224999999775, 1e-12);
        Close(after.CompressionY, .0225000000225, 1e-12);
    }

    private static void Close(double actual, double expected, double tolerance) =>
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
}
