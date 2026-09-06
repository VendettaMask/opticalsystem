using System.Globalization;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Visualization;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxApodizationImportTests
{
    [Theory]
    [InlineData(0, 2.75)]
    [InlineData(1, 2.75)]
    [InlineData(1, 9)]
    [InlineData(1, 0.12345678901234567)]
    [InlineData(1, 0)]
    [InlineData(2, 2.75)]
    public void ImportPreservesTypeAndFactorThroughSnapshotAndZmx(int type, double factor)
    {
        var optic = Import($"GFAC {factor.ToString("R", CultureInfo.InvariantCulture)} {type}");
        AssertApodization(optic, type, factor);
        AssertApodization(Optic.FromSnapshot(optic.ToSnapshot()), type, factor);
        AssertApodization(OpticalFormatCatalog.Import(new ZemaxZmxExporter().Export(optic), ".zmx"), type, factor);
        var clone = Assert.IsType<ZemaxApodization>(optic.Apodization!.Clone());
        Assert.Equal((ZemaxApodizationType)type, clone.Type);
        Assert.Equal(factor, clone.Factor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2.75)]
    public void GaussianFactorControlsRayIntensityUsingAmplitudeConvention(double factor)
    {
        var optic = Import($"GFAC {factor.ToString("R", CultureInfo.InvariantCulture)} 1");
        var rays = optic.SequentialRayTracer.RayGenerator;
        Assert.Equal(1, rays.GenerateGeneric(0, 0, 0, 0, 0.55).Rays.Single().Intensity, 12);
        Assert.Equal(Math.Exp(-2 * factor), rays.GenerateGeneric(0, 0, 0, 1, 0.55).Rays.Single().Intensity, 12);
        Assert.Equal(Math.Exp(-0.5 * factor), rays.GenerateGeneric(0, 0, 0.3, 0.4, 0.55).Rays.Single().Intensity, 12);
    }

    [Fact]
    public void NativeGaussianSigmaKeepsItsOriginalMeaningWhenExported()
    {
        var optic = Import("GFAC 0 0");
        optic.Apodization = new GaussianApodization(0.7);
        var restored = OpticalFormatCatalog.Import(new ZemaxZmxExporter().Export(optic), ".zmx");
        Assert.Equal(optic.Apodization.Intensity(0, 1), restored.Apodization!.Intensity(0, 1), 12);
    }

    [Fact]
    public void CosineCubedUsesPupilDistanceAndUpdatesAfterObjectDistanceChanges()
    {
        var optic = Import("GFAC 7 2", objectDistance: "10");
        foreach (var distance in new[] { 10.0, 20.0 })
        {
            optic.SurfaceGroup.Items[0].Thickness = distance;
            optic.SurfaceGroup.Renumber();
            var pupilDistance = optic.Paraxial.EstimateEntrancePupilLocation()
                - optic.SurfaceGroup.Items[0].CoordinateSystem.Origin.Z;
            var slope = optic.Paraxial.EstimateEntrancePupilDiameter() / (2 * pupilDistance);
            var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 1, 0.55).Rays.Single();
            Assert.Equal(Math.Pow(1 + slope * slope, -1.5), ray.Intensity, 12);
        }

        var infinite = Import("GFAC 7 2");
        Assert.Equal(1, infinite.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 1, 0.55).Rays.Single().Intensity);
    }

    [Theory]
    [InlineData("GFAC -1 1")]
    [InlineData("GFAC NaN 1")]
    [InlineData("GFAC 1 3")]
    [InlineData("GFAC 1")]
    public void InvalidApodizationDoesNotSilentlyBecomeUniform(string line)
    {
        Assert.ThrowsAny<Exception>(() => Import(line));
    }

    [Theory]
    [InlineData(0, "均匀（Zemax）")]
    [InlineData(1, "高斯（Zemax）")]
    [InlineData(2, "余弦立方（Zemax）")]
    public async Task ApplicationDisplaysEditsAndSavesImportedFactor(int type, string label)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"zemax-apodization-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "input.zmx");
            var saved = Path.Combine(directory, "saved.staropt");
            await File.WriteAllTextAsync(source, new ZemaxZmxExporter().Export(Import($"GFAC 2.75 {type}")));
            using var application = WorkbenchApplication.Create("blank");
            await application.Documents.OpenAsync(source);
            var settings = application.Prescription.GetSystemSettings();
            Assert.Equal(label, settings.ApodizationKind);
            Assert.Equal(2.75, settings.FirstApodizationParameter);
            application.Prescription.UpdateSystemSettings(settings with { FirstApodizationParameter = 0.375 });
            await application.Documents.SaveAsync(saved);
            await application.Documents.OpenAsync(saved);
            var reopened = application.Prescription.GetSystemSettings();
            Assert.Equal(label, reopened.ApodizationKind);
            Assert.Equal(0.375, reopened.FirstApodizationParameter);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LayoutUsesTheSystemRayAimingSetting(bool aiming)
    {
        var optic = Import("GFAC 1 1", internalStop: true);
        optic.RayAimingEnabled = aiming;
        var options = new LayoutBuildOptions(RayCount: 3, DeleteVignetted: false);
        var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0.85, 0.55, aimAtStop: aiming).Rays.Single();
        var unAimed = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0.85, 0.55, aimAtStop: false).Rays.Single();
        var path = new Layout2DBuilder(optic).Build(options: options).Rays.Single(path => path.PupilIndex == 2);
        Assert.Equal(ray.Origin.Y, path.Points[0].Y, 10);
        if (aiming)
        {
            Assert.True(Math.Abs(ray.Origin.Y - unAimed.Origin.Y) > 1e-6);
        }
    }

    private static void AssertApodization(Optic optic, int type, double factor)
    {
        var apodization = Assert.IsType<ZemaxApodization>(optic.Apodization);
        Assert.Equal((ZemaxApodizationType)type, apodization.Type);
        Assert.Equal(factor, apodization.Factor);
    }

    private static Optic Import(string apodization, string objectDistance = "INFINITY", bool internalStop = false) =>
        OpticalFormatCatalog.Import($$"""
            MODE SEQ
            UNIT MM
            ENPD 2
            {{apodization}}
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.55 1
            PWAV 1
            SURF 0
              DISZ {{objectDistance}}
            SURF 1
              {{(internalStop ? "" : "STOP")}}
              CURV 0.04
              DISZ 3
              GLAS N-BK7
              DIAM 5
            SURF 2
              CURV 0
              DISZ 10
              DIAM 5
            SURF 3
              {{(internalStop ? "STOP" : "")}}
              DISZ 20
              DIAM 1
            SURF 4
              DISZ 0
              DIAM 10
            """, ".zmx");
}
