using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class CadExportReliabilityTests
{
    [Fact]
    public void CadMeshUsesActualSagAndSurfaceCoordinateSystems()
    {
        var geometries = new IGeometry[]
        {
            new EvenAsphereGeometry(40, -0.4, new[] { 2e-6, -1e-9 }),
            new BiconicGeometry(45, 55, -0.2, -0.1),
            new ToroidalGeometry(60, 35)
        };

        foreach (var geometry in geometries)
        {
            var optic = Optic.CreateCookeTriplet();
            var front = optic.SurfaceGroup.Items[1];
            var back = optic.SurfaceGroup.Items[2];
            front.Geometry = geometry;
            front.SemiDiameter = 3;
            back.SemiDiameter = 4;
            front.CoordinateSystem = front.CoordinateSystem with
            {
                Origin = front.CoordinateSystem.Origin + new Vector3D(1.25, -0.75, 0),
                RotationXDegrees = 3,
                RotationYDegrees = -2
            };
            back.CoordinateSystem = back.CoordinateSystem with
            {
                Origin = back.CoordinateSystem.Origin + new Vector3D(1.25, -0.75, 0),
                RotationXDegrees = 3,
                RotationYDegrees = -2
            };

            var build = CadLensMeshBuilder.Build(
                optic,
                new StepCadExportOptions(
                    SurfaceSamples: 9,
                    AngularSamples: 32,
                    MaximumChordErrorMillimeters: 0.005),
                CancellationToken.None);

            var mesh = build.Parts[0];
            var expectedFrontCenter = front.CoordinateSystem.ToGlobalPoint(
                new Vector3D(0, 0, front.Geometry.Sag(0, 0)));
            var expectedFrontRim = front.CoordinateSystem.ToGlobalPoint(
                new Vector3D(
                    front.SemiDiameter,
                    0,
                    front.Geometry.Sag(front.SemiDiameter, 0)));
            var expectedBackRim = back.CoordinateSystem.ToGlobalPoint(
                new Vector3D(
                    back.SemiDiameter,
                    0,
                    back.Geometry.Sag(back.SemiDiameter, 0)));

            Assert.Contains(mesh.Vertices, point => Distance(point, expectedFrontCenter) <= 1e-8);
            Assert.Contains(mesh.Vertices, point => Distance(point, expectedFrontRim) <= 1e-8);
            Assert.Contains(mesh.Vertices, point => Distance(point, expectedBackRim) <= 1e-8);
            Assert.InRange(mesh.MaximumChordErrorMillimeters, 0, 0.005);
            Assert.True(SignedVolume(mesh) > 0);
        }
    }

    [Fact]
    public void StepDocumentContainsSeparateNamedAssemblyPartsAndUnicode()
    {
        var document = StepCadExporter.Build(
            Optic.CreateCookeTriplet(),
            new StepCadExportOptions(
                SurfaceSamples: 9,
                AngularSamples: 32,
                ProductName: "三片式镜头",
                CreatedUtc: new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(3, document.PartCount);
        Assert.True(document.VertexCount > 0);
        Assert.True(document.TriangleCount > 0);
        Assert.Equal(
            1,
            Count(document.Content, "PRODUCT('optical-system',"));
        Assert.Equal(
            document.PartCount,
            Count(document.Content, "NEXT_ASSEMBLY_USAGE_OCCURRENCE("));
        Assert.Equal(
            document.PartCount,
            Count(document.Content, "MANIFOLD_SOLID_BREP("));
        Assert.Contains("SHAPE_REPRESENTATION_RELATIONSHIP", document.Content, StringComparison.Ordinal);
        Assert.Contains(@"\X2\", document.Content, StringComparison.Ordinal);
        Assert.Contains("ADVANCED_FACE('',", document.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void CadMeshRejectsOpaqueGeometryAndTriangleLimit()
    {
        var opaque = Optic.CreateCookeTriplet();
        opaque.SurfaceGroup.Items[1].Geometry = new OpaqueGeometryPayload(
            ComponentSnapshot.Empty("unsupported-freeform"));
        var unsupported = Assert.Throws<OpticCapabilityException>(() =>
            StepCadExporter.Build(opaque));
        Assert.Contains("表面 1", unsupported.Message, StringComparison.Ordinal);
        Assert.Contains("unsupported-freeform", unsupported.Message, StringComparison.Ordinal);

        var limited = Assert.Throws<InvalidOperationException>(() =>
            StepCadExporter.Build(
                Optic.CreateCookeTriplet(),
                new StepCadExportOptions(MaximumTrianglesPerPart: 100)));
        Assert.Contains("三角形", limited.Message, StringComparison.Ordinal);
        Assert.Contains("S1-S2", limited.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CadMeshRejectsNonFiniteSagWithSurfaceAndCoordinates()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].Geometry = new NonFiniteGeometry();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StepCadExporter.Build(optic));

        Assert.Contains("表面 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("非有限 Sag", exception.Message, StringComparison.Ordinal);
        Assert.Contains("(", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledAtomicExportPreservesExistingTarget()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"optiland-step-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "existing.step");
        await File.WriteAllTextAsync(path, "existing-content");
        try
        {
            using var application = WorkbenchApplication.Create("cooke");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                application.CadExport.ExportAsync(
                    path,
                    new CadExportOptionsDto(),
                    cancellation.Token));

            Assert.Equal("existing-content", await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.GetFiles(directory, ".*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CadMeshHonorsCancellationBeforeGeometryWork()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            StepCadExporter.Build(
                Optic.CreateCookeTriplet(),
                cancellationToken: cancellation.Token));
    }

    private static double SignedVolume(CadLensMesh mesh)
    {
        var volume = 0.0;
        foreach (var triangle in mesh.Triangles)
        {
            var a = mesh.Vertices[triangle.A];
            var b = mesh.Vertices[triangle.B];
            var c = mesh.Vertices[triangle.C];
            volume += Dot(a, Cross(b, c)) / 6.0;
        }

        return volume;
    }

    private static int Count(string value, string pattern) =>
        value.Split(pattern, StringSplitOptions.None).Length - 1;

    private static double Distance(Vector3D left, Vector3D right) => (left - right).Length;

    private static Vector3D Cross(Vector3D left, Vector3D right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));

    private static double Dot(Vector3D left, Vector3D right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private sealed class NonFiniteGeometry : IGeometry
    {
        public string Kind => "non-finite-test";

        public double Sag(double x, double y) => double.NaN;

        public double? DistanceToIntersection(Vector3D origin, Vector3D direction) => null;

        public Vector3D SurfaceNormal(Vector3D localPoint) => new(0, 0, 1);

        public IGeometry Clone() => new NonFiniteGeometry();
    }
}
