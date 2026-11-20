using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class ExtendedSurfaceImportTests
{
    [Theory]
    [InlineData("XASPHERE", "MM", 1)]
    [InlineData("XASPHERE", "CM", 10)]
    [InlineData("XASPHERE", "IN", 25.4)]
    [InlineData("XOSPHERE", "MM", 1)]
    [InlineData("XOSPHERE", "CM", 10)]
    [InlineData("XOSPHERE", "IN", 25.4)]
    [InlineData("XPOLYNOM", "MM", 1)]
    [InlineData("XPOLYNOM", "CM", 10)]
    [InlineData("XPOLYNOM", "IN", 25.4)]
    public async Task SagNormalAndRealRaysSurviveUnitsAndBothFileRoundTrips(string type, string unit, double scale)
    {
        var source = Source(type, unit, """
            PARM 13 5
            PARM 14 3
            PARM 15 0
            PARM 16 0
            PARM 17 .002
            PARM 18 0
            PARM 19 .003
            """);
        var original = OpticalFormatCatalog.Import(source, ".zmx");
        var path = Path.Combine(Path.GetTempPath(), $"extended-surface-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([original], 0), path);
            var project = await StarOptProjectStore.LoadAsync(path);
            var candidates = new[]
            {
                original, project.Configurations[0],
                OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(original, ".zmx"), ".zmx")
            };
            foreach (var optic in candidates)
            {
                var geometry = Assert.IsType<NormalizedPolynomialGeometry>(optic.SurfaceGroup.Items[1].Geometry);
                Assert.Equal(3 * scale, geometry.NormalizationRadius, 12);
                Assert.Equal(.002 * scale, geometry.Coefficients[2], 12);
                foreach (var (x, y) in new[] { (0d, 0d), (.2, -.4), (-.7, .3), (1.1, .8) })
                {
                    var expected = ExpectedSag(type, x, y);
                    Assert.Equal(scale * expected, geometry.Sag(scale * x, scale * y), 10);
                    const double h = 1e-5;
                    var dx = (ExpectedSag(type, x + h, y) - ExpectedSag(type, x - h, y)) / (2 * h);
                    var dy = (ExpectedSag(type, x, y + h) - ExpectedSag(type, x, y - h)) / (2 * h);
                    var expectedNormal = new Vector3D(-dx, -dy, 1);
                    expectedNormal /= expectedNormal.Length;
                    var point = new Vector3D(scale * x, scale * y, scale * expected);
                    var normal = geometry.SurfaceNormal(point);
                    Assert.InRange((normal - expectedNormal).Length, 0, 1e-9);
                    var hit = geometry.DistanceToIntersection(new(scale * x, scale * y, -scale), new(0, 0, 1));
                    Assert.True(hit.IsHit, hit.Status.ToString());
                    Assert.Equal(scale * expected, hit.Point.Z, 9);
                }
                var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, .2, .4, .5876, false).Rays.Single();
                var frontHit = optic.SequentialRayTracer.TraceToSurface(ray, 1)!;
                var frontNormal = geometry.SurfaceNormal(frontHit.Position);
                var eta = 1 / optic.SurfaceGroup.Items[1].MaterialAfter.RefractiveIndex(587.6);
                var cosine = frontNormal.Z;
                var refracted = new Vector3D(0, 0, eta)
                    + frontNormal * (Math.Sqrt(1 - eta * eta * (1 - cosine * cosine)) - eta * cosine);
                Assert.InRange((frontHit.Direction - refracted).Length, 0, 1e-9);
                var traced = optic.SequentialRayTracer.TraceToSurface(ray, 2);
                Assert.NotNull(traced);
                Assert.True(double.IsFinite(traced.Position.Y));
                var vertexCurvature = .02 + (type == "XPOLYNOM" ? 2 * .003 / 9 : 0);
                var expectedFocalLength = scale / ((1 / eta - 1) * vertexCurvature);
                Assert.Equal(expectedFocalLength, optic.Paraxial.EstimateEffectiveFocalLength(), 7);
                var radius = optic.SurfaceGroup.Items[1].Radius;
                optic.SurfaceGroup.Items[1].Radius = radius * 2;
                var edited = Assert.IsType<NormalizedPolynomialGeometry>(optic.SurfaceGroup.Items[1].Geometry);
                Assert.Equal(radius * 2, edited.Base.Radius);
                Assert.Equal(geometry.Coefficients, edited.Coefficients);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("MM", 1)]
    [InlineData("CM", 10)]
    [InlineData("IN", 25.4)]
    public void BiconicMapsPrimaryColumnsToYAndRetainsXWhenEditing(string unit, double scale)
    {
        var optic = OpticalFormatCatalog.Import(Source("BICONICX", unit, "PARM 1 40\nPARM 2 -.3"), ".zmx");
        foreach (var candidate in new[] { optic, Optic.FromSnapshot(optic.ToSnapshot()),
                     OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(optic, ".zmx"), ".zmx") })
        {
            var surface = candidate.SurfaceGroup.Items[1];
            var geometry = Assert.IsType<BiconicGeometry>(surface.Geometry);
            Assert.Equal(40 * scale, geometry.RadiusX);
            Assert.Equal(50 * scale, surface.Radius);
            Assert.Equal(-.3, geometry.ConicX);
            Assert.Equal(-.2, surface.Conic);
            var x = .5;
            var y = -.8;
            var sag = (x * x / 40 + y * y / 50)
                / (1 + Math.Sqrt(1 - .7 * x * x / 1600 - .8 * y * y / 2500));
            Assert.Equal(sag * scale, geometry.Sag(x * scale, y * scale), 11);
            surface.Radius = 80 * scale;
            surface.Conic = -.6;
            var edited = Assert.IsType<BiconicGeometry>(surface.Geometry);
            Assert.Equal(40 * scale, edited.RadiusX);
            Assert.Equal(-.3, edited.ConicX);
            Assert.Equal(80 * scale, edited.RadiusY);
            Assert.Equal(-.6, edited.ConicY);
        }
    }

    [Theory]
    [InlineData("XASPHERE", 240, 480)]
    [InlineData("XOSPHERE", 240, 240)]
    [InlineData("XPOLYNOM", 230, 20)]
    public void HighestOrderTermsRemainFiniteWithoutCoefficientRescaling(string type, int count, int power)
    {
        var parameters = $"XDAT 1 {count}\nXDAT 2 .001\nXDAT {count + 2} .01";
        var optic = OpticalFormatCatalog.Import(Source(type, "MM", parameters).Replace("CURV .02", "CURV 0"), ".zmx");
        var geometry = Assert.IsType<NormalizedPolynomialGeometry>(optic.SurfaceGroup.Items[1].Geometry);
        var y = .000999;
        Assert.Equal(.01 * Math.Pow(.999, power), geometry.Sag(0, y), 14);
        var normal = geometry.SurfaceNormal(new(0, y, geometry.Sag(0, y)));
        Assert.InRange(normal.Y, -1, 0);
        Assert.True(double.IsFinite(normal.Z));
        var saved = Optic.FromSnapshot(optic.ToSnapshot());
        Assert.Equal(geometry.Sag(0, y), saved.SurfaceGroup.Items[1].Geometry.Sag(0, y));
    }

    [Fact]
    public void InactiveCoefficientsStaySavedAndMultiConfigurationOverridesUseFileUnits()
    {
        var source = Source("XASPHERE", "CM", """
            PARM 13 2
            PARM 14 3
            PARM 15 .01
            PARM 16 .02
            PARM 17 999
            """) + "\nMNUM 2\nPRAM 1 2 .04 0 16";
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        Assert.Equal(2, imported.Configurations.Count);
        var a = Assert.IsType<NormalizedPolynomialGeometry>(imported.Configurations[0].SurfaceGroup.Items[1].Geometry);
        var b = Assert.IsType<NormalizedPolynomialGeometry>(imported.Configurations[1].SurfaceGroup.Items[1].Geometry);
        Assert.Equal(2, b.TermCount);
        Assert.Equal(9990, b.Coefficients[2]);
        Assert.Equal(.4, b.Coefficients[1]);
        Assert.Equal(.2 * Math.Pow(.5, 4), b.Sag(15, 0) - a.Sag(15, 0), 11);
        var restored = OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(imported.Configurations[1], ".zmx"), ".zmx");
        var saved = Assert.IsType<NormalizedPolynomialGeometry>(restored.SurfaceGroup.Items[1].Geometry);
        Assert.Equal(b.Coefficients, saved.Coefficients);
        Assert.Equal(b.Sag(15, 0), saved.Sag(15, 0));
    }

    [Theory]
    [InlineData("XASPHERE", "XDAT 1 241\nXDAT 2 1")]
    [InlineData("XPOLYNOM", "XDAT 1 231\nXDAT 2 1")]
    [InlineData("XOSPHERE", "XDAT 1 2.5\nXDAT 2 1")]
    [InlineData("XASPHERE", "XDAT 1 1\nXDAT 2 0")]
    [InlineData("XPOLYNOM", "XDAT 1 1\nXDAT 2 -1")]
    public void InvalidDefinitionsCannotBecomeExecutableSurfaces(string type, string parameters) =>
        Assert.ThrowsAny<Exception>(() => OpticalFormatCatalog.Import(Source(type, "MM", parameters), ".zmx"));

    [Theory]
    [InlineData("XASPHERE")]
    [InlineData("XOSPHERE")]
    [InlineData("XPOLYNOM")]
    [InlineData("BICONICX")]
    public void UnmappedParametersStayOpaque(string type)
    {
        var optic = OpticalFormatCatalog.Import(Source(type, "MM", "PARM 3 7"), ".zmx");
        Assert.IsType<OpaqueGeometryPayload>(optic.SurfaceGroup.Items[1].Geometry);
    }

    [Fact]
    public void OddLinearCuspHasNoFabricatedNormal()
    {
        var geometry = new NormalizedPolynomialGeometry(0, 0, 2, SagPolynomialBasis.Radial, 1, [.01]);
        Assert.Equal(0, geometry.Sag(0, 0));
        Assert.True(double.IsNaN(geometry.SurfaceNormal(Vector3D.Zero).Z));
        Assert.False(geometry.DistanceToIntersection(new(0, 0, -1), new(0, 0, 1)).IsHit);
    }

    [Fact]
    public void NewParametersCommitTogetherUndoAndRejectInvalidUpdates()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Prescription.GetSurfaces()[1];
        var update = new SurfaceComponentUpdateDto("扩展偶次非球面", before.ApertureKind, 1, 1, 0, 50,
            Polynomial: new NormalizedPolynomialUpdateDto(4, 2, [.001, -.0002, 123]));
        app.Prescription.UpdateSurfaceComponents(1, update);
        var after = app.Prescription.GetSurfaces()[1];
        Assert.Equal("extended_asphere", after.GeometryDefinition!.Kind);
        Assert.Equal(123, after.GeometryDefinition.Numbers["c2"]);
        Assert.True(app.Documents.Undo());
        Assert.Equal(before.GeometryKind, app.Prescription.GetSurfaces()[1].GeometryKind);
        Assert.True(app.Documents.Redo());
        Assert.Equal(after.GeometryDefinition, app.Prescription.GetSurfaces()[1].GeometryDefinition);
        var revision = app.Events.Revision;
        Assert.Throws<ArgumentException>(() => app.Prescription.UpdateSurfaceComponents(1,
            update with { Polynomial = new NormalizedPolynomialUpdateDto(0, 2, [.001, .002]) }));
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(after.GeometryDefinition, app.Prescription.GetSurfaces()[1].GeometryDefinition);
    }

    private static double ExpectedSag(string type, double x, double y)
    {
        var r2 = x * x + y * y;
        var baseSag = .02 * r2 / (1 + Math.Sqrt(1 - .8 * .02 * .02 * r2));
        if (type == "XPOLYNOM")
            return baseSag + .002 * x * x / 9 + .003 * y * y / 9;
        var rho = Math.Sqrt(r2) / 3;
        var argument = type == "XASPHERE" ? rho * rho : rho;
        return baseSag + .002 * Math.Pow(argument, 3) + .003 * Math.Pow(argument, 5);
    }

    [Fact]
    public void CartesianTiltAndMixedTermsTraceButDoNotInventScalarFirstOrder()
    {
        var optic = OpticalFormatCatalog.Import(Source("XPOLYNOM", "MM",
            "XDAT 1 5\nXDAT 2 3\nXDAT 3 .01\nXDAT 4 -.02\nXDAT 5 .003\nXDAT 6 .004\nXDAT 7 .005"), ".zmx");
        var geometry = Assert.IsType<NormalizedPolynomialGeometry>(optic.SurfaceGroup.Items[1].Geometry);
        const double x = .3;
        const double y = -.6;
        var sag = ExpectedSag("XPOLYNOM", x, y) - .002 * x * x / 9 - .003 * y * y / 9
            + .01 * x / 3 - .02 * y / 3 + .003 * x * x / 9 + .004 * x * y / 9 + .005 * y * y / 9;
        Assert.Equal(sag, geometry.Sag(x, y), 12);
        Assert.True(geometry.DistanceToIntersection(new(x, y, -1), new(0, 0, 1)).IsHit);
        Assert.Throws<NotSupportedException>(() => optic.Paraxial.EstimateEffectiveFocalLength());
    }

    private static string Source(string type, string unit, string parameters) => $"""
        MODE SEQ
        UNIT {unit}
        ENPD 1
        WAVM 1 .5876 1
        PWAV 1
        SURF 0
          TYPE STANDARD
          DISZ INFINITY
        SURF 1
          STOP
          TYPE {type}
          CURV .02
          CONI -.2
          DIAM 2 1
          GLAS N-BK7
          {parameters}
          DISZ 3
        SURF 2
          TYPE STANDARD
          DIAM 2 1
        """;
}
