using System.Text;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.Tests;

public sealed class FullReliabilityCoreTests
{
    [Fact]
    public void SceneIdentityIncludesActualDispersionAndExtinction()
    {
        var sphere = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.Sphere) with
        { Parameters = new SphereParameters(Material: "same-name") };
        var document = Scene([sphere]);
        var materials = new MaterialRegistry();
        materials.Register(new ConstantIndexMaterial("same-name", 1.5));
        var first = NonSequentialSceneHasher.Compute(document, materials);
        materials.Register(new ConstantIndexMaterial("same-name", 1.6));
        var second = NonSequentialSceneHasher.Compute(document, materials);
        Assert.NotEqual(first, second);
        materials.Register(new ConstantIndexMaterial("same-name", 1.6, .01));
        Assert.NotEqual(second, NonSequentialSceneHasher.Compute(document, materials));
        Assert.Equal(NonSequentialSceneHasher.Compute(document, materials), NonSequentialSceneHasher.Compute(document.Clone(), materials.CreateSnapshot()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistryChangesDetachSequentialTraceCache(bool catalogPriority)
    {
        var optic = Optic.CreateCookeTriplet();
        var cache = new RayTraceCache(8, 10000);
        optic.ConfigureRayTraceCache(cache, 7);
        var rays = optic.SequentialRayTracer.RayGenerator.GenerateNormalized(0, 0, .5876, 16, "hexapolar");
        using var first = optic.SequentialRayTracer.Trace(rays, TraceRequest.FinalOnly());
        using var second = optic.SequentialRayTracer.Trace(rays, TraceRequest.FinalOnly());
        Assert.True(cache.Statistics.Hits > 0);
        var hits = cache.Statistics.Hits;
        if (catalogPriority) optic.Materials.SetPreferredGlassCatalogs(["OHARA"]);
        else optic.Materials.Register(new ConstantIndexMaterial("custom", 1.7));
        using var changed = optic.SequentialRayTracer.Trace(rays, TraceRequest.FinalOnly());
        Assert.Equal(hits, cache.Statistics.Hits);
    }

    [Fact]
    public void UnknownWavelengthRemainsUnknownInLivePixelsReplayAndPathSummary()
    {
        var detector = Detector();
        var scene = Scene([detector]);
        var trace = new NonSequentialDocumentTracer().Trace(scene, new MaterialRegistry(),
            new(DirectRay: new RealRay(Vector3D.Zero, new(0, 0, 1), 600)));
        Assert.Equal(1, trace.Detectors[0].PowerByWavelength[0].Sum());
        var replay = NonSequentialDetectorReconstruction.Reconstruct(scene, trace.Branches);
        Assert.Equal(1, replay[0].PowerByWavelength[0].Sum());
        Assert.False(NonSequentialPathFilter.Parse("W1").IsMatch(scene, trace.Branches[0]));
        Assert.Equal(1, NonSequentialPathAnalyzer.Analyze(scene, trace.Branches)[0].WavelengthRayCounts[0]);
    }

    [Fact]
    public void BoundPathFilterUsesOneImmutableObjectIndex()
    {
        var detector = Detector();
        var scene = Scene([detector]);
        var trace = new NonSequentialDocumentTracer().Trace(scene, new MaterialRegistry(),
            new(DirectRay: new RealRay(Vector3D.Zero, new(0, 0, 1), 587.6)));
        var predicate = NonSequentialPathFilter.Parse("D1 & W1").Bind(scene);
        for (var index = 0; index < 10000; index++) Assert.True(predicate(trace.Branches[0]));
        Assert.True(NonSequentialPathFilter.MatchAll.Bind(scene)(trace.Branches[0]));
    }

    [Theory]
    [InlineData(1e8)]
    [InlineData(1e16)]
    public void FarAwayOffAxisSphereRayRetainsTheCorrectChord(double distance)
    {
        var sphere = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.Sphere) with
        { Parameters = new SphereParameters(1, Behavior: NonSequentialSurfaceBehavior.Absorbing) };
        var trace = new NonSequentialDocumentTracer().Trace(Scene([sphere]), new MaterialRegistry(),
            new(DirectRay: new RealRay(new(.5, 0, -distance), new(0, 0, 1), 587.6)));
        var segment = Assert.Single(Assert.Single(trace.Branches).Segments);
        Assert.Equal(.5, segment.End.X);
        Assert.InRange(Math.Abs(segment.End.Z + Math.Sqrt(.75)), 0, 1e-6);
    }

    [Fact]
    public async Task ReaderSupportsConcurrentPagingAndEnumeration()
    {
        var bytes = Database();
        using var stream = new MemoryStream(bytes);
        using var reader = new NonSequentialRayDatabaseReader(stream);
        var jobs = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var pass = 0; pass < 10; pass++)
            {
                Assert.Equal(600, reader.ReadAllBranches().Count);
                Assert.Equal(550, reader.ReadRange(549, 1)[0].Id);
                Assert.Equal(600, reader.ReadChunks().SelectMany(chunk => chunk).Count());
            }
        }));
        await Task.WhenAll(jobs);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("objects")]
    [InlineData("wavelengths")]
    [InlineData("version")]
    public void DatabaseRejectsChecksummedButInvalidHeader(string defect)
    {
        var header = NonSequentialRayDatabaseHeader.Create(Scene([]));
        header = defect switch
        {
            "hash" => header with { SceneHash = "not-a-hash" },
            "objects" => header with { Objects = [null!] },
            "wavelengths" => header with { Wavelengths = [] },
            _ => header with { FormatVersion = 999 }
        };
        using var stream = new MemoryStream();
        if (defect == "version")
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NonSequentialRayDatabaseWriter(stream, header, true));
            return;
        }
        using (var writer = new NonSequentialRayDatabaseWriter(stream, header, leaveOpen: true)) writer.Complete();
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => new NonSequentialRayDatabaseReader(stream, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DatabaseRejectsInvalidInternalSegmentObjects(bool nullSegment)
    {
        using var stream = new MemoryStream();
        var segment = new NonSequentialRaySegment(1, Guid.NewGuid(), 1, Vector3D.Zero, new(0, 0, 1),
            new(0, 0, 1), new(0, 0, 1), 587.6, 1, 1, 1, 1, null);
        using (var writer = new NonSequentialRayDatabaseWriter(stream, NonSequentialRayDatabaseHeader.Create(Scene([])), true))
        {
            writer.OnBranch(new(1, null, 0, null, [nullSegment ? null! : segment], NonSequentialTerminationReason.Escaped, 1, 587.6));
            writer.Complete();
        }
        stream.Position = 0;
        using var reader = new NonSequentialRayDatabaseReader(stream, true);
        Assert.Throws<InvalidDataException>(() => reader.ReadAllBranches());
    }

    [Theory]
    [InlineData("=HYPERLINK(\"test\")")]
    [InlineData(" +SUM(1,2)")]
    [InlineData("-2+3")]
    [InlineData("@SUM(1)")]
    [InlineData("\t=1+1")]
    [InlineData("\uFEFF \t=1+1")]
    public void CsvTextCannotBecomeAnExecutableSpreadsheetFormula(string value) =>
        Assert.StartsWith("\"'", CsvCell.Escape(value));

    [Theory]
    [InlineData("-1.25")]
    [InlineData("+2e-8")]
    [InlineData("ordinary text")]
    public void CsvKeepsNumericValuesAndOrdinaryText(string value) => Assert.Equal('"' + value + '"', CsvCell.Escape(value));

    [Fact]
    public void FixedNonzeroMeritWithoutImprovementIsNotReportedAsOptimizationSuccess()
    {
        var value = 1d;
        var fixedProblem = new OptimizationProblem();
        fixedProblem.AddVariable(new DelegateVariable("fixed", () => value, next => value = next, value, value));
        fixedProblem.AddOperand(new Operand("nonzero", 0, 1, () => 1));
        Assert.False(new NelderMeadOptimizer().Optimize(fixedProblem, 1).Success);
    }

    [Theory]
    [InlineData(".seq")]
    [InlineData(".len")]
    [InlineData(".zmx")]
    public void TextExportRetainsDoublePrecision(string extension)
    {
        var optic = Optic.CreateCookeTriplet();
        var expected = 1.2345678901234567e-11;
        optic.SurfaceGroup.Items[1].Thickness = expected;
        var exported = OpticalFormatCatalog.Export(optic, extension);
        var imported = OpticalFormatCatalog.Import(exported, extension);
        Assert.Equal(expected, imported.SurfaceGroup.Items[1].Thickness);
    }

    [Fact]
    public void ConfigurationLimitsApplyBeforeCreatingUnsavableState()
    {
        var optic = Optic.CreateBlank();
        Assert.Throws<ArgumentException>(() => new MultiConfiguration(Enumerable.Repeat(optic, StarOptProjectStore.MaximumConfigurationCount + 1)));
        var multiple = new MultiConfiguration(optic);
        while (multiple.Configurations.Count < StarOptProjectStore.MaximumConfigurationCount) multiple.Configurations.Add(optic);
        Assert.Throws<InvalidOperationException>(() => multiple.AddConfiguration());
        Assert.Throws<InvalidOperationException>(() => multiple.Configurations.Add(optic));
        Assert.Equal(StarOptProjectStore.MaximumConfigurationCount, multiple.Configurations.Count);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(10001, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 501)]
    public void SerialMonteCarloEnforcesTheParallelBudget(int trials, int iterations)
    {
        var optic = Optic.CreateBlank();
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonteCarlo(optic, optic.CreateTolerancing()).RunDetailed(trials, 1, iterations));
    }

    [Fact]
    public void SerialAndParallelMonteCarloSharePerTrialRandomStreams()
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Items[0].Thickness = 100;
        static Tolerancing Worker(Optic worker)
        {
            var model = worker.CreateTolerancing();
            var variable = new DelegateVariable("spacing", () => worker.SurfaceGroup.Items[0].Thickness,
                value => worker.SurfaceGroup.Items[0].Thickness = value, 0, 1000);
            model.AddPerturbation(new VariablePerturbation("uniform", variable, new UniformSampler(-1, 1)));
            model.AddOperand(new Operand("spacing", 100, 1, () => variable.Value));
            model.SetCriterionEvaluator(() => variable.Value);
            return model;
        }
        var monteCarlo = new MonteCarlo(optic, Worker(optic));
        var serial = monteCarlo.RunDetailed(12, 123, 0);
        foreach (var degree in new[] { 1, 4 })
        {
            var parallel = monteCarlo.RunDetailed(12, 123, 0, CancellationToken.None, Worker, degree);
            Assert.Equal(serial.Select(r => r.Criterion), parallel.Select(r => r.Criterion));
        }
        Assert.Equal(100, optic.SurfaceGroup.Items[0].Thickness);
    }

    [Fact]
    public void StlRejectsVertexPrefixThatIsNotTheVertexKeyword() => Assert.Throws<InvalidDataException>(() =>
        NonSequentialStlImporter.Import(Encoding.UTF8.GetBytes("solid a\nvertexoops 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendsolid"), "bad.stl"));

    [Fact]
    public void FilmLayerBudgetStopsEnumeratingBeforeUnboundedAllocation()
    {
        var count = 0;
        IEnumerable<CoherentFilm> Films()
        {
            while (true) { count++; yield return new CoherentFilm(new ConstantIndexMaterial("film", 1.5), 100); }
        }
        Assert.Throws<ArgumentException>(() => new CoherentThinFilmSolver(new AirMaterial(), new AirMaterial(), Films()));
        Assert.Equal(CoherentThinFilmSolver.MaximumLayers + 1, count);
    }

    [Theory]
    [InlineData(".seq")]
    [InlineData(".len")]
    public void TextPrescriptionNameKeepsCommentCharacters(string extension)
    {
        var optic = Optic.CreateCookeTriplet(); optic.Name = "lens ! # // literal";
        Assert.Equal(optic.Name, OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(optic, extension), extension).Name);
    }

    private static NonSequentialObjectDefinition Detector() => NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
    { LocalCoordinateSystem = new CoordinateSystem(new(0, 0, 5)), Parameters = new DetectorRectangleParameters(2, 2, 2, 2) };
    private static NonSequentialDocument Scene(NonSequentialObjectDefinition[] objects) => new("test", [new("d", 587.6, 1, true)], objects);
    private static byte[] Database()
    {
        using var stream = new MemoryStream();
        using (var writer = new NonSequentialRayDatabaseWriter(stream, NonSequentialRayDatabaseHeader.Create(Scene([])), true))
        {
            for (var index = 1; index <= 600; index++) writer.OnBranch(new(index, null, 0, null, [], NonSequentialTerminationReason.Escaped, 1, 587.6));
            writer.Complete();
        }
        return stream.ToArray();
    }
}
