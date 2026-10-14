using System.Security.Cryptography;
using System.Text.Json;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Optimization;
using Xunit.Abstractions;

namespace OptilandWorkbench.Tests;

public sealed class FieldCurvatureNativeMatrixTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("123456", "FCGS", 303)]
    [InlineData("123456", "FCGT", 303)]
    [InlineData("double-gauss-28-degree-field", "FCGS", 303)]
    [InlineData("double-gauss-28-degree-field", "FCGT", 303)]
    [InlineData("relay-lens", "FCGS", 303)]
    [InlineData("relay-lens", "FCGT", 303)]
    [InlineData("even-asphere", "FCGS", 101)]
    [InlineData("even-asphere", "FCGT", 101)]
    public void NativeMfeControlsPassOriginalCurvatureTarget(string name, string code, int count)
    {
        var root = Root();
        var capture = Path.Combine(root, "validation/zemax/2026-r1/fcgt-efno-2026-10-14");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(capture, "manifest.json")));
        var item = manifest.RootElement.GetProperty("Cases").EnumerateArray().Single(item => item.GetProperty("Name").GetString() == name);
        var source = Path.Combine(root, item.GetProperty("Source").GetString()!);
        Assert.Equal(item.GetProperty("SourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        var directory = Path.Combine(capture, name);
        using var environment = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "environment.json")));
        Assert.True(environment.RootElement.GetProperty("validLicense").GetBoolean());
        Assert.Equal(26, environment.RootElement.GetProperty("major").GetInt32());
        Assert.Equal(1, environment.RootElement.GetProperty("minor").GetInt32());
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, code + "-schema.json")));
        Assert.False(schema.RootElement[0].GetProperty("cell").GetProperty("IsActive").GetBoolean());
        Assert.Equal("Integer", schema.RootElement[1].GetProperty("cell").GetProperty("DataType").GetString());
        Assert.Equal("Double", schema.RootElement[2].GetProperty("cell").GetProperty("DataType").GetString());
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "curvature-operands.json")));
        var rows = native.RootElement.EnumerateArray().Where(row => row.GetProperty("code").GetString() == code).ToArray();
        Assert.Equal(count, rows.Length);
        using var signed = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "signed-curvature-operands.json")));
        var signedRows = signed.RootElement.EnumerateArray().Where(row => row.GetProperty("code").GetString() == code).ToArray();
        Assert.Equal(count / 101 * 7, signedRows.Length);
        rows = rows.Concat(signedRows).ToArray();
        var maximum = 0.0;
        foreach (var row in rows)
        {
            var definition = new MeritOperandDefinition
            {
                Type = code,
                Enabled = true,
                Weight = 1,
                ZemaxIntegerParameters = [0, row.GetProperty("wave").GetInt32()],
                ZemaxDataParameters = [row.GetProperty("hx").GetDouble(), row.GetProperty("hy").GetDouble(), 0, 0]
            };
            var actual = MeritFunctionCatalog.Evaluate(optic, definition);
            Assert.True(string.IsNullOrEmpty(actual.Error), actual.Error);
            maximum = Math.Max(maximum, Math.Abs(actual.Value - row.GetProperty("value").GetDouble()));
        }
        output.WriteLine($"{name}/{code}: count={rows.Length}, maximumError={maximum:R} mm; originalTarget=1e-5 mm");
        Assert.InRange(maximum, 0, 1e-5);
    }

    [Theory]
    [InlineData("Field Curvature")]
    [InlineData("Field Curvature and Distortion")]
    public void ApplicationPresetMatchesCapturedCurveAndRetainsExplicitLegacySettings(string name)
    {
        var root = Root();
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "artifacts/zemax/123456-zemax-2026-r1-baseline/analyses/004-fieldcurvatureanddistortion/data.json")));
        var curves = native.RootElement.GetProperty("dataSeries").EnumerateArray().ToArray();
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root,
            "artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX")), ".zmx");
        optic.FieldDefinition = FieldDefinitionKind.Angle;
        var maximumAngle = curves[0].GetProperty("x")[100].GetDouble();
        var maximumHeight = FieldCoordinates.MaximumRadius(optic.Fields);
        foreach (var field in optic.Fields)
        {
            field.X *= maximumAngle / maximumHeight;
            field.Y *= maximumAngle / maximumHeight;
        }
        var runtime = new WorkbenchRuntime(optic);
        var current = runtime.BuildAnalysisData(name, new Dictionary<string, string> { ["NumPoints"] = "101" });
        var prefix = name == "Field Curvature" ? "" : "FieldCurvature.";
        Assert.Equal(.001, current.Values[prefix + "ParabasalDelta"]);
        Assert.Equal("ChiefAndPositive", current.Values[prefix + "ParabasalRayPair"]);
        var maximum = 0.0;
        for (var wave = 0; wave < curves.Length; wave++)
            for (var direction = 0; direction < 2; direction++)
                for (var field = 0; field < 101; field++)
                    maximum = Math.Max(maximum, Math.Abs(current.PlotSeries[2 * wave + direction].Points[field].X
                        - curves[wave].GetProperty("y")[field][direction].GetDouble()));
        output.WriteLine($"{name}: 606 captured values; maximumError={maximum:R} mm");
        Assert.InRange(maximum, 0, 1e-5);
        var legacy = runtime.BuildAnalysisData(name, new Dictionary<string, string>
        { ["NumPoints"] = "3", ["ChiefRayParabasal"] = "false", ["ParabasalDelta"] = "0.00001" });
        Assert.Equal("Symmetric", legacy.Values[prefix + "ParabasalRayPair"]);
        Assert.Equal(1e-5, legacy.Values[prefix + "ParabasalDelta"]);
        var general = new FieldCurvatureAnalysis(optic, numPoints: 3).GenerateData();
        Assert.Equal("Symmetric", general.Values["ParabasalRayPair"]);
        Assert.Equal(1e-5, general.Values["ParabasalDelta"]);
        Assert.Equal(general.PlotSeries.SelectMany(series => series.Points), legacy.PlotSeries.SelectMany(series => series.Points));
    }

    [Theory]
    [InlineData("FCGS")]
    [InlineData("FCGT")]
    public void ObliqueGeneralizedComponentsRetainActualImageDefocus(string code)
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(Root(),
            "validation/zemax/2026-r1/tessar-ray-audit-2026-10-07/even-asphere-gq6-remove/source.zmx")), ".zmx");
        var row = new MeritOperandDefinition
        {
            Type = code,
            Enabled = true,
            Weight = 1,
            ZemaxIntegerParameters = [0, 1],
            ZemaxDataParameters = [.3, .4, 0, 0]
        };
        var before = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.True(string.IsNullOrEmpty(before.Error), before.Error);
        optic.SurfaceGroup.Items[^2].Thickness += .123;
        optic.SurfaceGroup.Renumber();
        var after = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.True(string.IsNullOrEmpty(after.Error), after.Error);
        Assert.Equal(before.Value - .123, after.Value, 8);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository not found");
    }
}
