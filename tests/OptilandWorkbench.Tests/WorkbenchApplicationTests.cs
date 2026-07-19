using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.Tests;

public sealed class WorkbenchApplicationTests
{
    [Fact]
    public void SurfaceEditPublishesOneRevisionAndSupportsUndoRedo()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var events = new List<WorkspaceChangedEventArgs>();
        application.Events.Changed += (_, args) => events.Add(args);
        var original = application.Prescription.GetSurfaces()
            .First(surface => surface.Number > 0 && double.IsFinite(surface.Radius));
        var initialRevision = application.Events.Revision;

        application.Prescription.UpdateSurface(original with { Radius = original.Radius + 2.5 });

        Assert.Equal(initialRevision + 1, application.Events.Revision);
        var changed = Assert.Single(events);
        Assert.Equal(WorkspaceChangeCategory.Surface, changed.Category);
        Assert.Equal(
            original.Radius + 2.5,
            application.Prescription.GetSurfaces().Single(surface => surface.Number == original.Number).Radius,
            precision: 10);

        events.Clear();
        Assert.True(application.Documents.Undo());
        Assert.Equal(
            original.Radius,
            application.Prescription.GetSurfaces().Single(surface => surface.Number == original.Number).Radius,
            precision: 10);
        Assert.Single(events);

        events.Clear();
        Assert.True(application.Documents.Redo());
        Assert.Equal(
            original.Radius + 2.5,
            application.Prescription.GetSurfaces().Single(surface => surface.Number == original.Number).Radius,
            precision: 10);
        Assert.Single(events);
    }

    [Fact]
    public async Task AnalysisResultCarriesRequestIdentityAndSourceRevision()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var instanceId = Guid.NewGuid();
        var sourceRevision = application.Events.Revision;
        var settings = application.Analyses.MergeSettings("First Order", null);

        var result = await application.Analyses.RunAsync(new AnalysisRequestDto(
            instanceId,
            7,
            "First Order",
            settings));

        Assert.Equal(instanceId, result.InstanceId);
        Assert.Equal(7, result.Generation);
        Assert.Equal(sourceRevision, result.SourceRevision);
        Assert.NotEmpty(result.View.Rows);
    }

    [Fact]
    public async Task FileSwitchCancelsRunningHeavyAnalysis()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var settings = application.Analyses.MergeSettings("Encircled Energy", null);
        settings["NumRays"] = "200000";
        settings["NumPoints"] = "2048";
        var running = application.Analyses.RunAsync(new AnalysisRequestDto(
            Guid.NewGuid(),
            1,
            "Encircled Energy",
            settings));

        application.Documents.NewBlank();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await running);
    }

    [Fact]
    public async Task FileSwitchCancelsRunningTolerancing()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var surface = application.Prescription.GetSurfaces().First(item => item.Number > 1);
        var running = application.Tolerancing.RunAsync(new TolerancingRequestDto(
            surface.Number,
            0.1,
            0.05,
            10_000,
            1234,
            100));

        application.Documents.NewBlank();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await running);
    }

    [Fact]
    public async Task VisualizationUsesImmutableRevisionSnapshot()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var expectedRevision = application.Events.Revision;

        var scene = await application.Visualization.BuildSceneAsync(SceneDimension.TwoDimensional);

        Assert.Equal(expectedRevision, scene.SourceRevision);
        Assert.Equal(SceneDimension.TwoDimensional, scene.Dimension);
        Assert.NotNull(scene.TwoDimensional);
        Assert.Null(scene.ThreeDimensional);
        Assert.NotEmpty(scene.TwoDimensional!.Surfaces);
    }

    [Fact]
    public async Task ThreeDimensionalVisualizationPreservesCurvedSurfaceMesh()
    {
        using var application = WorkbenchApplication.Create("tessar");

        var scene = await application.Visualization.BuildSceneAsync(SceneDimension.ThreeDimensional);

        var threeDimensional = Assert.IsType<Scene3Dto>(scene.ThreeDimensional);
        var curvedSurface = threeDimensional.Surfaces.Single(surface => surface.SurfaceNumber == 1);
        var points = curvedSurface.Faces.SelectMany(face => face.Points).ToArray();
        Assert.NotEmpty(points);
        Assert.True(points.Max(point => point.Z) - points.Min(point => point.Z) > 0.01);
        Assert.All(points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
            Assert.True(double.IsFinite(point.Z));
        });
    }

    [Fact]
    public async Task VisualizationRequestIncludesAllSelectedWavelengths()
    {
        using var application = WorkbenchApplication.Create("tessar");
        var options = application.Visualization.GetVisualizationOptions();

        var scene = await application.Visualization.BuildSceneAsync(new VisualizationRequestDto(
            SceneDimension.TwoDimensional,
            FirstSurface: 1,
            LastSurface: options.SurfaceNumbers.Max(),
            FieldIndex: 0,
            IncludeAllWavelengths: true,
            RayCount: 3,
            LowerPupil: -1,
            UpperPupil: 1));

        var twoDimensional = Assert.IsType<Scene2Dto>(scene.TwoDimensional);
        Assert.Equal(
            options.Wavelengths.Select(wavelength => wavelength.Index),
            twoDimensional.Rays.Select(ray => ray.WavelengthIndex).Distinct().Order());
        Assert.Equal(options.Wavelengths.Count * 3, twoDimensional.Rays.Count);
    }

    [Fact]
    public async Task FailedOpenKeepsCurrentDocumentAndPath()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var original = application.Documents.GetSnapshot();
        var path = Path.Combine(Path.GetTempPath(), $"invalid-optic-{Guid.NewGuid():N}.optiland.json");
        try
        {
            await File.WriteAllTextAsync(path, "not-json");

            await Assert.ThrowsAnyAsync<Exception>(() => application.Documents.OpenAsync(path));

            var current = application.Documents.GetSnapshot();
            Assert.Null(application.Documents.CurrentPath);
            Assert.Equal(original.Name, current.Name);
            Assert.Equal(original.SurfaceCount, current.SurfaceCount);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
