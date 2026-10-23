using System.Text.Json;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class ExplicitObjectivesReferenceTests
{
    public static IEnumerable<object[]> FanCases => Directory.EnumerateDirectories(Path.Combine(Root,"fans"))
        .Order().Select(path=>new object[]{Path.GetFileName(path)});

    [Theory]
    [MemberData(nameof(FanCases))]
    public async Task ExplicitNativeFanAndOpdMatchAtTheSameFrozenPrescriptionAndPrimary(string id)
    {
        Verify("fans/"+id+"/");
        var folder=Path.Combine(Root,"fans",id);
        var raw=JsonFiles.Read<JsonElement>(Path.Combine(folder,"request.json"));
        var request=JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(folder,"request.json"));
        var native=JsonFiles.Read<JsonElement>(Path.Combine(folder,"point-reference.json"));
        Assert.Equal(raw.GetProperty("inputSha256").GetString(),JsonFiles.Hash(File.ReadAllBytes(Path.Combine(folder,"source.zmx"))));
        Assert.Equal(83,native.GetProperty("inputs").GetArrayLength());
        var primary=JsonFiles.Read<JsonElement>(Path.Combine(folder,"reference-wavelength-readback.json"));
        Assert.True(primary.GetProperty("IsPrimary").GetBoolean());
        Assert.Equal(request.Wavelength,primary.GetProperty("WavelengthNumber").GetInt32());
        Assert.Equal(request.PrimaryWavelengthMicrometers,primary.GetProperty("Wavelength").GetDouble());
        var before=JsonFiles.Read<JsonElement>(Path.Combine(folder,"model.json")).GetProperty("surfaces");
        var after=JsonFiles.Read<JsonElement>(Path.Combine(folder,"active-model.json")).GetProperty("surfaces");
        for(var i=1;i<before.GetArrayLength()-1;i++)Assert.Equal(before[i].GetProperty("Thickness").GetDouble(),after[i].GetProperty("Thickness").GetDouble());
        var optic=(await WorkbenchExecutor.Import(Path.Combine(folder,"source.zmx"))).Configurations[0];
        for(var i=0;i<optic.Wavelengths.Count;i++)optic.Wavelengths[i].IsPrimary=i==request.Wavelength-1;
        foreach(var key in new[]{"Ray Fan","Optical Path Difference"})
        {
            var selected=request with{CanonicalAnalysisKey=key};var entry=AnalysisComparisonRegistry.Get(key);
            var data=new WorkbenchRuntime(optic).BuildAnalysisData(key,selected.WorkbenchSettings);
            var path=Path.GetTempFileName();NumericResult actual;
            try{JsonFiles.Write(path,data);actual=ResultNormalizer.Workbench(path,entry,selected);}
            finally{File.Delete(path);}
            var expected=NativeExplicitFanNormalizer.Read(Path.Combine(folder,"point-reference.json"),entry,selected);
            Compare(actual,expected,key);
        }
    }

    [Fact]
    public async Task EvenAsphereUsesTheRequested1024PupilAnd2048FftWithoutChangingTheBudget()
    {
        Verify("energy-1024/");var folder=Path.Combine(Root,"energy-1024");
        var request=JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(folder,"request.json"));
        var optic=(await WorkbenchExecutor.Import(Path.Combine(folder,"source.zmx"))).Configurations[0];
        var data=new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey,request.WorkbenchSettings);
        Assert.Equal(1024,Convert.ToInt32(data.Values["PupilSampling"]));
        Assert.Equal(2048,Convert.ToInt32(data.Values["EffectiveImageSampling"]));
        Assert.Equal(2048,AnalysisResourceLimits.MaximumFftGridSize);
        var expected=ResultNormalizer.Zemax(Path.Combine(folder,"data.json"),AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey),request);
        Compare(ExtendedResultNormalizer.Workbench(data,request),expected,request.CanonicalAnalysisKey);
    }

    [Theory]
    [InlineData("1998-064-wide-angle-lens-200-degree-field-c1-primary-f1-w2-field-curvature-and-distortion-f-theta",100)]
    [InlineData("2028-065-wide-angle-lens-210-degree-field-c1-primary-f1-w2-field-curvature-and-distortion-f-theta",105)]
    public async Task FthetaNativePlotCoversTheWholeRequestedField(string id,double maximum)
    {
        Verify("f-theta/"+id+"/");var folder=Path.Combine(Root,"f-theta",id);
        var request=JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(folder,"request.json"));
        var captured=JsonFiles.Read<JsonElement>(Path.Combine(folder,"captured-settings.json"));
        Assert.Equal("F_Theta",captured.GetProperty("properties").GetProperty("Distortion").GetString());
        var native=JsonFiles.Read<JsonElement>(Path.Combine(folder,"data.json")).GetProperty("dataSeries")[0];
        Assert.Equal(101,native.GetProperty("x").GetArrayLength());
        Assert.Equal(maximum,native.GetProperty("x")[100].GetDouble());
        var optic=(await WorkbenchExecutor.Import(Path.Combine(folder,"source.zmx"))).Configurations[0];
        var data=new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey,request.WorkbenchSettings);
        var path=Path.GetTempFileName();NumericResult actual;
        try{JsonFiles.Write(path,data);actual=ResultNormalizer.Workbench(path,AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey),request);}
        finally{File.Delete(path);}
        Compare(actual,ResultNormalizer.Zemax(Path.Combine(folder,"data.json"),AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey),request),request.CanonicalAnalysisKey);
    }

    [Theory]
    [InlineData(1025,16,"pupilSampling")]
    [InlineData(8,2049,"imageSampling")]
    public void EnergyRejectsSamplingOutsideTheExistingSharedBudget(int pupil,int image,string parameter)
    {
        var error=Assert.Throws<ArgumentOutOfRangeException>(()=>new DiffractionEncircledEnergyAnalysis(new(),pupilSampling:pupil,imageSampling:image));
        Assert.Equal(parameter,error.ParamName);
    }

    [Theory]
    [InlineData("units","Inches")]
    [InlineData("contract","BuiltInPlot")]
    public void ExplicitReferenceCannotBeRelabeledAsAnotherContract(string property,string value)
    {
        var folder=Directory.EnumerateDirectories(Path.Combine(Root,"fans")).Order().First();
        var node=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"point-reference.json")))!;
        node[property]=value;var path=Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,node.ToJsonString());var request=JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(folder,"request.json"));
            Assert.Throws<InvalidDataException>(()=>NativeExplicitFanNormalizer.Read(path,AnalysisComparisonRegistry.Get("Ray Fan"),request));
        }
        finally{File.Delete(path);}
    }

    private static void Compare(NumericResult actual,NumericResult expected,string key)
    {
        var policy=ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory,"comparison-settings.json")).Analyses[key];
        Assert.Equal(key=="Field Curvature and Distortion"?3:2,expected.Series.Count);
        foreach(var reference in expected.Series)
        {
            var aligned=ComparisonMetrics.Align(actual.Series.Single(s=>s.Id==reference.Id),reference,[]);
            Assert.Equal(1,aligned.Coverage);
            Assert.Equal(Conclusion.Pass,ComparisonMetrics.Calculate(reference.Id,reference.YAxis.Unit,aligned,policy.Quantities[reference.YAxis.Quantity]).Conclusion);
        }
    }
    private static void Verify(string prefix)
    {
        foreach(var item in JsonFiles.Read<JsonElement>(Path.Combine(Root,"manifest.json")).EnumerateArray().Where(i=>i.GetProperty("path").GetString()!.StartsWith(prefix,StringComparison.Ordinal)))
            Assert.Equal(item.GetProperty("sha256").GetString(),JsonFiles.Hash(File.ReadAllBytes(Path.Combine(Root,item.GetProperty("path").GetString()!))));
    }
    private static string Root=>Path.Combine(AppContext.BaseDirectory,"Fixtures/objectives-completion");
}
