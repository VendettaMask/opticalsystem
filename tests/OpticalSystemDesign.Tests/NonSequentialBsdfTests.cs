using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialBsdfTests
{
    internal static NonSequentialDocument Scene(double rho, int rays = 8192)
    {
        var source = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourcePoint) with
        { LocalCoordinateSystem = new(new(0, 0, .032), RotationYDegrees: 180), Parameters = new SourcePointParameters(1e-10, 1, 0, 20, rays) };
        var surface = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        { Parameters = new PlaneRectangleParameters(1, 1, NonSequentialSurfaceBehavior.Reflective), SurfaceBsdf = new(NonSequentialBsdfKind.LambertianReflection, rho) };
        var detector = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, .064)), Parameters = new DetectorRectangleParameters(.128, .128, 32, 32) };
        return new("Lambertian control", [new("550 nm", 550, 1, true)], [source, surface, detector]);
    }

    [Theory]
    [InlineData(0)] [InlineData(.25)] [InlineData(1)]
    public void OpaqueSurfaceConservesEnergyAndMatchesTheAnalyticalSquareViewFactor(double rho)
    {
        var scene = Scene(rho); var trace = new NonSequentialDocumentTracer().Trace(scene, Optic.CreateBlank().Materials);
        var e = trace.EnergyBalance;
        Assert.InRange(Math.Abs(e.AccountedPowerWatts / e.SourcePowerWatts - 1), 0, 1e-12);
        Assert.InRange(Math.Abs(e.AbsorbedPowerWatts / e.SourcePowerWatts - (1 - rho)), 0, 1e-12);
        Assert.Equal(0, e.TruncatedPowerWatts);
        // Differential Lambertian patch to a centered square: a/h=b/h=1.
        var viewFactor = 4 / (Math.PI * Math.Sqrt(2)) * Math.Atan(1 / Math.Sqrt(2));
        var expectedFraction = rho * viewFactor;
        var sixSigma = rho * 6 * Math.Sqrt(viewFactor * (1 - viewFactor) / 8192);
        Assert.InRange(Math.Abs(e.DetectorPowerWatts / e.SourcePowerWatts - expectedFraction), 0, sixSigma + 1e-12);
        Assert.InRange(Math.Abs(e.DetectorPowerWatts - Assert.Single(trace.Detectors).TotalPowerWatts) / e.SourcePowerWatts, 0, 1e-12);
        Assert.Equal(8192, trace.BsdfStatistics!.SampleCount);
        if (rho == 0)
        {
            Assert.All(trace.Branches, b => Assert.Equal(NonSequentialTerminationReason.Absorbed, b.TerminationReason));
            var hasReflection = NonSequentialPathFilter.Parse("S2 | R2").Bind(scene);
            Assert.DoesNotContain(trace.Branches, b => hasReflection(b));
        }
        Assert.InRange(Math.Abs(trace.BsdfStatistics.IncomingPowerWatts - trace.BsdfStatistics.ReflectedPowerWatts
            - trace.BsdfStatistics.AbsorbedPowerWatts) / e.SourcePowerWatts, 0, 1e-12);
        Record("energy-" + rho.ToString(System.Globalization.CultureInfo.InvariantCulture), new { rho, e, viewFactor, sixSigma, trace.BsdfStatistics });
    }

    [Fact]
    public void FullFresnelNoneAndStochasticDoNotAddAnotherReflectionOrAbsorptionToOpaqueBsdf()
    {
        var scene = Scene(.5, 4096); var tracer = new NonSequentialDocumentTracer(); var materials = Optic.CreateBlank().Materials;
        var a = tracer.Trace(scene, materials, new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
        foreach (var mode in new[] { NonSequentialSplittingMode.None, NonSequentialSplittingMode.SimpleStochastic })
        {
            var b = tracer.Trace(scene, materials, new(SplittingMode: mode));
            Assert.Equal(a.EnergyBalance, b.EnergyBalance);
            Assert.Equal(a.Detectors[0].PowerByWavelength[1], b.Detectors[0].PowerByWavelength[1]);
        }
    }

    [Fact]
    public void MultipleDiffuseBouncesKeepAbsorbedAndTruncatedPowerInTheLedger()
    {
        var scene = Scene(.5, 1); var source = scene.Objects[0];
        scene.Replace(source.Id, source with { Kind = NonSequentialObjectKind.SourceRay, Parameters = new SourceRayParameters(1e-10, LocalDirection: new(0, 0, 1)) });
        scene.Remove(scene.Objects[2].Id);
        scene.Insert(2, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, .064)), Parameters = new PlaneRectangleParameters(1000, 1000, NonSequentialSurfaceBehavior.Reflective), SurfaceBsdf = new(NonSequentialBsdfKind.LambertianReflection, .5) });
        var bottom = scene.Objects[1]; scene.Replace(bottom.Id, bottom with { Parameters = new PlaneRectangleParameters(1000, 1000, NonSequentialSurfaceBehavior.Reflective) });
        var result = new NonSequentialDocumentTracer().Trace(scene, Optic.CreateBlank().Materials, new(MaximumSegmentsPerRay: 4));
        Assert.InRange(Math.Abs(result.EnergyBalance.TruncatedPowerWatts / result.EnergyBalance.SourcePowerWatts - Math.Pow(.5, 4)), 0, 1e-12);
        Assert.InRange(Math.Abs(result.EnergyBalance.AccountedPowerWatts / result.EnergyBalance.SourcePowerWatts - 1), 0, 1e-12);
        Assert.All(Assert.Single(result.Branches).Segments, s => Assert.Equal(RayInteractionKind.DiffuseReflection, s.InteractionKind));
    }

    [Fact]
    public async Task DatabaseFiltersProjectRoundtripAndSceneIdentityPreserveTheActualScatteringModel()
    {
        var scene = Scene(.5, 512); var materials = Optic.CreateBlank().Materials;
        using var stream = new MemoryStream();
        using (var writer = new NonSequentialRayDatabaseWriter(stream, NonSequentialRayDatabaseHeader.Create(scene, materials: materials), true))
        { new NonSequentialDocumentTracer().Trace(scene, materials, sink: writer); writer.Complete(); }
        stream.Position = 0; using var reader = new NonSequentialRayDatabaseReader(stream);
        Assert.Equal("LambertianReflection/1", reader.Header.SurfaceBsdfProtocol);
        var detected = reader.ReadBranches(NonSequentialPathFilter.Parse("S2 & R2 & D3")).ToArray();
        Assert.NotEmpty(detected); Assert.All(detected, b => Assert.Contains(b.Segments, s => s.InteractionKind == RayInteractionKind.DiffuseReflection));
        Assert.Empty(reader.ReadBranches(NonSequentialPathFilter.Parse("!S2 & D3")));
        var summaries = NonSequentialPathAnalyzer.Analyze(scene, detected);
        Assert.Contains(summaries, row => row.FilterExpression.Contains("S2"));
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("diffuse.staropt");
        await StarOptProjectStore.SaveAsync(new([Optic.CreateBlank()], 0, NonSequentialDocument: scene), path);
        var opened = (await StarOptProjectStore.LoadAsync(path)).NonSequentialDocument!;
        Assert.Equal(scene.Objects[1].SurfaceBsdf, opened.Objects[1].SurfaceBsdf);
        Assert.Equal(NonSequentialSceneHasher.Compute(scene, materials), NonSequentialSceneHasher.Compute(opened, materials));
        var oldHash = NonSequentialSceneHasher.Compute(scene, materials); var changed = scene.Objects[1];
        scene.Replace(changed.Id, changed with { SurfaceBsdf = new(NonSequentialBsdfKind.LambertianReflection, .25) });
        Assert.NotEqual(oldHash, NonSequentialSceneHasher.Compute(scene, materials)); Assert.True(reader.IsStale(scene, materials));
        using var raw = File.OpenRead(path); raw.Position = 52; using var decompressor = new BrotliStream(raw, CompressionMode.Decompress);
        using var json = await JsonDocument.ParseAsync(decompressor); Assert.Equal(8, json.RootElement.GetProperty("FormatVersion").GetInt32());
    }

    [Fact]
    public void LegacyNullBsdfAndUnsupportedTransparentSurfacesAreExplicit()
    {
        var scene = Scene(.5, 1); var surface = scene.Objects[1];
        var direct = new NonSequentialDocument("BSDF boundary", scene.Wavelengths, [surface]);
        Assert.Throws<NotSupportedException>(() => new OpticalSystemDesign.Core.Analysis.Imaging.DirectCameraScene(direct,
            [new(surface.Id, [.5])], []));
        Assert.Throws<InvalidDataException>(() => scene.Replace(surface.Id, surface with { Parameters = new PlaneRectangleParameters(1, 1, NonSequentialSurfaceBehavior.Refractive) }));
        scene.Replace(surface.Id, surface with { SurfaceBsdf = null });
        Assert.DoesNotContain("SurfaceBsdf", JsonSerializer.Serialize(scene));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => new NonSequentialDocumentTracer().Trace(scene, Optic.CreateBlank().Materials));
    }

    internal static void Record(string name, object data)
    {
        var dir = Environment.GetEnvironmentVariable("OPTICAL_BSDF_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, name + ".json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }
}
