using System.Text.RegularExpressions;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class CadExportTests
{
    [Fact]
    public void StepExporterWritesClosedFacetedLensSolidsInMillimeters()
    {
        var optic = Optic.CreateCookeTriplet();
        var step = StepCadExporter.Serialize(
            optic,
            new StepCadExportOptions(
                SurfaceSamples: 17,
                AngularSamples: 32,
                ProductName: "Cooke STEP validation",
                CreatedUtc: new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero)));

        Assert.StartsWith("ISO-10303-21;", step, StringComparison.Ordinal);
        Assert.EndsWith("END-ISO-10303-21;", step.TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("FILE_SCHEMA(('CONFIG_CONTROL_DESIGN'));", step, StringComparison.Ordinal);
        Assert.Contains("SI_UNIT(.MILLI.,.METRE.)", step, StringComparison.Ordinal);
        Assert.Contains("ADVANCED_BREP_SHAPE_REPRESENTATION", step, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(step, @"MANIFOLD_SOLID_BREP\(").Count);
        Assert.True(Regex.Matches(step, @"EDGE_LOOP\('").Count > 100);
        Assert.Equal(3, Regex.Matches(step, @"CLOSED_SHELL\(").Count);
        Assert.True(Regex.Matches(step, @"FACE\('',").Count > 100);
        Assert.DoesNotContain("NaN", step, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Infinity", step, StringComparison.OrdinalIgnoreCase);

        var definitions = Regex.Matches(step, @"#(\d+)\s*=")
            .Select(match => int.Parse(match.Groups[1].Value))
            .ToHashSet();
        var references = Regex.Matches(step, @"#(\d+)")
            .Select(match => int.Parse(match.Groups[1].Value))
            .ToArray();
        Assert.All(references, reference => Assert.Contains(reference, definitions));
    }

    [Fact]
    public void StepExporterRejectsSystemsWithoutLensSolids()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StepCadExporter.Serialize(Optic.CreateBlank()));

        Assert.Contains("没有可导出的镜片实体", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CadExportServiceWritesStepByDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), $"optiland-cad-{Guid.NewGuid():N}.step");
        try
        {
            using var application = WorkbenchApplication.Create("cooke");
            var result = await application.CadExport.ExportAsync(
                path,
                new CadExportOptionsDto(
                    CadExportFormat.Step,
                    SurfaceSamples: 17,
                    AngularSamples: 32));

            Assert.Equal(CadExportFormat.Step, result.Format);
            Assert.Equal(Path.GetFullPath(path), result.Path);
            Assert.True(result.ByteCount > 10_000);
            Assert.Equal(result.ByteCount, new FileInfo(path).Length);
            Assert.StartsWith(
                "ISO-10303-21;",
                await File.ReadAllTextAsync(path),
                StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData("Cooke Triplet", "Cooke Triplet.step")]
    [InlineData("  ", "optical-system.step")]
    public void CadSuggestedFileNameDefaultsToStep(string documentName, string expected)
    {
        var factory = typeof(MainWindow).GetMethod(
            "CadSuggestedFileName",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(factory);
        Assert.Equal(expected, factory.Invoke(null, new object[] { documentName }));
    }
}
