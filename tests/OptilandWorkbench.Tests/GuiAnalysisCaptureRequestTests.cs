using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.Tests;

public sealed class GuiAnalysisCaptureRequestTests
{
    [Fact]
    public void ParseReturnsNullWithoutCaptureMode()
    {
        Assert.Null(GuiAnalysisCaptureRequest.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void ParseRequiresAllCapturePaths()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            GuiAnalysisCaptureRequest.Parse(new[] { "--capture-analysis-gui" }));

        Assert.Contains("--source=", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseNormalizesPathsAndRange()
    {
        var request = GuiAnalysisCaptureRequest.Parse(new[]
        {
            "--capture-analysis-gui",
            "--source=lens.zmx",
            "--settings-manifest=settings.json",
            "--output=images",
            "--start=3",
            "--end=7"
        });

        Assert.NotNull(request);
        Assert.True(Path.IsPathFullyQualified(request.SourcePath));
        Assert.True(Path.IsPathFullyQualified(request.SettingsManifestPath));
        Assert.True(Path.IsPathFullyQualified(request.OutputDirectory));
        Assert.Equal(3, request.StartIndex);
        Assert.Equal(7, request.EndIndex);
    }
}
