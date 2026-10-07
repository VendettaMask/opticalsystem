using System.Text.Json;
using OptilandWorkbench.ZemaxComparison.Normalization;

namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class NativeReferenceDiagnosticsTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"messages\":[]}")]
    public void ReportsWithoutDiagnosticsRemainEligible(string text)
    {
        using var report = JsonDocument.Parse(text);
        NativeReferenceDiagnostics.Validate(report.RootElement);
    }

    [Theory]
    [InlineData("Success", "取样太低, 数据不准确!")]
    [InlineData("Success", "Calculation aborted; invalid results!")]
    [InlineData("Failure", "")]
    public void DiagnosticsAreRejectedWithoutParsingLanguageOrTrustingSuccess(string code, string message)
    {
        using var report = JsonDocument.Parse(JsonSerializer.Serialize(new { messages = new[] { new { ErrorCode=code, Text=message } } }));
        var error = Assert.Throws<InvalidDataException>(() => NativeReferenceDiagnostics.Validate(report.RootElement));
        Assert.Contains("Native report emitted diagnostics", error.Message);
        Assert.Contains(code, error.Message);
    }

    [Fact]
    public void InvalidDiagnosticSchemaIsRejected()
    {
        using var report = JsonDocument.Parse("{\"messages\":null}");
        Assert.Throws<InvalidDataException>(() => NativeReferenceDiagnostics.Validate(report.RootElement));
    }

    [Theory]
    [InlineData("PSF")]
    [InlineData("Diffraction Encircled Energy")]
    public void RunnerNormalizerRejectsDiagnosedReportsBeforeAcceptingAnyNumericShape(string key)
    {
        var directory = Path.Combine(Path.GetTempPath(), "zemax-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "data.json");
        try
        {
            File.WriteAllText(path, """
                {"messages":[{"ErrorCode":"Success","Text":"invalid reference"}],
                 "dataGrids":[{"nx":1,"ny":1,"minX":0,"minY":0,"dx":1,"dy":1,"values":[[0]]}],
                 "dataSeries":[]}
                """);
            var error = Assert.Throws<InvalidDataException>(() => ResultNormalizer.Zemax(path,
                AnalysisComparisonRegistry.Get(key), new CanonicalAnalysisRequest
                {
                    CanonicalAnalysisKey=key, Apodization="None", WorkbenchSettings=[]
                }));
            Assert.Contains("Native report emitted diagnostics", error.Message);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
