using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class RiflescopeMtfFieldTests
{
    private static readonly double[] Frequencies = [.1, .2, .4, .8, 1.6, 3.2];
    [Fact]
    public void FourierSixFrequenciesMeetConvergedDirectNativeResults()
    {
        using var fixture = new Fixture();
        var low = fixture.Native("fourier", 256); var high = fixture.Native("fourier", 512);
        var actual = Compute(fixture, MtfComputationMethod.Fourier, 64, Frequencies);
        var critical = Compute(fixture, MtfComputationMethod.Fourier, 128, [3.2]);
        Record("n64", "fourier", actual); Record("n128-critical", "fourier", critical);
        for (var k = 0; k < 12; k++)
        {
            Pass("Fourier MTF vs Field", low[k], high[k]);
            Pass("Fourier MTF vs Field", Curve(actual, k), high[k]);
        }
        for (var k = 0; k < 2; k++)
        {
            Pass("Fourier MTF vs Field", Curve(actual, 10+k), Curve(critical, k));
            Pass("Fourier MTF vs Field", Curve(critical, k), high[10+k]);
        }
    }
    [Fact]
    public void FiniteSamplingHuygensRegressionMatchesNativeWithoutClaimingConvergence()
    {
        using var fixture = new Fixture();
        var actual = Compute(fixture, MtfComputationMethod.Huygens, 64, Frequencies);
        Record("n64", "huygens", actual);
        var native = fixture.Native("huygens", 64);
        for (var k = 0; k < 12; k++) Pass("Huygens MTF vs Field", Curve(actual, k), native[k]);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ChiefAndCentroidPsfMatchNativeAbsoluteIntensityAndPhysicalAxes(bool centroid)
    {
        using var fixture = new Fixture();
        var folder = Path.Combine(fixture.Root,"native-huygens-split",centroid ? "psf-field2-centroid" : "psf-field2-chief");
        var grid = JsonFiles.Read<JsonElement>(Path.Combine(folder,"data.json")).GetProperty("dataGrids")[0];
        var native = grid.GetProperty("values").EnumerateArray().SelectMany(r=>r.EnumerateArray().Select(v=>v.GetDouble())).ToArray();
        var actual = new HuygensPsfAnalysis(fixture.Optic(),numRays:32,imageSize:32,pixelPitchMillimeters:0,
            wavelengthNumber:2,fieldNumber:2,useCentroid:centroid,normalize:false).GenerateData();
        Assert.Equal(AnalysisOutcome.Success,actual.Outcome);
        var series=actual.PlotSeries.Single();
        Assert.Equal(AnalysisAxisUnit.Milliradian,series.XUnit); Assert.Equal(AnalysisAxisQuantity.IncidentAngle,series.XQuantity);
        Assert.Equal(AnalysisAxisUnit.Dimensionless,series.ValueUnit);
        var pitch=grid.GetProperty("dx").GetDouble()*(Math.PI*1000/10800);
        Assert.InRange(Math.Abs((series.Points[1].X-series.Points[0].X)-pitch),0,1e-12);
        var expected = new Series1DResult("grid","Native",Enumerable.Range(0,native.Length).Select(i=>(double)i).ToArray(),native,
            new("Sample","Dimensionless"),new("Irradiance","Dimensionless"));
        var measured=expected with {Y=series.Points.Select(p=>p.Value!.Value).ToArray()};
        Pass("Huygens PSF",measured,expected,"Irradiance");
        Record("psf32",centroid ? "centroid" : "chief",actual);
    }
    private static AnalysisData Compute(Fixture fixture,MtfComputationMethod method,int n,double[] frequencies)
    {
        var data=new MtfVsFieldAnalysis(fixture.Optic(),method,spatialFrequencies:frequencies,fieldPointCount:10,wavelengthNumber:0,
            removeVignettingFactors:true,zemaxCompatibleOutput:true,
            settings:new(PupilSampling:n,ImageSize:n,PixelPitchMillimeters:0,ZemaxCompatible:true,UseZemaxHuygensSemantics:method==MtfComputationMethod.Huygens)).GenerateData();
        Assert.Equal(AnalysisOutcome.Success,data.Outcome); return data;
    }
    private static Series1DResult Curve(AnalysisData data,int k)
    {
        var s=data.PlotSeries[k]; Assert.Equal(AnalysisAxisQuantity.NormalizedField,s.XQuantity); Assert.Equal(AnalysisAxisUnit.Dimensionless,s.XUnit);
        return new("curve","Core",s.Points.Select(p=>p.X).ToArray(),s.Points.Select(p=>p.Y).ToArray(),new("NormalizedField","Dimensionless"),new("Modulation","Dimensionless"));
    }
    private static void Pass(string analysis,Series1DResult a,Series1DResult b,string quantity="Modulation")
    {
        var gate=ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory,"comparison-settings.json")).Analyses[analysis].Quantities[quantity];
        var metric=ComparisonMetrics.Calculate(analysis,"Dimensionless",ComparisonMetrics.Align(a,b,[]),gate);
        Assert.True(metric.Conclusion==Conclusion.Pass,metric.ToString());
    }
    private static void Record(string cohort,string id,AnalysisData data)
    {
        var output=Environment.GetEnvironmentVariable("OSD_RIFLESCOPE_MTF_FIELD_EVIDENCE"); if(string.IsNullOrWhiteSpace(output))return;
        var hash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location)));
        var folder=Path.Combine(output,"core-"+hash[..12],cohort);Directory.CreateDirectory(folder);
        JsonFiles.Write(Path.Combine(folder,id+".json"),data);JsonFiles.Write(Path.Combine(folder,"identity.json"),new{coreSha256=hash});
    }
    private sealed class Fixture:IDisposable
    {
        internal string Root {get;}=Path.Combine(Path.GetTempPath(),"riflescope-mtf-"+Guid.NewGuid().ToString("N"));
        private readonly JsonElement _contract;
        internal Fixture()
        {
            var folder=Path.Combine(AppContext.BaseDirectory,"Fixtures/riflescope-mtf-field");
            var manifest=JsonFiles.Read<JsonElement>(Path.Combine(folder,"manifest.json"));var archive=Path.Combine(folder,"reference.zip");
            Assert.Equal(manifest.GetProperty("sha256").GetString(),Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive))));
            ZipFile.ExtractToDirectory(archive,Root);_contract=JsonFiles.Read<JsonElement>(Path.Combine(Root,"contract.json"));
            Assert.Equal(_contract.GetProperty("policySha256").GetString(),Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"comparison-settings.json")))));
        }
        internal Optic Optic()
        {
            var file=Path.Combine(Root,"inputs/riflescope.optic.json");
            Assert.Equal(_contract.GetProperty("snapshotSha256").GetString(),Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
            return Core.Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(file));
        }
        internal Series1DResult[] Native(string method,int n)=>NativeCurves.Read(Root,method,n,_contract.GetProperty("sourceSha256").GetString()!,Frequencies);
        public void Dispose()
        {
            var prefix=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar+"riflescope-mtf-";
            if(!Path.GetFullPath(Root).StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected fixture cleanup path");
            Directory.Delete(Root,true);
        }
    }
}
