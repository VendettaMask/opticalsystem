using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Tests;

public sealed class CadExportGeometryCoverageTests
{
    [Fact]
    public void PlaneLensUsesSemiDiametersAndDoesNotCutPhysicalAperture()
    {
        var optic = Optic.CreateCookeTriplet();
        var front = optic.SurfaceGroup.Items[1];
        var back = optic.SurfaceGroup.Items[2];
        front.Geometry = new PlaneGeometry();
        back.Geometry = new PlaneGeometry();
        front.SemiDiameter = 3;
        back.SemiDiameter = 5;
        front.PhysicalAperture = new AnnularAperture(2, 1);

        var mesh = CadLensMeshBuilder.Build(
            optic,
            new StepCadExportOptions(SurfaceSamples: 9, AngularSamples: 32),
            CancellationToken.None).Parts[0];

        Assert.Contains(mesh.Vertices, point => Close(point, Global(front, 0, 0)));
        Assert.Contains(mesh.Vertices, point => Close(point, Global(back, 0, 0)));
        Assert.Contains(mesh.Vertices, point => Close(point, Global(front, 3, 0)));
        Assert.Contains(mesh.Vertices, point => Close(point, Global(back, 5, 0)));
        Assert.Contains(mesh.Vertices, point => Close(point, ExtendedRim(front, 5, 0)));
    }

    [Fact]
    public void CementedLensGroupIsSplitByMaterial()
    {
        var document = StepCadExporter.Build(
            Optic.CreateTessarLens(),
            new StepCadExportOptions(SurfaceSamples: 9, AngularSamples: 32));

        Assert.Equal(4, document.PartCount);
        Assert.Contains("Lens S6-S7 K10", document.Content, StringComparison.Ordinal);
        Assert.Contains("Lens S7-S8 N-SK15", document.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void StandaloneReflectorIsSkippedWithWarning()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[3].IsReflective = true;

        var document = StepCadExporter.Build(
            optic,
            new StepCadExportOptions(SurfaceSamples: 9, AngularSamples: 32));

        Assert.Equal(2, document.PartCount);
        var warning = Assert.Single(document.Warnings, item => item.Contains("反射面", StringComparison.Ordinal));
        Assert.Contains("反射面", warning, StringComparison.Ordinal);
        Assert.Contains("3", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void CoincidentPlaneFacesAreRejectedAsZeroThickness()
    {
        var optic = Optic.CreateCookeTriplet();
        var front = optic.SurfaceGroup.Items[1];
        var back = optic.SurfaceGroup.Items[2];
        front.Geometry = new PlaneGeometry();
        back.Geometry = new PlaneGeometry();
        front.SemiDiameter = 3;
        back.SemiDiameter = 3;
        back.CoordinateSystem = front.CoordinateSystem;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StepCadExporter.Build(
                optic,
                new StepCadExportOptions(SurfaceSamples: 9, AngularSamples: 32)));

        Assert.Contains("S1-S2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedExportPreservesExistingTarget()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"optiland-step-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "existing.step");
        await File.WriteAllTextAsync(path, "existing-content");
        try
        {
            using var application = WorkbenchApplication.Create("cooke");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                application.CadExport.ExportAsync(
                    path,
                    new CadExportOptionsDto(MaximumTrianglesPerPart: 100)));

            Assert.Equal("existing-content", await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.GetFiles(directory, ".*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Vector3D Global(
        OptilandWorkbench.Core.Domain.OpticalSurface surface,
        double x,
        double y) =>
        surface.CoordinateSystem.ToGlobalPoint(new Vector3D(x, y, surface.Geometry.Sag(x, y)));

    private static Vector3D ExtendedRim(
        OptilandWorkbench.Core.Domain.OpticalSurface surface,
        double radius,
        double angle)
    {
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var edgeX = surface.SemiDiameter * cosine;
        var edgeY = surface.SemiDiameter * sine;
        return surface.CoordinateSystem.ToGlobalPoint(new Vector3D(
            radius * cosine,
            radius * sine,
            surface.Geometry.Sag(edgeX, edgeY)));
    }

    private static bool Close(Vector3D left, Vector3D right) =>
        (left - right).Length <= 1e-8;
}
