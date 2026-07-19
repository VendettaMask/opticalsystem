using System.Text;
using System.Text.Json;
using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxImportTests
{
    [Fact]
    public void Optiland058ZemaxFixtureImportsSystemAndPrescription()
    {
        var source = File.ReadAllText(FixturePath("optiland-0.5.8-zemax-reference.zmx"));
        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal("Optiland 0.5.8 Zemax Import Reference", optic.Name);
        Assert.Equal(ApertureKind.EntrancePupilDiameter, optic.Aperture.Kind);
        Assert.Equal(12.5, optic.Aperture.Value, precision: 12);

        Assert.Equal(3, optic.Fields.Count);
        Assert.Equal((0.0, 0.0, 1.0, 0.0, 0.0), FieldValues(optic.Fields[0]));
        Assert.Equal((1.5, 7.0, 0.5, 0.1, 0.15), FieldValues(optic.Fields[1]));
        Assert.Equal((-1.5, 10.0, 0.25, 0.2, 0.25), FieldValues(optic.Fields[2]));

        Assert.Equal(3, optic.Wavelengths.Count);
        Assert.Equal(486.1327, optic.Wavelengths[0].Nanometers, precision: 10);
        Assert.Equal(587.5618, optic.Wavelengths[1].Nanometers, precision: 10);
        Assert.Equal(656.2725, optic.Wavelengths[2].Nanometers, precision: 10);
        Assert.Equal(new[] { false, true, false }, optic.Wavelengths.Select(wavelength => wavelength.IsPrimary));
        Assert.Equal(new[] { 0.5, 1.0, 0.5 }, optic.Wavelengths.Select(wavelength => wavelength.Weight));

        Assert.Equal(5, optic.SurfaceGroup.Items.Count);
        Assert.IsType<PlaneGeometry>(optic.SurfaceGroup.Items[0].Geometry);
        var standard = Assert.IsType<StandardGeometry>(optic.SurfaceGroup.Items[1].Geometry);
        Assert.Equal(50, standard.Radius, precision: 12);
        Assert.True(optic.SurfaceGroup.Items[1].IsStop);
        Assert.Equal(6.25, optic.SurfaceGroup.Items[1].SemiDiameter, precision: 12);

        var evenAsphere = Assert.IsType<EvenAsphereGeometry>(optic.SurfaceGroup.Items[2].Geometry);
        Assert.Equal(-40, evenAsphere.Base.Radius, precision: 12);
        Assert.Equal(-1, evenAsphere.Base.Conic, precision: 12);
        Assert.Equal(1e-6, evenAsphere.Coefficients[0], precision: 15);
        Assert.Equal(-2e-8, evenAsphere.Coefficients[1], precision: 15);

        var toroidal = Assert.IsType<ToroidalGeometry>(optic.SurfaceGroup.Items[3].Geometry);
        Assert.Equal(100, toroidal.TangentialRadius, precision: 12);
        Assert.Equal(80, toroidal.SagittalRadius, precision: 12);
        var importedFlint = Assert.IsType<CatalogGlassMaterial>(optic.SurfaceGroup.Items[3].MaterialAfter);
        Assert.Equal("N-F2", importedFlint.Name);
        Assert.Equal("SCHOTT", importedFlint.Manufacturer);

        var positions = optic.SurfaceGroup.Items.Select(surface => surface.CoordinateSystem.Origin.Z).ToArray();
        Assert.Equal(double.NegativeInfinity, positions[0]);
        Assert.Equal(new[] { 0.0, 4.0, 6.0, 14.0 }, positions.Skip(1));
    }

    [Fact]
    public void ZemaxFixtureMatchesPython058ReferenceContract()
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(
            FixturePath("optiland-0.5.8-zemax-reference.json")));
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(FixturePath("optiland-0.5.8-zemax-reference.zmx")),
            ".zmx");
        var root = expected.RootElement;

        Assert.Equal("0.5.8", root.GetProperty("optiland_version").GetString());
        Assert.Equal(root.GetProperty("aperture").GetProperty("value").GetDouble(), optic.Aperture.Value, precision: 12);

        var expectedFields = root.GetProperty("fields").EnumerateArray().ToArray();
        Assert.Equal(expectedFields.Length, optic.Fields.Count);
        for (var index = 0; index < expectedFields.Length; index++)
        {
            Assert.Equal(expectedFields[index].GetProperty("x").GetDouble(), optic.Fields[index].X, precision: 12);
            Assert.Equal(expectedFields[index].GetProperty("y").GetDouble(), optic.Fields[index].Y, precision: 12);
            Assert.Equal(expectedFields[index].GetProperty("vx").GetDouble(), optic.Fields[index].VignetteFactorX, precision: 12);
            Assert.Equal(expectedFields[index].GetProperty("vy").GetDouble(), optic.Fields[index].VignetteFactorY, precision: 12);
        }

        var expectedWavelengths = root.GetProperty("wavelengths").EnumerateArray().ToArray();
        Assert.Equal(expectedWavelengths.Length, optic.Wavelengths.Count);
        for (var index = 0; index < expectedWavelengths.Length; index++)
        {
            Assert.Equal(
                expectedWavelengths[index].GetProperty("value_um").GetDouble() * 1000,
                optic.Wavelengths[index].Nanometers,
                precision: 10);
            Assert.Equal(
                expectedWavelengths[index].GetProperty("is_primary").GetBoolean(),
                optic.Wavelengths[index].IsPrimary);
        }

        var expectedSurfaces = root.GetProperty("surfaces").EnumerateArray().ToArray();
        Assert.Equal(expectedSurfaces.Length, optic.SurfaceGroup.Items.Count);
        for (var index = 0; index < expectedSurfaces.Length; index++)
        {
            var expectedPosition = expectedSurfaces[index]
                .GetProperty("geometry")
                .GetProperty("position")[2];
            Assert.Equal(ReadPythonNumber(expectedPosition), optic.SurfaceGroup.Items[index].CoordinateSystem.Origin.Z, precision: 12);
            Assert.Equal(
                expectedSurfaces[index].GetProperty("is_stop").GetBoolean(),
                optic.SurfaceGroup.Items[index].IsStop);

            var expectedSag = expectedSurfaces[index]
                .GetProperty("geometry")
                .GetProperty("sag_sample");
            Assert.Equal(
                ReadPythonNumber(expectedSag.GetProperty("z")),
                optic.SurfaceGroup.Items[index].Geometry.Sag(
                    expectedSag.GetProperty("x").GetDouble(),
                    expectedSag.GetProperty("y").GetDouble()),
                precision: 11);
        }
    }

    [Fact]
    public void ZemaxImportSupportsFloatingStopMirrorAndCoordinateBreak()
    {
        const string source = """
            MODE SEQ
            FLOA
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.55 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ 5
            SURF 1
              CURV 0.02
              DISZ 2
              GLAS CUSTOM-Z 0 0 1.7 30
              STOP
              DIAM 4
            SURF 2
              TYPE COORDBRK
              DISZ 3
              PARM 1 1
              PARM 4 90
            SURF 3
              CURV 0
              DISZ 2
              GLAS MIRROR
              DIAM 5
            SURF 4
              CURV 0
              DISZ 0
              GLAS AIR
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal(4, optic.SurfaceGroup.Items.Count);
        Assert.Equal(ApertureKind.EntrancePupilDiameter, optic.Aperture.Kind);
        Assert.Equal(8, optic.Aperture.Value, precision: 12);
        var mirror = optic.SurfaceGroup.Items[2];
        Assert.True(mirror.IsReflective);
        Assert.Equal("CUSTOM-Z", mirror.MaterialBefore.Name);
        Assert.Equal("CUSTOM-Z", mirror.MaterialAfter.Name);
        var customGlass = Assert.IsType<AbbeMaterial>(mirror.MaterialAfter);
        Assert.Equal(1.7, customGlass.Nd, precision: 12);
        Assert.Equal(30, customGlass.Vd, precision: 12);
        Assert.Equal(4, mirror.CoordinateSystem.Origin.X, precision: 10);
        Assert.Equal(0, mirror.CoordinateSystem.Origin.Y, precision: 10);
        Assert.Equal(2, mirror.CoordinateSystem.Origin.Z, precision: 10);
        Assert.Equal(90, mirror.CoordinateSystem.RotationYDegrees, precision: 10);

        var image = optic.SurfaceGroup.Items[3];
        Assert.Equal(6, image.CoordinateSystem.Origin.X, precision: 10);
        Assert.Equal(2, image.CoordinateSystem.Origin.Z, precision: 10);
    }

    [Fact]
    public async Task WorkbenchFilePathImportDetectsBomlessUtf16Zemax()
    {
        var source = File.ReadAllText(FixturePath("optiland-0.5.8-zemax-reference.zmx"));
        var path = Path.Combine(Path.GetTempPath(), $"optiland-zemax-{Guid.NewGuid():N}.zmx");
        try
        {
            await File.WriteAllBytesAsync(path, Encoding.Unicode.GetBytes(source));

            var optic = await OptilandConnector.ReadOpticAsync(path);

            Assert.Equal(5, optic.SurfaceGroup.Items.Count);
            Assert.Equal(12.5, optic.Aperture.Value, precision: 12);
            Assert.Equal(587.5618, Assert.Single(optic.Wavelengths, item => item.IsPrimary).Nanometers, precision: 10);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("EVENASPH", 0.048125)]
    [InlineData("ODDASPHE", 0.009925)]
    public void ZemaxAsphereParametersUseOptilandCoefficientOrders(string surfaceType, double expectedSag)
    {
        var source = $"""
            MODE SEQ
            ENPD 10
            SURF 0
              TYPE {surfaceType}
              CURV 0
              DISZ 0
              PARM 1 0.002
              PARM 2 -0.000003
            SURF 1
              CURV 0
              DISZ 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal(expectedSag, optic.SurfaceGroup.Items[0].Geometry.Sag(3, 4), precision: 12);
    }

    [Theory]
    [InlineData("MODE NSC\nENPD 10\nSURF 0\nSURF 1", "MODE SEQ")]
    [InlineData("MODE SEQ\nENPD 10\nSURF 0\nTYPE BINARY_2\nSURF 1", "BINARY_2")]
    [InlineData("MODE SEQ\nENPD 10\nSURF 0\nSURF 1\nDISZ -2", "Negative Zemax thickness")]
    [InlineData("MODE SEQ\nENPD 10\nFTYP 0 0 1 1 0 0 1\nSURF 0\nSURF 1", "afocal image space")]
    public void ZemaxImportRejectsUnsupportedPhysicalContracts(string source, string expectedMessage)
    {
        var exception = Assert.ThrowsAny<Exception>(() => OpticalFormatCatalog.Import(source, ".zmx"));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    private static (double X, double Y, double Weight, double VignetteX, double VignetteY) FieldValues(
        Core.Domain.FieldPoint field) =>
        (field.X, field.Y, field.Weight, field.VignetteFactorX, field.VignetteFactorY);

    private static double ReadPythonNumber(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.GetDouble();
        }

        return element.GetString() switch
        {
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            var value => throw new InvalidDataException($"Unexpected Python numeric token '{value}'.")
        };
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
