using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

/// <summary>Frozen Grid/5 discrepancy and internal transport controls. A passing regression here
/// preserves a known failed commercial gate; it never certifies White Specular.</summary>
public sealed class CommercialBsdfRuntimeGapTests
{
    private static string Source => Path.Combine(AppContext.BaseDirectory,"Fixtures","speos-native-v11-white-runtime.anisotropicbsdf");
    private static MeasuredBsdf Model(bool runtime = false)
    {
        var data = BsdfFileImporter.Read(Source,new(BsdfImportSides.Front));
        return new(runtime ? data : SpeosV11XyzTests.LegacyWhite(data));
    }

    [Fact]
    public void FrozenGridFiveDiscrepancySeparatesEscapedAndFiniteSensorPower()
    {
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","speos-native-v11-white-runtime-reference.json")));
        var reference=json.RootElement;
        Assert.Equal(reference.GetProperty("SourceSha256").GetString(),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Source))).ToLowerInvariant());
        var model=Model(); Assert.Equal("MeasuredBsdfGrid/5",model.Data.ComputationProtocol);
        var source=reference.GetProperty("SourceWatts").GetDouble();
        var computed=model.Integrate(SpeosV11XyzTests.Context(0,400)).Reflection*source;
        var escaped=reference.GetProperty("NativeEscapedWatts").GetDouble();
        var sensor=reference.GetProperty("NativeSensorIntegratedWatts").GetDouble();
        var absorbed=reference.GetProperty("NativeAbsorbedWatts").GetDouble();
        Assert.Equal(source,escaped+absorbed,12);
        Assert.Equal(reference.GetProperty("CoreLegacyFullHemisphereWatts").GetDouble(),computed,12);
        Assert.InRange(escaped-sensor,5e-5,6e-5);
        Assert.InRange(escaped-computed,.01840,.01841);
        Assert.InRange(sensor-computed,.01834,.01835);
        Record("grid5-runtime-gap",new{Protocol=model.Data.ComputationProtocol,ComputedWatts=computed,NativeEscapedWatts=escaped,NativeFiniteSensorWatts=sensor,SceneGapWatts=escaped-computed,FiniteSensorGapWatts=sensor-computed,CommercialPowerGatePassed=false});
    }

    [Theory] [InlineData(400, false)] [InlineData(550, false)] [InlineData(700, false)]
    [InlineData(400, true)] [InlineData(550, true)] [InlineData(700, true)]
    public void WhiteSamplingHasConsistentPowerAndProbability(double wavelength, bool runtime)
    {
        var model=Model(runtime);var c=SpeosV11XyzTests.Context(0,wavelength);
        var energy=model.Integrate(c);var random=new Random(94017);const int count=8192;
        double first=0,second=0;
        for(var i=0;i<count;i++)
        {
            var sample=model.Sample(c,random.NextDouble(),random.NextDouble());
            Assert.True(sample.Direction.Z>0);Assert.Equal(BsdfLobe.DiffuseReflection,sample.Lobe);
            Assert.Equal(energy.Total,sample.Value*sample.Direction.Z/sample.Pdf,11);
            first+=sample.Direction.Z;second+=sample.Direction.Z*sample.Direction.Z;
        }
        // Independent integration in solid angle of the evaluated density, not the sampler's CDF.
        const int steps=49152;double mass=0,mean=0,square=0;
        for(var i=0;i<steps;i++)
        {
            var theta=(i+.5)*Math.PI/(2*steps);var mu=Math.Cos(theta);
            var f=model.Evaluate(c,new(Math.Sin(theta),0,mu));
            var density=f*mu*Math.Sin(theta)*Math.PI*Math.PI/steps;
            mass+=density;mean+=density*mu;square+=density*mu*mu;
        }
        Assert.InRange(Math.Abs(mass-energy.Reflection),0,2e-9);
        mean/=mass;square/=mass;
        Assert.InRange(Math.Abs(first/count-mean),0,6*Math.Sqrt((square-mean*mean)/count));
        Assert.InRange(energy.Total,0,1);
        Record((runtime?"grid6":"grid5")+"-runtime-sampling-"+wavelength,new{WavelengthNm=wavelength,Count=count,Energy=energy.Total,IntegratedWatts=mass,ExpectedMeanCosine=mean,ObservedMeanCosine=first/count,ObservedMeanSquareCosine=second/count});
    }

    [Fact]
    public void NativeWhiteArchiveImportsItsDefinedConeWithoutAManualOverride()
    {
        var data=Model(true).Data; Assert.Equal("MeasuredBsdfGrid/6",data.ComputationProtocol);
        Assert.Null(data.SpeosPeak!.ManualCone); Assert.Equal(3,data.SpeosPeak.FileCones!.Count);
    }

    [Theory]
    [InlineData(400,.2697200184443873,.269720)]
    [InlineData(550,.3,.3)]
    [InlineData(700,.3302799815556128,.330279)]
    public void RuntimePowerMatchesIndependentSharedCellPredictionAndNativeTrace(double wavelength,double independent,double nativeEscaped)
    {
        var model=Model(true);var energy=model.Integrate(SpeosV11XyzTests.Context(0,wavelength));
        Assert.Equal(independent,energy.Reflection,12);Assert.Equal(0,energy.Transmission);
        // Capture-specific scene comparison: HTML rounds to 1 microW and the black emitter
        // occludes a small solid angle. This is distinct from the old pointwise 1e-10 gate.
        Assert.InRange(Math.Abs(energy.Reflection-nativeEscaped),0,2e-6);
        Record("grid6-native-power-"+wavelength,new{WavelengthNm=wavelength,CoreFullHemisphereWatts=energy.Reflection,NativeEscapedWatts=nativeEscaped,NativeSceneErrorWatts=energy.Reflection-nativeEscaped,IndependentPrediction=independent,Scope="1 W near-normal controlled Direct MC scene; not full directional or all-material certification"});
    }

    [Theory] [InlineData(6,.255776)] [InlineData(19,.225564)] [InlineData(45,.2)]
    public void NonNodeIncidencesMatchIndependentInterpolationAndHeldoutNativePower(double incidence,double nativeEscaped)
    {
        var energy=Model(true).Integrate(SpeosV11XyzTests.Context(incidence,400)).Reflection;
        var expected=incidence<30?.2697200184443873*(1-incidence/30)+.2*incidence/30:.2;
        Assert.Equal(expected,energy,12);Assert.InRange(Math.Abs(energy-nativeEscaped),0,2e-6);
        Record("grid6-heldout-power-"+incidence,new{IncidenceDegrees=incidence,CoreFullHemisphereWatts=energy,NativeEscapedWatts=nativeEscaped,IndependentPrediction=expected,AngularShapeCertified=false});
    }

    private static void Record(string name,object value)
    {
        var root=Environment.GetEnvironmentVariable("OPTICAL_COMMERCIAL_CALIBRATION_EVIDENCE_DIR");if(string.IsNullOrWhiteSpace(root))return;
        Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
    }
}
