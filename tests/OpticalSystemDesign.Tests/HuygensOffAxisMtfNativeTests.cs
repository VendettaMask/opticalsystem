using System.Text.Json;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class HuygensOffAxisMtfNativeTests
{
    [Theory]
    [InlineData("positive", "MTHA")]
    [InlineData("positive", "MTHS")]
    [InlineData("positive", "MTHT")]
    [InlineData("positive", "MTHN")]
    [InlineData("positive", "MTHX")]
    [InlineData("negative", "MTHA")]
    [InlineData("negative", "MTHS")]
    [InlineData("negative", "MTHT")]
    [InlineData("negative", "MTHN")]
    [InlineData("negative", "MTHX")]
    public void CapturedMfeControlsRequireTheSameGeometricCentroidWindow(string direction, string code)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Validation", "Zemax", "HuygensOffAxis");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root, "source.ZMX")), ".zmx");
        optic.RayAimingEnabled = true;
        if (direction == "positive") optic.Fields[2].Y = 15;
        using var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "mfe", direction, "mtf-operands.json")));
        var row = capture.RootElement.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("code").GetString() == code);
        Assert.Equal(1, row.GetProperty("sampling").GetInt32());
        Assert.Equal(1, row.GetProperty("wave").GetInt32());
        Assert.Equal(3, row.GetProperty("field").GetInt32());
        var result = MtfMetrics.EvaluateHuygens(optic, 1, 1, 3,
            row.GetProperty("frequency").GetDouble(), row.GetProperty("imageDeltaMicrometers").GetDouble());
        var actual = code switch
        {
            "MTHA" => (result.Tangential + result.Sagittal) / 2,
            "MTHS" => result.Sagittal,
            "MTHT" => result.Tangential,
            "MTHN" => Math.Min(result.Tangential, result.Sagittal),
            "MTHX" => Math.Max(result.Tangential, result.Sagittal),
            _ => throw new InvalidOperationException()
        };
        Assert.InRange(Math.Abs(actual - row.GetProperty("value").GetDouble()), 0, 1e-6);
    }
}
