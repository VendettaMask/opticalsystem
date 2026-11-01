using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class CommercialBsdfRuntimeWhiteTests
{
    [Theory]
    [InlineData("half",181,.218926)] [InlineData("quarter",361,.219296)]
    public void FullResolutionV11WhiteTablesStayWithinTheOriginalPayloadBudget(string name,int thetaCount,double nativeEscaped)
    {
        var file=Path.Combine(AppContext.BaseDirectory,"Fixtures","speos-white-"+name+"-degree.anisotropicbsdf");
        var data=BsdfFileImporter.Read(file,new(BsdfImportSides.Front));
        Assert.Equal("MeasuredBsdfGrid/6",data.ComputationProtocol);
        Assert.Equal(thetaCount,data.Points.Select(p=>p.OutgoingThetaDegrees).Distinct().Count());
        Assert.InRange(data.PayloadPointCount,1,MeasuredBsdfData.MaximumPoints);
        var energy=new MeasuredBsdf(data).Integrate(SpeosV11XyzTests.Context(0,400));
        Assert.InRange(Math.Abs(energy.Reflection-nativeEscaped),0,2e-6);
    }

    [Theory] [InlineData(.01)] [InlineData(0)]
    public void SharedCellRejectsNonpassiveCornersAndUndefinedDiffuseReferences(double referenceDiffuse)
    {
        var diffuse=new List<MeasuredBsdfPoint>();var peak=new List<MeasuredBsdfPoint>();
        foreach(var input in new[]{0d,60})foreach(var output in new[]{0d,90})foreach(var phi in new[]{0d,360})
        {
            diffuse.Add(new(BsdfSide.Front,550,input,output,phi,(input==0?.9:referenceDiffuse)/Math.PI,0));
            peak.Add(new(BsdfSide.Front,550,input,output,phi,(input==0?0:.1-referenceDiffuse)/Math.PI,0));
        }
        var spectrum=new SpeosSpectralDefinition([new(false,new[]{400d,700},new[]{.1,1d},1)],true,true);
        var error=Assert.Throws<InvalidDataException>(()=>new MeasuredBsdfData("cell passivity","synthetic",new string('0',64),diffuse,
            speosPeak:new(1,1,550,peak),speosSpectral:spectrum));
        Assert.Contains(referenceDiffuse==0?"参考":"单元",error.Message);
    }

    [Fact]
    public async Task FrozenGridFiveExperimentRetainsItsProtocolAndExactRawIspReplay()
    {
        using var app=WorkbenchApplication.Create();
        var opened=await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","camera-speos-grid5-frozen.zip"));
        Assert.Equal("MeasuredBsdfGrid/5",opened.B.Request.NscDetector!.SurfaceBsdfProtocol);
        var replay=await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(opened.B.PhotoElectrons,replay.PhotoElectrons);Assert.Equal(opened.B.RawDn,replay.RawDn);Assert.Equal(opened.B.Developed.Rgb24,replay.Developed.Rgb24);
    }
}
