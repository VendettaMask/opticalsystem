using System.Text;
using System.Text.Json;
using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Visualization;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxImportTests
{
    [Fact]
    public void ZemaxSpecificMeritRowsArePreservedAsDisabledReadOnlyRecords()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            SURF 0
              DISZ 100
            SURF 1
              STOP
              DISZ 0
            CONF 2 0 0 0 0 0 0 0 0 0
            MNCA 1 1 0 0 0 0 1 1 0 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Collection(
            optic.MeritFunctionOperands,
            operand =>
            {
                Assert.Equal("CONF", operand.Type);
                Assert.False(operand.Enabled);
                Assert.Contains("Zemax 只读记录", operand.Comment, StringComparison.Ordinal);
            },
            operand =>
            {
                Assert.Equal("MNCA", operand.Type);
                Assert.False(operand.Enabled);
                Assert.Equal(1, operand.Surface);
                Assert.Equal(1, operand.Wavelength);
                Assert.Equal(1, operand.Target);
                Assert.Equal(1, operand.Weight);
            });
    }

    [Fact]
    public void ZemaxMeritFunctionRowsImportInOriginalOrder()
    {
        const string source = """
            MODE SEQ
            NAME "Merit import"
            ENPD 10
            FTYP 0 0 2 2 0 0 0
            XFLN 0 0
            YFLN 0 10
            FWGN 1 1
            WAVM 1 0.4861327 1
            WAVM 2 0.5875618 1
            PWAV 2
            SURF 0
              CURV 0
              DISZ 20
            SURF 1
              CURV 0
              DISZ 0
            DMFS 0 0 0 0 0 0 0 0 0 0
            BLNK 序列评价函数: RMS 波前差：质心参考高斯求积 3 环 6 臂
            BLNK 视场操作数 2.
            OPDX 0 2 0 0.7142857142857143 0.16785534350986436 0.29073398328101191 0 -0.032320912073968894 0 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        Assert.Collection(
            optic.MeritFunctionOperands,
            operand =>
            {
                Assert.Equal("DMFS", operand.Type);
                Assert.False(operand.Enabled);
            },
            operand =>
            {
                Assert.Equal("BLNK", operand.Type);
                Assert.Equal("序列评价函数: RMS 波前差：质心参考高斯求积 3 环 6 臂", operand.Comment);
            },
            operand =>
            {
                Assert.Equal("BLNK", operand.Type);
                Assert.Equal("视场操作数 2.", operand.Comment);
            },
            operand =>
            {
                Assert.Equal("OPDX", operand.Type);
                Assert.Equal(0, operand.Surface);
                Assert.Equal(2, operand.Wavelength);
                Assert.Equal(0, operand.Hx, precision: 12);
                Assert.Equal(0.7142857142857143, operand.Hy, precision: 12);
                Assert.Equal(0.16785534350986436, operand.Px, precision: 12);
                Assert.Equal(0.29073398328101191, operand.Py, precision: 12);
                Assert.Equal(0, operand.Target, precision: 12);
                Assert.Equal(-0.032320912073968894, operand.Weight, precision: 12);
            });
    }

    public static IEnumerable<object[]> SampleLensFiles()
    {
        yield return new object[] { "achromatic-doublet.zmx", FieldDefinitionKind.Angle, 5, 2 };
        yield return new object[] { "double-gauss-50mm.zmx", FieldDefinitionKind.Angle, 11, 4 };
        yield return new object[] { "telephoto-four-element.zmx", FieldDefinitionKind.Angle, 9, 4 };
        yield return new object[] { "finite-conjugate-macro.zmx", FieldDefinitionKind.ObjectHeight, 9, 3 };
        yield return new object[] { "real-image-height-demo.zmx", FieldDefinitionKind.RealImageHeight, 5, 2 };
    }

    [Theory]
    [MemberData(nameof(SampleLensFiles))]
    public void SampleLensFilesUseCatalogGlassAndTraceEveryDefinedField(
        string fileName,
        FieldDefinitionKind expectedFieldDefinition,
        int expectedSurfaceCount,
        int expectedGlassSurfaceCount)
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", fileName));
        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal(expectedFieldDefinition, optic.FieldDefinition);
        Assert.Equal(expectedSurfaceCount, optic.SurfaceGroup.Items.Count);
        Assert.Equal(3, optic.Fields.Count);
        Assert.Equal(3, optic.Wavelengths.Count);
        Assert.Equal(
            expectedGlassSurfaceCount,
            optic.SurfaceGroup.Items.Count(surface => surface.MaterialAfter is CatalogGlassMaterial));

        var wavelength = optic.Wavelengths.Single(item => item.IsPrimary).Micrometers;
        foreach (var field in optic.Fields)
        {
            var normalized = FieldCoordinates.Normalize(optic.Fields, field.X, field.Y);
            var history = optic.TraceGeneric(normalized.X, normalized.Y, 0, 0, wavelength)
                .RayHistories.Single();
            var final = Assert.Single(history, sample => sample.SurfaceNumber == optic.SurfaceGroup.Items[^1].Number);

            Assert.False(final.Vignetted);
            Assert.True(final.Intensity > 0);
            Assert.True(double.IsFinite(final.Position.X));
            Assert.True(double.IsFinite(final.Position.Y));
            Assert.True(double.IsFinite(final.Position.Z));

            if (expectedFieldDefinition == FieldDefinitionKind.RealImageHeight)
            {
                var local = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(final.Position);
                Assert.Equal(field.X, local.X, precision: 8);
                Assert.Equal(field.Y, local.Y, precision: 8);
            }
        }

        var scene = new Layout2DBuilder(optic).Build3D(options: new LayoutBuildOptions(
            FieldIndex: optic.Fields.Count - 1,
            WavelengthIndex: optic.Wavelengths.ToList().FindIndex(item => item.IsPrimary),
            RayCount: 3));
        Assert.NotEmpty(scene.LensElements);
        Assert.NotEmpty(scene.Rays);
    }

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
    public void ZemaxImportWithoutGcatUsesDefaultSchottGlassPriority()
    {
        const string source = """
            MODE SEQ
            ENPD 8
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ 20
            SURF 1
              CURV 0.02
              DISZ 3
              GLAS F2
              DIAM 4
            SURF 2
              CURV -0.02
              DISZ 15
              GLAS AIR
            SURF 3
              CURV 0
              DISZ 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var glass = Assert.Single(optic.SurfaceGroup.Items
            .Select(surface => surface.MaterialAfter)
            .OfType<CatalogGlassMaterial>());

        Assert.Equal("SCHOTT", glass.Manufacturer);
        Assert.Equal("F2", glass.CatalogName);
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
        Assert.Equal(ApertureKind.FloatByStopSize, optic.Aperture.Kind);
        Assert.Equal(4, optic.Aperture.Value, precision: 12);
        Assert.Equal(8, optic.Paraxial.EstimateEntrancePupilDiameter(), precision: 12);
        var exported = OpticalFormatCatalog.Export(optic, ".zmx");
        Assert.Contains("FLOA", exported, StringComparison.Ordinal);
        Assert.Equal(
            ApertureKind.FloatByStopSize,
            OpticalFormatCatalog.Import(exported, ".zmx").Aperture.Kind);
        var mirror = optic.SurfaceGroup.Items[2];
        Assert.True(mirror.IsReflective);
        Assert.Equal("MIRROR", mirror.Material);
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
    public void ZemaxRealImageHeightFieldsImportAndRoundTrip()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            FTYP 3 0 2 1 0 0 0
            XFLN 0 2.5
            YFLN 0 4.25
            FWGN 1 0.5
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ INFINITY
            SURF 1
              CURV 0.02
              DISZ 5
              GLAS N-BK7
              STOP
              DIAM 5
            SURF 2
              CURV -0.02
              DISZ 25
              GLAS AIR
            SURF 3
              CURV 0
              DISZ 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");
        var exported = OpticalFormatCatalog.Export(optic, ".zmx");
        var restored = OpticalFormatCatalog.Import(exported, ".zmx");

        Assert.Equal(FieldDefinitionKind.RealImageHeight, optic.FieldDefinition);
        Assert.Equal(FieldDefinitionKind.RealImageHeight, restored.FieldDefinition);
        Assert.Equal(new[] { 0.0, 2.5 }, optic.Fields.Select(field => field.X));
        Assert.Equal(new[] { 0.0, 4.25 }, optic.Fields.Select(field => field.Y));
        Assert.Contains("FTYP 3", exported, StringComparison.Ordinal);

        var normalized = FieldCoordinates.Normalize(optic.Fields, 2.5, 4.25);
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary).Micrometers;
        var final = optic.TraceGeneric(normalized.X, normalized.Y, 0, 0, wavelength).RayHistories.Single()[^1];
        var local = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(final.Position);
        Assert.Equal(2.5, local.X, precision: 8);
        Assert.Equal(4.25, local.Y, precision: 8);
    }

    [Fact]
    public void ZemaxMirrMetadataTracesForwardAndMultiConfigurationsArePreserved()
    {
        const string source = """
            MODE SEQ
            NAME Multi configuration import
            ENPD 10
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 0
            WAVM 1 0.5875618 1
            PWAV 1
            SURF 0
              CURV 0
              DISZ 200
              DIAM 20
              MIRR 2 0
            SURF 1
              TYPE EVENASPH
              CURV 0.02
              DISZ 15
              GLAS N-BK7
              DIAM 8
              PARM 1 0
              STOP
              MIRR 2 0
            SURF 2
              CURV -0.02
              DISZ 30
              GLAS AIR
              DIAM 8
              MIRR 2 0
            SURF 3
              CURV 0
              DISZ 0
              DIAM 12
              MIRR 2 0
            MNUM 3 2
            THIC 0 1 100 0 0 0 1 1 1 0 0 "" 0
            THIC 0 2 150 0 0 0 1 1 1 0 0 "" 0
            THIC 0 3 200 0 0 0 1 1 1 0 0 "" 0
            THIC 1 1 5 0 0 0 1 1 1 0 0 "" 0
            THIC 1 2 10 0 0 0 1 1 1 0 0 "" 0
            THIC 1 3 15 0 0 0 1 1 1 0 0 "" 0
            PRAM 1 1 0.000001 0 1 0 1 1 1 0 0 "" 0
            PRAM 1 2 0.000002 0 1 0 1 1 1 0 0 "" 0
            PRAM 1 3 0.000003 0 1 0 1 1 1 0 0 "" 0
            """;

        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);

        Assert.Equal(3, imported.Configurations.Count);
        Assert.Equal(2, imported.ActiveConfigurationIndex);
        Assert.Same(imported.Configurations[2], imported.ActiveOptic);
        Assert.Equal(new[] { 100.0, 150.0, 200.0 }, imported.Configurations
            .Select(configuration => configuration.SurfaceGroup.Items[0].Thickness));
        Assert.Equal(new[] { 5.0, 10.0, 15.0 }, imported.Configurations
            .Select(configuration => configuration.SurfaceGroup.Items[1].Thickness));
        Assert.Equal(new[] { 0.000001, 0.000002, 0.000003 }, imported.Configurations
            .Select(configuration => Assert.IsType<EvenAsphereGeometry>(
                configuration.SurfaceGroup.Items[1].Geometry).Coefficients[0]));
        Assert.All(imported.ActiveOptic.SurfaceGroup.Items, surface => Assert.False(surface.IsReflective));

        var scene = new Layout2DBuilder(imported.ActiveOptic).Build(options: new LayoutBuildOptions(
            FirstSurface: 1,
            LastSurface: 3,
            RayCount: 3));
        Assert.NotEmpty(scene.Rays);
        Assert.All(scene.Rays, ray => Assert.True(ray.Points.Count >= 2));

        var connector = new OptilandConnector(Optic.CreateBlank());
        connector.ApplyLoadedDocument(new LoadedOpticalDocument(
            imported.ActiveOptic,
            imported.Configurations,
            imported.ActiveConfigurationIndex), "multi.zmx");
        var rows = connector.GetMultiConfigurationRows();
        Assert.Equal(3, rows.Count);
        Assert.Equal(2, Assert.Single(rows, row => row.Active).Index);
        Assert.Equal(new[] { "配置 1", "配置 2", "配置 3" }, rows.Select(row => row.Name));
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

    [Fact]
    public async Task LensVa3StyleImportPreservesFieldsConfigurationsCurvaturesAndMeritRows()
    {
        const string source = """
            VERS 241210 1439 20120530 20120530
            MODE SEQ
            NAME
            FNUM 2.7 0
            GCAT CDGM
            FTYP 3 0 5 3 0 0 0 5
            XFLN 0 0 0 0 0
            YFLN 0 4.5 3.375 2.25 1.125
            FWGN 1 1 1 1 1
            FCOM 1 轴上视场
            FCOM 2 最大Y视场
            WAVM 1 0.42 1
            WAVM 2 0.44 1
            WAVM 3 0.46 1
            WAVM 4 0.48 1
            PWAV 2
            SURF 0
              TYPE STANDARD
              CURV 0
              DISZ 2500
            SURF 1
              TYPE STANDARD
              CURV 0.025
              DISZ 3
              GLAS H-K9L 0 0 1.5 40
              STOP
              DIAM 1000
            SURF 2
              TYPE STANDARD
              CURV 0
              DISZ 0
            EFFL 0 2 0 0 0 0 10.7 1 0 0
            DMFS 0 0 0 0 0 0 0 0 0 0
            BLNK 对比度于185 lp/MM
            MECS 0 1 1 185 0.33571068701972878 0 0 0.058177641733144006 0 0
            MECT 0 1 1 185 0.33571068701972878 0 0 0.058177641733144006 0 0
            MNUM 2 2
            CRVT 1 1 0.02 0 0 0 1 1 1 0 0 "" 0
            CRVT 1 2 0.025 0 0 0 1 1 1 0 0 "" 0
            THIC 0 1 500 0 0 0 1 1 1 0 0 "" 0
            THIC 0 2 2500 0 0 0 1 1 1 0 0 "" 0
            """;
        var zmxPath = Path.Combine(Path.GetTempPath(), $"lens-va3-{Guid.NewGuid():N}.zmx");
        var projectPath = Path.Combine(Path.GetTempPath(), $"lens-va3-{Guid.NewGuid():N}.staropt");
        try
        {
            await File.WriteAllBytesAsync(zmxPath, Encoding.Unicode.GetBytes(source));

            var imported = await OptilandConnector.ReadDocumentAsync(zmxPath);

            Assert.Equal(2, imported.Configurations.Count);
            Assert.Equal(1, imported.ActiveConfigurationIndex);
            Assert.Equal(new[] { 500.0, 2500.0 }, imported.Configurations
                .Select(configuration => configuration.SurfaceGroup.Items[0].Thickness));
            Assert.Equal(new[] { 50.0, 40.0 }, imported.Configurations
                .Select(configuration => Assert.IsType<StandardGeometry>(
                    configuration.SurfaceGroup.Items[1].Geometry).Radius));
            Assert.Equal(5, imported.ActiveOptic.Fields.Count);
            Assert.Equal("轴上视场", imported.ActiveOptic.Fields[0].Label);
            Assert.Equal("最大Y视场", imported.ActiveOptic.Fields[^1].Label);
            Assert.Equal(3, imported.ActiveOptic.Wavelengths.Count);
            Assert.Collection(
                imported.ActiveOptic.MeritFunctionOperands,
                operand =>
                {
                    Assert.Equal("EFFL", operand.Type);
                    Assert.Equal(10.7, operand.Target, precision: 12);
                },
                operand => Assert.Equal("DMFS", operand.Type),
                operand => Assert.Equal("BLNK", operand.Type),
                operand =>
                {
                    Assert.Equal("MECS", operand.Type);
                    Assert.Equal(1, operand.Field);
                    Assert.Equal(1, operand.Wavelength);
                    Assert.Equal(185, operand.SpatialFrequency);
                    Assert.Equal(0.33571068701972878, operand.Px, precision: 15);
                    Assert.Equal(0.058177641733144006, operand.Weight, precision: 15);
                },
                operand => Assert.Equal("MECT", operand.Type));

            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument(imported.Configurations, imported.ActiveConfigurationIndex),
                projectPath);
            var reopened = await OptilandConnector.ReadDocumentAsync(projectPath);

            Assert.Equal(imported.Configurations.Count, reopened.Configurations.Count);
            Assert.Equal(imported.ActiveConfigurationIndex, reopened.ActiveConfigurationIndex);
            Assert.Equal(imported.ActiveOptic.Fields.Select(field => field.Label),
                reopened.ActiveOptic.Fields.Select(field => field.Label));
            Assert.Equal(imported.ActiveOptic.MeritFunctionOperands.Count,
                reopened.ActiveOptic.MeritFunctionOperands.Count);
        }
        finally
        {
            File.Delete(zmxPath);
            File.Delete(projectPath);
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
    [InlineData("MODE SEQ\nENPD 10\nFTYP 0 0 1 1 0 0 1\nSURF 0\nSURF 1", "afocal image space")]
    public void ZemaxImportRejectsUnsupportedPhysicalContracts(string source, string expectedMessage)
    {
        var exception = Assert.ThrowsAny<Exception>(() => OpticalFormatCatalog.Import(source, ".zmx"));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ZemaxImportPreservesSignedThicknessAndFollowingSurfaceCoordinate()
    {
        const string source = """
            MODE SEQ
            ENPD 10
            SURF 0
              DISZ 0
            SURF 1
              DISZ -2
            SURF 2
              DISZ 0
            """;

        var optic = OpticalFormatCatalog.Import(source, ".zmx");

        Assert.Equal(-2, optic.SurfaceGroup.Items[1].Thickness, precision: 12);
        Assert.Equal(-2, optic.SurfaceGroup.Items[2].CoordinateSystem.Origin.Z, precision: 12);
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
