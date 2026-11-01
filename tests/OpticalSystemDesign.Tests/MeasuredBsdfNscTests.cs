using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Scattering;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class MeasuredBsdfNscTests
{
    internal static NonSequentialDocument Scene(double r, double t, int count = 8192)
    {
        var scene = NonSequentialBsdfTests.Scene(r, count); var plane = scene.Objects[1];
        scene.Replace(plane.Id, plane with { SurfaceBsdf = new(NonSequentialBsdfKind.Measured, 0, MeasuredBsdfTests.Constant(r, t)),
            Parameters = new PlaneRectangleParameters(1, 1, t > 0 ? NonSequentialSurfaceBehavior.Refractive : NonSequentialSurfaceBehavior.Reflective) });
        var detector = scene.Objects[2]; scene.Replace(detector.Id, detector with { LocalCoordinateSystem = new(new(0, 0, -.064)),
            Parameters = new DetectorRectangleParameters(.128, .128, 32, 32, false, true) });
        return scene;
    }

    [Theory]
    [InlineData(.2, .5)] [InlineData(0, 1)] [InlineData(0, 0)] [InlineData(.4, 0)]
    public void MixedBrdfBtdfConservePowerAndMatchIndependentDetectorViewFactor(double r, double t)
    {
        var scene = Scene(r, t); var result = new NonSequentialDocumentTracer().Trace(scene, Optic.CreateBlank().Materials);
        var e = result.EnergyBalance; var s = result.BsdfStatistics!; var energy = r + t;
        Assert.InRange(Math.Abs(e.AccountedPowerWatts / e.SourcePowerWatts - 1), 0, 1e-12);
        Assert.InRange(Math.Abs(e.AbsorbedPowerWatts / e.SourcePowerWatts - 1 + energy), 0, 1e-12);
        Assert.Equal(8192, s.SampleCount); Assert.Equal(energy == 0 ? 0 : 8192, s.ReflectionSampleCount + s.TransmissionSampleCount);
        Assert.InRange(Math.Abs(s.IncomingPowerWatts - s.ReflectedPowerWatts - s.TransmittedPowerWatts - s.AbsorbedPowerWatts) / e.SourcePowerWatts, 0, 1e-12);
        var view = 4 / (Math.PI * Math.Sqrt(2)) * Math.Atan(1 / Math.Sqrt(2));
        var p = energy > 0 ? t / energy * view : 0;
        Assert.InRange(Math.Abs(e.DetectorPowerWatts / e.SourcePowerWatts - t * view), 0, energy * 6 * Math.Sqrt(p * (1 - p) / 8192) + 1e-12);
        if (energy > 0)
        {
            var probability = t / energy;
            Assert.InRange(Math.Abs(s.TransmissionSampleCount / 8192d - probability), 0, 6 * Math.Sqrt(probability * (1 - probability) / 8192) + 1e-12);
        }
        MeasuredBsdfTests.Record($"energy-r{r}-t{t}", new { R = r, T = t, e, s, ViewFactor = view, ExpectedDetectorFraction = t * view });
    }

    [Fact]
    public void BtdfUpdatesSolidEntryExitAndOpticalPathWithoutAnotherSnellOrFresnelStep()
    {
        var material = Optic.CreateBlank().Materials; material.Register(new ConstantIndexMaterial("test n=1.7", 1.7));
        var source = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay) with
        { LocalCoordinateSystem = new(new(0, 0, -2)), Parameters = new SourceRayParameters(1) };
        var box = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.Box) with
        { Parameters = new BoxParameters(1000, 1000, 1, "test n=1.7", NonSequentialSurfaceBehavior.Refractive),
            SurfaceBsdf = new(NonSequentialBsdfKind.Measured, 0, MeasuredBsdfTests.Constant(0, 1)) };
        var detector = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, 2)), Parameters = new DetectorRectangleParameters(1000, 1000, 1, 1) };
        var scene = new NonSequentialDocument("Interface control", [new("550", 550, 1, true)], [source, box, detector], "Vacuum");
        foreach (var mode in Enum.GetValues<NonSequentialSplittingMode>())
        {
            var run = new NonSequentialDocumentTracer().Trace(scene, material, new(SplittingMode: mode));
            var branch = Assert.Single(run.Branches); Assert.Equal(3, branch.Segments.Count);
            Assert.Equal(RayInteractionKind.DiffuseTransmission, branch.Segments[0].InteractionKind);
            Assert.Equal(RayInteractionKind.DiffuseTransmission, branch.Segments[1].InteractionKind);
            Assert.Equal(branch.Segments[0].SegmentLength, branch.Segments[0].CumulativeOpticalPathLength, 12);
            Assert.Equal(1.7 * branch.Segments[1].SegmentLength,
                branch.Segments[1].CumulativeOpticalPathLength - branch.Segments[0].CumulativeOpticalPathLength, 12);
            Assert.Equal(branch.Segments[2].SegmentLength,
                branch.Segments[2].CumulativeOpticalPathLength - branch.Segments[1].CumulativeOpticalPathLength, 12);
            Assert.InRange(run.EnergyBalance.DetectorPowerWatts, 1 - 1e-14, 1 + 1e-14);
        }
    }

    [Fact]
    public async Task BtdfFiltersDatabaseSceneHashAndNativeProjectFreezeTheFullMeasuredGrid()
    {
        var scene = Scene(.2, .5, 1024); var material = Optic.CreateBlank().Materials;
        using var stream = new MemoryStream();
        using (var writer = new NonSequentialRayDatabaseWriter(stream, NonSequentialRayDatabaseHeader.Create(scene, materials: material), true))
        { new NonSequentialDocumentTracer().Trace(scene, material, sink: writer); writer.Complete(); }
        stream.Position = 0; using var reader = new NonSequentialRayDatabaseReader(stream);
        Assert.Equal(MeasuredBsdfData.Protocol, reader.Header.SurfaceBsdfProtocol);
        var filter = NonSequentialPathFilter.Parse("B2 & S2 & T2 & !R2 & D3");
        var branches = reader.ReadBranches(filter).ToArray(); Assert.NotEmpty(branches);
        Assert.All(branches, b => Assert.Contains(b.Segments, s => s.InteractionKind == RayInteractionKind.DiffuseTransmission));
        Assert.Empty(reader.ReadBranches(NonSequentialPathFilter.Parse("R2 & D3")));
        Assert.Contains(NonSequentialPathAnalyzer.Analyze(scene, branches), r => r.FilterExpression.Contains("B2"));
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("measured.staropt");
        await StarOptProjectStore.SaveAsync(new([Optic.CreateBlank()], 0, NonSequentialDocument: scene), path);
        var opened = (await StarOptProjectStore.LoadAsync(path)).NonSequentialDocument!;
        Assert.Equal(scene.Objects[1].SurfaceBsdf!.MeasuredData!.DataSha256, opened.Objects[1].SurfaceBsdf!.MeasuredData!.DataSha256);
        Assert.Equal(NonSequentialSceneHasher.Compute(scene, material), NonSequentialSceneHasher.Compute(opened, material));
        using var raw = File.OpenRead(path); raw.Position = 52; using var decompressor = new BrotliStream(raw, CompressionMode.Decompress);
        using var json = await JsonDocument.ParseAsync(decompressor); Assert.Equal(9, json.RootElement.GetProperty("FormatVersion").GetInt32());
        var item = scene.Objects[1]; scene.Replace(item.Id, item with { SurfaceBsdf = new(NonSequentialBsdfKind.Measured, 0, MeasuredBsdfTests.Constant(.1, .5)) });
        Assert.True(reader.IsStale(scene, material));
    }

    [Fact]
    public void TransmissionRequiresDeclaredMediaAndBothMeasuredSides()
    {
        var scene = Scene(.2, .5, 1); var plane = scene.Objects[1];
        Assert.Throws<InvalidDataException>(() => scene.Replace(plane.Id, plane with { Parameters = new PlaneRectangleParameters(1, 1, NonSequentialSurfaceBehavior.Reflective) }));
        var data = plane.SurfaceBsdf!.MeasuredData!;
        var front = new MeasuredBsdfData(data.Name, data.SourceFileName, data.SourceSha256, data.Points.Where(p => p.Side == BsdfSide.Front).ToArray());
        Assert.Throws<InvalidDataException>(() => scene.Replace(plane.Id, plane with { SurfaceBsdf = new(NonSequentialBsdfKind.Measured, 0, front) }));
    }

    [Fact]
    public void RotatedUnitTransmissionKeepsTheSamplingWeightPassive()
    {
        var scene = Scene(0, 1); const double degrees = 37; var angle = degrees * Math.PI / 180;
        foreach (var item in scene.Objects.ToArray())
        {
            var old = item.LocalCoordinateSystem; var p = old.Origin;
            scene.Replace(item.Id, item with { LocalCoordinateSystem = new(
                new(p.X * Math.Cos(angle) + p.Z * Math.Sin(angle), p.Y, -p.X * Math.Sin(angle) + p.Z * Math.Cos(angle)),
                old.RotationXDegrees, old.RotationYDegrees + degrees, old.RotationZDegrees) });
        }
        var result = new NonSequentialDocumentTracer().Trace(scene, Optic.CreateBlank().Materials);
        Assert.InRange(Math.Abs(result.EnergyBalance.AccountedPowerWatts / result.EnergyBalance.SourcePowerWatts - 1), 0, 1e-12);
        Assert.InRange(result.EnergyBalance.AbsorbedPowerWatts / result.EnergyBalance.SourcePowerWatts, 0, 1e-13);
        Assert.Equal(8192, result.BsdfStatistics!.TransmissionSampleCount);
    }
}
