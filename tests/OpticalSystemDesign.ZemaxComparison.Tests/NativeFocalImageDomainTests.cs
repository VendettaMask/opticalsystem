using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class NativeFocalImageDomainTests
{
    [Fact]
    public void CoatedPlaneContactCanBeVerifiedWithAnOffAxisField()
    {
        var directory = Directory.CreateTempSubdirectory("native-plane-contact-");
        try
        {
            var native = Path.Combine(directory.FullName, "data.json");
            var workbench = Path.Combine(directory.FullName, "workbench.json");
            var model = Path.Combine(directory.FullName, "model.json");
            const string prescription = """
                {"mode":"Sequential","aperture":{"AFocalImageSpace":false},"fields":[{"data":{"X":0,"Y":1}}],
                 "surfaces":[
                   {"Type":"Standard","IsObject":true,"Radius":null,"Thickness":null,"Material":"","Coating":""},
                   {"Type":"Standard","Radius":null,"Thickness":0,"Material":"","Coating":"ETALON"},
                   {"Type":"Standard","IsImage":true,"Radius":null,"Thickness":null,"Material":"","Coating":""}]}
                """;
            File.WriteAllText(native, """{"dataSeries":[{"x":[0,1],"y":[[1,1],[0.4,0.4]]}]}""");
            File.WriteAllText(model, prescription);
            JsonFiles.Write(workbench, FocalPlaneTrainOutcome());
            var entry = AnalysisComparisonRegistry.Get("MTF");
            var request = new CanonicalAnalysisRequest
            {
                CanonicalAnalysisKey = "MTF", Apodization = "Uniform", WorkbenchSettings = [], MaximumFieldRadius = 1
            };
            var error = Assert.Throws<InvalidDataException>(() => ResultNormalizer.Zemax(native, entry, request, workbench));
            Assert.Contains("no image-plane frequency scale", error.Message);

            File.WriteAllText(model, prescription.Replace("\"Radius\":null,\"Thickness\":0", "\"Radius\":50,\"Thickness\":0"));
            Assert.Equal(2, ResultNormalizer.Zemax(native, entry, request, workbench).Series.Count);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static AnalysisData FocalPlaneTrainOutcome() => new("MTF", new Dictionary<string, object>
    {
        [FocalImageDomain.ResultKey] = FocalImageDomain.CollimatedPlaneTrain
    }, Outcome: AnalysisOutcome.NotApplicable);

    [Fact]
    public void FiniteMtfValuesNeedAValidPhysicalDomainAndIndependentPrescriptionProof()
    {
        var directory = Directory.CreateTempSubdirectory("native-focal-domain-");
        try
        {
            var native = Path.Combine(directory.FullName, "data.json");
            var workbench = Path.Combine(directory.FullName, "workbench.json");
            var model = Path.Combine(directory.FullName, "model.json");
            File.WriteAllText(native, """{"dataSeries":[{"x":[0,1],"y":[[1,1],[0.4,0.4]]}]}""");
            const string prescription = """
                {"mode":"Sequential","aperture":{"AFocalImageSpace":false},"fields":[{"data":{"X":0,"Y":0}}],
                 "surfaces":[
                   {"Type":"Standard","IsObject":true,"Thickness":null,"Material":"","Coating":""},
                   {"Type":"GridSag","Thickness":0,"Material":"","Coating":""},
                   {"Type":"Standard","IsImage":true,"Thickness":null,"Material":"","Coating":""}]}
                """;
            File.WriteAllText(model, prescription);
            JsonFiles.Write(workbench, new AnalysisData("MTF", new Dictionary<string, object>
            {
                [FocalImageDomain.ResultKey] = FocalImageDomain.CollimatedContactPupil
            }, Outcome: AnalysisOutcome.NotApplicable));
            var entry = AnalysisComparisonRegistry.Get("MTF");
            var request = new CanonicalAnalysisRequest
            {
                CanonicalAnalysisKey = "MTF", Apodization = "Uniform", WorkbenchSettings = []
            };
            var error = Assert.Throws<InvalidDataException>(() => ResultNormalizer.Zemax(native, entry, request, workbench));
            Assert.Contains("no image-plane frequency scale", error.Message);

            File.WriteAllText(model, prescription.Replace("\"Type\":\"GridSag\",\"Thickness\":0,\"Material\":\"\"",
                "\"Type\":\"GridSag\",\"Thickness\":0,\"Material\":\"N-BK7\""));
            Assert.Equal(2, ResultNormalizer.Zemax(native, entry, request, workbench).Series.Count);
            File.WriteAllText(model, prescription);
            JsonFiles.Write(workbench, AnalysisData.Unavailable("MTF", "Another failure"));
            Assert.Equal(2, ResultNormalizer.Zemax(native, entry, request, workbench).Series.Count);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
