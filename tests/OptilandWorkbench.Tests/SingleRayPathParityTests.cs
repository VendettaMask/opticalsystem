using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class SingleRayPathParityTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures", "single-ray-path");
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static IEnumerable<object[]> Captures()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        return manifest.RootElement.GetProperty("cases").EnumerateArray()
            .Select(c => new object[] { c.GetProperty("name").GetString()! }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public void AllElevenColumnsMatchOriginalNativeReportsAcrossSixOfficialLenses(string name)
    {
        var directory = Path.Combine(Root, name);
        var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(
            File.ReadAllText(Path.Combine(directory, "snapshot.json")), Json)!);
        using var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "canonical-request.json")));
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "native-normalized.json")));
        using var environment = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "environment.json")));
        Assert.Equal(26, environment.RootElement.GetProperty("major").GetInt32());
        Assert.Equal(1, environment.RootElement.GetProperty("minor").GetInt32());
        Assert.True(environment.RootElement.GetProperty("validLicense").GetBoolean());
        var r = request.RootElement;
        var settings = r.GetProperty("workbenchSettings");
        double Setting(string name) => double.Parse(settings.GetProperty(name).GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var data = new SingleRayTraceAnalysis(optic, fieldNumber: r.GetProperty("field").GetInt32(),
            hx: Setting("Hx"), hy: Setting("Hy"), px: Setting("Px"), py: Setting("Py"),
            wavelengthNumber: r.GetProperty("wavelength").GetInt32(),
            globalCoordinates: bool.Parse(settings.GetProperty("GlobalCoordinates").GetString()!),
            useRayAiming: r.GetProperty("useRayAiming").GetBoolean()).GenerateData();
        var rows = Assert.IsType<double[][]>(data.Values["RealRayData"]);
        var series = native.RootElement.GetProperty("series");
        Assert.Equal(11, series.GetArrayLength());
        Assert.Equal(optic.SurfaceGroup.Items.Count - 1, rows.Length);
        for (var column = 0; column < series.GetArrayLength(); column++)
        {
            var s = series[column];
            Assert.Equal(rows.Length, s.GetProperty("y").GetArrayLength());
            // Original capture budgets: coordinates/path 1e-6 mm, direction and
            // normal components 1e-8, incidence 1e-6 degrees. No fitted offsets.
            var budget = column is <= 2 or >= 9 ? 1e-6 : 1e-8;
            for (var surface = 0; surface < rows.Length; surface++)
            {
                Assert.Equal(rows[surface][0], s.GetProperty("x")[surface].GetDouble());
                Assert.InRange(Math.Abs(rows[surface][column + 1] - s.GetProperty("y")[surface].GetDouble()), 0, budget);
            }
        }
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(50)]
    [InlineData(-50)]
    public void InfiniteObjectReportUsesSignedVertexPlanePathWhilePhysicalHistoryStaysUnchanged(double radius)
    {
        var optic = Model(double.PositiveInfinity, radius);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, .8, .55, aimAtStop: false);
        var before = optic.SequentialRayTracer.Trace(bundle).RayHistories.Single().ToArray();
        var local = new SingleRayTraceAnalysis(optic, py: .8, useRayAiming: false).GenerateData();
        var global = new SingleRayTraceAnalysis(optic, py: .8, useRayAiming: false, globalCoordinates: true).GenerateData();
        var expected = double.IsPositiveInfinity(radius) ? 0
            : Math.Sign(radius) * (Math.Abs(radius) - Math.Sqrt(radius * radius - 16));
        var rows = Assert.IsType<double[][]>(local.Values["RealRayData"]);
        Assert.InRange(Math.Abs(rows[0][11] - expected), 0, 1e-12);
        Assert.Equal(rows[0][11], Assert.IsType<double[][]>(global.Values["RealRayData"])[0][11]);
        Assert.True(before.Single(s => s.SurfaceNumber == 1).SegmentLength > 0);
        Assert.Equal(before, optic.SequentialRayTracer.Trace(bundle).RayHistories.Single());
        foreach (var row in rows.Skip(1))
            Assert.Equal(before.Single(s => s.SurfaceNumber == row[0]).SegmentLength, row[11]);
        var tableRow = Assert.Single(local.Table!.Rows.Where((row, index) =>
            local.Table.RowGroups![index] == "实光线" && row[0] == "1"));
        Assert.Equal(expected.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), tableRow[11]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(250)]
    public void FiniteAndZeroObjectDistancesKeepActualObjectToSurfaceSegments(double objectDistance)
    {
        var optic = Model(objectDistance, 50);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, .55, aimAtStop: false);
        var history = optic.SequentialRayTracer.Trace(bundle).RayHistories.Single();
        var data = new SingleRayTraceAnalysis(optic, py: 0, useRayAiming: false).GenerateData();
        var rows = Assert.IsType<double[][]>(data.Values["RealRayData"]);
        Assert.NotEmpty(rows);
        Assert.Equal(1d, rows[0][0]);
        foreach (var row in rows)
            Assert.Equal(history.Single(s => s.SurfaceNumber == row[0]).SegmentLength, row[11]);
    }

    [Fact]
    public void AFirstSurfaceMissDoesNotInventAVertexPlaneSegment()
    {
        var optic = Model(double.PositiveInfinity, 2);
        var data = new SingleRayTraceAnalysis(optic, py: .8, useRayAiming: false).GenerateData();
        var row = Assert.Single(Assert.IsType<double[][]>(data.Values["RealRayData"]));
        Assert.Equal(1d, row[0]);
        Assert.Equal(0d, row[11]);
        Assert.Equal(1, data.Values["VignettedSurface"]);
    }

    [Fact]
    public void AllOriginalNativeFixtureHashesStayUnchanged()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(Root, file.GetProperty("path").GetString()!));
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, path);
            Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        }
    }

    private static Optic Model(double objectDistance, double radius)
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = objectDistance, Geometry = new PlaneGeometry(), SemiDiameter = 100 },
            new OpticalSurface { Thickness = 10, Geometry = double.IsPositiveInfinity(radius) ? new PlaneGeometry() : new StandardGeometry(radius), SemiDiameter = 100, IsStop = true },
            new OpticalSurface { Thickness = 0, Geometry = new PlaneGeometry(), SemiDiameter = 100 }
        ]);
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 10;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint());
        optic.Wavelengths.Clear();
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        return optic;
    }
}
