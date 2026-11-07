using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class NativeLengthUnitContractTests
{
    [Theory]
    [InlineData("CM", "Centimeters")]
    [InlineData("IN", "Inches")]
    [InlineData("M", "Meters")]
    public void RequiresMatchingUnsavedUnitConversionAndNativeReadback(string unit, string sourceUnit)
    {
        var directory = Directory.CreateTempSubdirectory("native-units-");
        try
        {
            var data = Path.Combine(directory.FullName, "data.json");
            var receipt = Path.Combine(directory.FullName, "unit-normalization.json");
            var model = Path.Combine(directory.FullName, "model.json");
            var request = new CanonicalAnalysisRequest { CanonicalAnalysisKey = "First Order", NativeLensUnit = unit, Apodization = "none", WorkbenchSettings = [] };
            Assert.Throws<InvalidDataException>(() => NativeLengthUnitContract.Validate(data, request));

            File.WriteAllText(model, """{"units":{"LensUnits":"Millimeters"}}""");
            void WriteReceipt(string originalUnit, bool savedInput) => File.WriteAllText(receipt, JsonSerializer.Serialize(new
            {
                originalUnit, convertedUnit = "Millimeters", method = "OpticStudio.Tools.OpenScale.ScaleByUnits", savedInput
            }));
            WriteReceipt(sourceUnit, false);
            NativeLengthUnitContract.Validate(data, request);
            WriteReceipt(sourceUnit, true);
            Assert.Throws<InvalidDataException>(() => NativeLengthUnitContract.Validate(data, request));
            WriteReceipt("Millimeters", false);
            Assert.Throws<InvalidDataException>(() => NativeLengthUnitContract.Validate(data, request));
            WriteReceipt(sourceUnit, false);
            File.WriteAllText(model, """{"units":{"LensUnits":"Inches"}}""");
            Assert.Throws<InvalidDataException>(() => NativeLengthUnitContract.Validate(data, request));
        }
        finally { directory.Delete(recursive: true); }
    }
}
