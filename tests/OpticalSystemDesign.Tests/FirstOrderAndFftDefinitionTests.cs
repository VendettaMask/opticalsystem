using System.Numerics;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class FirstOrderAndFftDefinitionTests
{
    private static readonly JsonSerializerOptions SnapshotOptions = new()
    { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [Fact]
    public async Task SamplingPolicyIsNestedAndIsolatedBetweenAsynchronousRequests()
    {
        Assert.False(FftPupilSampling.IsRequired);
        var required=Task.Run(async()=>
        {
            using var scope=FftPupilSampling.RequireResolvedResults();
            using(FftPupilSampling.RequireResolvedResults())Assert.True(FftPupilSampling.IsRequired);
            await Task.Yield();return FftPupilSampling.IsRequired;
        });
        var ordinary=Task.Run(()=>FftPupilSampling.IsRequired);
        Assert.True(await required);Assert.False(await ordinary);Assert.False(FftPupilSampling.IsRequired);
    }
    [Theory]
    [InlineData(.125,true)] [InlineData(.5,true)] [InlineData(.5001,false)] [InlineData(.75,false)]
    public void PhaseNyquistCheckUsesUnwrappedNeighbourDifferencesAndIgnoresPiston(double step,bool resolved)
    {
        const int n=8;var grid=new PupilGridSpecification(n,false,false,1);
        foreach(var piston in new[]{0d,1024d})
        {
            var samples=new List<WavefrontSample>();var pupil=new Complex[n,n];
            for(var row=0;row<n;row++)for(var col=0;col<n;col++)
            {samples.Add(new(grid.Coordinate(col),grid.YCoordinate(row),0,0,0,piston+step*col,1));pupil[row,col]=Complex.One;}
            var report=FftPupilSampling.Assess(new(samples,1,0,0),grid,pupil,s=>s.OpdWaves);
            Assert.Equal(resolved?FftPhaseSamplingStatus.ResolvedAtNodes:FftPhaseSamplingStatus.UndersampledPhase,report.Status);
            Assert.Equal(2*n*(n-1),report.ConnectedEdges);Assert.Equal(step,report.MaximumStepWaves,10);
            if(!resolved)Assert.Throws<FftPupilSamplingException>(()=>report.RequireResolved("test"));
        }
    }

    [Fact]
    public void PhaseCheckDoesNotConnectAcrossAPhysicalPupilGap()
    {
        var grid=new PupilGridSpecification(3,false,false,1);var pupil=new Complex[3,3];
        pupil[0,0]=pupil[0,2]=Complex.One;
        var samples=new[]{new WavefrontSample(-1,-1,0,0,0,0,1),new WavefrontSample(1,-1,0,0,0,100,1)};
        var report=FftPupilSampling.Assess(new(samples,1,0,0),grid,pupil,s=>s.OpdWaves);
        Assert.Equal(0,report.ConnectedEdges);Assert.Equal(FftPhaseSamplingStatus.NoConnectedSamples,report.Status);
    }

    [Theory]
    [InlineData("PSF")] [InlineData("MTF")] [InlineData("FFT PSF Cross Section")] [InlineData("Diffraction Encircled Energy")]
    public void UnderresolvedRealPupilReturnsTypedUnavailableWithoutNumericCurves(string key)
    {
        var optic=Toroid();var before=JsonSerializer.Serialize(optic.ToSnapshot(),SnapshotOptions);
        var settings=new Dictionary<string,string>{{"Sampling","32"},{"PupilSampling","32"},{"ImageSampling","64"},{"Display","64"},
            {"ImageDeltaMicrometers",".25"},{"WavelengthNumber","1"},{"FieldNumber","1"},{"UseRayAiming","False"},{"UsePolarization","False"},
            {"Normalized","False"},{"MaximumDistanceMicrometers","5"},{"NumPoints","401"},{"MaximumFrequency","50"}};
        var data=new WorkbenchRuntime(optic).BuildAnalysisData(key,settings);
        Assert.Equal(AnalysisOutcome.Unavailable,data.Outcome);Assert.Empty(data.PlotSeries);
        var diagnostic=Assert.IsType<FftPupilSampling>(data.Values["PupilPhaseSampling"]);
        Assert.Equal(FftPhaseSamplingStatus.UndersampledPhase,diagnostic.Status);Assert.True(diagnostic.MaximumStepWaves>.5);
        Assert.Equal(before,JsonSerializer.Serialize(optic.ToSnapshot(),SnapshotOptions));
        Assert.False(FftPupilSampling.IsRequired);
    }

    [Fact]
    public void RawFiniteFourierSumRetainsItsExplicitSamplingDiagnostic()
    {
        var optic=Toroid();var psf=DiffractionEngine.ComputeFftPsf(optic,(0,0),optic.Wavelengths[0],32,64,
            cellCenteredPupil:true,zemaxFftSampling:true,imageDelta:-1);
        Assert.Equal(FftPhaseSamplingStatus.UndersampledPhase,psf.PupilPhaseSampling!.Status);
        Assert.All(psf.Values.Cast<double>(),v=>Assert.True(double.IsFinite(v)&&v>=0));
    }

    [Fact]
    public async Task FirstOrderDeclaresItsIndexModelAndNeverChangesCrystalRayModeOrParaxFlag()
    {
        var input=Path.Combine(AppContext.BaseDirectory,"Fixtures","reference-closure","crystal-cube.zmx");
        var imported=await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(input);
        foreach(var optic in imported.Configurations)
        {
            var before=JsonSerializer.Serialize(optic.ToSnapshot(),SnapshotOptions);var data=new FirstOrderAnalysis(optic).GenerateData();
            Assert.Equal(FirstOrderAnalysis.ReferenceConvention,data.Values["FirstOrderReferenceConvention"]);
            Assert.Equal(false,data.Values["IncludesChiefRayWalkOff"]);
            Assert.Equal(100d,data.Values["EffectiveFocalLength"]);Assert.Equal(5d,data.Values["FNumber"]);
            Assert.Equal(before,JsonSerializer.Serialize(optic.ToSnapshot(),SnapshotOptions));
        }
    }

    private static Optic Toroid()=>OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","reference-closure","Toroid.zmx")),".zmx");
}
