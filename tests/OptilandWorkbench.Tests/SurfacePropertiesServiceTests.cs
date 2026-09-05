using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class SurfacePropertiesServiceTests
{
    [Fact]
    public async Task ComponentsAndPropertiesCommitTogetherUndoAndRoundTrip()
    {
        using var app = WorkbenchApplication.Create("cooke");
        // Establish the derived automatic apertures before capturing the undo baseline.
        app.Prescription.UpdateSurfaceComponents(1, Update(app.Prescription.GetSurfaces()[1]));
        var before = app.Prescription.GetSurfaces();
        var selected = before.First(row => row.Number > 0 && !row.IsStop);
        var revision = app.Events.Revision;
        app.Prescription.UpdateSurfaceComponents(selected.Number, Update(selected) with
        {
            IsStop = true,
            Coating = "MgF2",
            SemiDiameterFixed = true,
            SemiDiameter = 8.25
        });
        var updated = app.Prescription.GetSurfaces().Single(row => row.Number == selected.Number);
        Assert.Equal(revision + 1, app.Events.Revision);
        Assert.Equal(selected.Number, Assert.Single(app.Prescription.GetSurfaces(), row => row.IsStop).Number);
        Assert.Equal("MgF2", updated.Coating);
        Assert.True(updated.SemiDiameterFixed);
        Assert.Equal(8.25, updated.SemiDiameter);
        Assert.True(app.Documents.Undo());
        Assert.Equal(before, app.Prescription.GetSurfaces());
        Assert.True(app.Documents.Redo());
        Assert.Equal(updated, app.Prescription.GetSurfaces().Single(row => row.Number == selected.Number));

        var path = Path.Combine(Path.GetTempPath(), $"surface-properties-{Guid.NewGuid():N}.staropt");
        try
        {
            await app.Documents.SaveAsync(path);
            using var reloaded = WorkbenchApplication.Create();
            await reloaded.Documents.OpenAsync(path);
            Assert.Equal(updated, reloaded.Prescription.GetSurfaces().Single(row => row.Number == selected.Number));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void InvalidPropertyEditDoesNotPartiallyChangeGeometryOrHistory()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Prescription.GetSurfaces();
        var revision = app.Events.Revision;
        Assert.Throws<ArgumentOutOfRangeException>(() => app.Prescription.UpdateSurfaceComponents(1,
            Update(before[1]) with { GeometryKind = "平面光栅", SemiDiameterFixed = true, SemiDiameter = double.NaN }));
        Assert.Equal(before, app.Prescription.GetSurfaces());
        Assert.Equal(revision, app.Events.Revision);
        Assert.False(app.Documents.Undo());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjectAndImageCannotBeUsedAsStop(bool image)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Prescription.GetSurfaces();
        var row = image ? before[^1] : before[0];
        Assert.Throws<ArgumentException>(() => app.Prescription.UpdateSurfaceComponents(row.Number, Update(row) with { IsStop = true }));
        Assert.Equal(before, app.Prescription.GetSurfaces());
    }

    [Fact]
    public void StopMustBeMovedInsteadOfDeleted()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var stop = app.Prescription.GetSurfaces().Single(row => row.IsStop);
        Assert.Throws<ArgumentException>(() => app.Prescription.UpdateSurfaceComponents(stop.Number, Update(stop) with { IsStop = false }));
        Assert.Equal(stop.Number, Assert.Single(app.Prescription.GetSurfaces(), row => row.IsStop).Number);
    }

    [Fact]
    public void EditingPropertiesPreservesExistingAsphereApertureAndCoatingComponents()
    {
        var optic = Optic.CreateCookeTriplet();
        var runtime = new WorkbenchRuntime(optic);
        var surface = optic.SurfaceGroup.Items[1];
        var geometry = new EvenAsphereGeometry(42, -0.7, new[] { 1.25e-5, -3.5e-8 });
        var aperture = new AnnularAperture(6.4, 1.7);
        var coating = new SimpleCoatingModel(0.8, 0.15);
        surface.Geometry = geometry;
        surface.PhysicalAperture = aperture;
        surface.CoatingModel = coating;
        runtime.ApplySurfaceComponents(surface, "偶次非球面", "环形",
            isStop: true, coating: surface.Coating, semiDiameterFixed: true, semiDiameter: 7);
        Assert.Same(geometry, surface.Geometry);
        Assert.Same(aperture, surface.PhysicalAperture);
        Assert.Same(coating, surface.CoatingModel);
        Assert.True(surface.IsStop);
        Assert.Equal(7, surface.SemiDiameter);
    }

    [Fact]
    public void NewPhysicalApertureUsesNewlyCommittedFixedSemiDiameter()
    {
        var optic = Optic.CreateCookeTriplet();
        var runtime = new WorkbenchRuntime(optic);
        var surface = optic.SurfaceGroup.Items[1];
        runtime.ApplySurfaceComponents(surface, "标准球面/圆锥", "圆形", semiDiameterFixed: true, semiDiameter: 7);
        Assert.Equal(7, Assert.IsType<CircularAperture>(surface.PhysicalAperture).Radius);
    }

    [Fact]
    public void UnsupportedImportedGeometryCannotBeReplacedByPropertyEditor()
    {
        var optic = Optic.CreateCookeTriplet();
        var runtime = new WorkbenchRuntime(optic);
        var surface = optic.SurfaceGroup.Items[1];
        var geometry = new OpaqueGeometryPayload(ComponentSnapshot.Empty("Zemax TYPE BINARY_2"));
        surface.Geometry = geometry;
        Assert.Throws<NotSupportedException>(() => runtime.ApplySurfaceComponents(surface, "平面", "无"));
        Assert.Same(geometry, surface.Geometry);
    }

    [Fact]
    public void ReadOnlyInspectionUsesActualCoordinatesRatherThanPlaceholderZeros()
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        surface.CoordinateSystem = new CoordinateSystem(new Vector3D(1.25, -2.5, 3), 4, 5, 6);
        var row = WorkbenchMapper.ToSurfaceDto(surface);
        Assert.Equal(new SurfaceInspectionDto("none", 1.25, -2.5, 3, 4, 5, 6), row.Inspection);
    }

    internal static SurfaceComponentUpdateDto Update(SurfaceRowDto row) => new(
        row.GeometryKind, row.ApertureKind, row.GratingOrder, row.GratingPeriodMicrometers,
        row.GrooveOrientationAngleDegrees, row.ThinLensFocalLength);
}
