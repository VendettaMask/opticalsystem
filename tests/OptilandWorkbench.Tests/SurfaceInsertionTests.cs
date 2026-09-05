using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class SurfaceInsertionTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(-1, false)]
    public async Task InsertAtRequestedRowIsOneUndoableEditAndRoundTrips(int requested, bool after)
    {
        using var app = WorkbenchApplication.Create("cooke");
        // Materialize derived apertures before comparing undo snapshots.
        app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[1]);
        var before = app.Prescription.GetSurfaces();
        var target = requested < 0 ? before.Count - 1 : requested;
        var index = target + (after ? 1 : 0);
        var revision = app.Events.Revision;
        var inserted = app.Prescription.InsertSurface(target, after);
        var rows = app.Prescription.GetSurfaces();
        Assert.Equal(index, inserted);
        Assert.Equal(revision + 1, app.Events.Revision);
        Assert.Equal(before.Count + 1, rows.Count);
        Assert.Equal(Enumerable.Range(0, rows.Count), rows.Select(row => row.Number));
        Assert.Equal(40, rows[index].Radius);
        Assert.Equal(5, rows[index].Thickness);
        Assert.Equal("Air", rows[index].Material);
        Assert.False(rows[index].IsStop);
        Assert.Equal(before.Select(row => (row.Label, row.Radius, row.Thickness, row.IsStop)),
            rows.Where(row => row.Number != index).Select(row => (row.Label, row.Radius, row.Thickness, row.IsStop)));
        Assert.True(app.Documents.Undo());
        Assert.Equal(before, app.Prescription.GetSurfaces());
        Assert.True(app.Documents.Redo());
        Assert.Equal(rows, app.Prescription.GetSurfaces());

        var path = Path.Combine(Path.GetTempPath(), $"surface-insertion-{Guid.NewGuid():N}.staropt");
        try
        {
            await app.Documents.SaveAsync(path);
            using var reopened = WorkbenchApplication.Create();
            await reopened.Documents.OpenAsync(path);
            Assert.Equal(rows, reopened.Prescription.GetSurfaces());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, true)] // The image surface.
    [InlineData(-2, false)]
    [InlineData(int.MaxValue, true)]
    public void InvalidInsertionDoesNotChangeDocumentOrHistory(int requested, bool after)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Prescription.GetSurfaces();
        var revision = app.Events.Revision;
        var target = requested == -1 ? before.Count - 1 : requested;
        Assert.Throws<ArgumentOutOfRangeException>(() => app.Prescription.InsertSurface(target, after));
        Assert.Equal(before, app.Prescription.GetSurfaces());
        Assert.Equal(revision, app.Events.Revision);
        Assert.False(app.Documents.Undo());
    }

    [Fact]
    public void MiddleInsertionPreservesRichComponentsAndRemapsEveryConfigurationsPickupsAndOverrides()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].Geometry = new EvenAsphereGeometry(44, -0.7, new[] { 1e-5, -2e-8 });
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(4, 3);
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(0.82, 0.07);
        optic.Pickups.LinkRadius(1, 4, 0.5, 1);
        var runtime = new WorkbenchRuntime(optic);
        var alternate = runtime.AddMultiConfiguration();
        runtime.SetMultiConfigurationThickness(alternate, 4, 12.25);
        var before = Serialize(runtime.CaptureDocument());
        var original = runtime.Surfaces.ToArray();
        var geometry = original[1].Geometry;
        var aperture = original[1].PhysicalAperture;
        var coating = original[1].CoatingModel;

        Assert.Equal(2, runtime.InsertSurface(2, after: false));
        Assert.Equal(original, runtime.Surfaces.Where(surface => surface.Number != 2));
        Assert.Same(geometry, runtime.Surfaces[1].Geometry);
        Assert.Same(aperture, runtime.Surfaces[1].PhysicalAperture);
        Assert.Same(coating, runtime.Surfaces[1].CoatingModel);
        var document = runtime.CaptureDocument();
        Assert.All(document.Configurations, configuration =>
        {
            Assert.Equal(original.Length + 1, configuration.SurfaceGroup.Items.Count);
            var pickup = Assert.Single(configuration.Pickups.RadiusPickups);
            Assert.Equal(1, pickup.SourceSurface);
            Assert.Equal(5, pickup.TargetSurface);
            Assert.Equal(0.5, pickup.Scale);
            Assert.Equal(1, pickup.Offset);
        });
        Assert.Equal(12.25, document.Configurations[alternate].SurfaceGroup.Items[5].Thickness);
        Assert.Contains(document.BrokenLinks!, link => link.ConfigurationIndex == alternate
            && link.SurfaceNumber == 5 && link.Property == "thickness");
        var inserted = Serialize(document);
        Assert.True(runtime.Undo());
        Assert.Equal(before, Serialize(runtime.CaptureDocument()));
        Assert.True(runtime.Redo());
        Assert.Equal(inserted, Serialize(runtime.CaptureDocument()));
        runtime.RemoveSurface(runtime.Surfaces[2]);
        Assert.Equal(before, Serialize(runtime.CaptureDocument()));
    }

    [Fact]
    public void DeleteRemovesOnlyRequestedInteriorSurfaceAndCanBeUndone()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[1]);
        var before = app.Prescription.GetSurfaces();
        var revision = app.Events.Revision;
        app.Prescription.RemoveSurface(2);
        Assert.Equal(revision + 1, app.Events.Revision);
        Assert.Equal(before.Where(row => row.Number != 2).Select(row => (row.Label, row.Radius)),
            app.Prescription.GetSurfaces().Select(row => (row.Label, row.Radius)));
        Assert.True(app.Documents.Undo());
        Assert.Equal(before, app.Prescription.GetSurfaces());
    }

    private static string Serialize(LoadedOpticalDocument document) => JsonSerializer.Serialize(new
    {
        document.ActiveConfigurationIndex,
        Configurations = document.Configurations.Select(configuration => configuration.ToSnapshot()),
        document.BrokenLinks
    }, new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
}
