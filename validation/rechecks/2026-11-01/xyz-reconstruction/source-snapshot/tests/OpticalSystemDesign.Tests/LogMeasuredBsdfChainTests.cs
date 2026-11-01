using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using CoreNsc=OpticalSystemDesign.Core.NonSequential;

namespace OpticalSystemDesign.Tests;

public sealed class LogMeasuredBsdfChainTests
{
    [Fact]
    public async Task PreviousPhaseGridNineExperimentReplaysExactly()
    {
        using var app=WorkbenchApplication.Create();var opened=await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","camera-polar-grid9-frozen.zip"));
        Assert.Equal("MeasuredBsdfGrid/9",opened.B.Request.NscDetector!.SurfaceBsdfProtocol);var replay=await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(opened.B.PhotoElectrons,replay.PhotoElectrons);Assert.Equal(opened.B.RawDn,replay.RawDn);Assert.Equal(opened.B.Developed.Rgb24,replay.Developed.Rgb24);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ExplicitSupportSurvivesNscCameraProjectAndFrozenReplay(bool transmission)
    {
        using var app=WorkbenchApplication.Create();using var files=new CameraSensorFileTests.TemporaryFiles();
        app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);foreach(var o in app.NonSequential.GetDocument().Objects.ToArray())app.NonSequential.DeleteObject(o.Id);
        app.NonSequential.UpdateWavelengths([new(1,"green",550,1,true)]);
        Guid Add(NonSequentialObjectKind kind,double z,NonSequentialObjectParameters parameters,double tilt=0,NonSequentialSurfaceBsdf? bsdf=null)
        {var id=app.NonSequential.AddObject(kind);app.NonSequential.UpdateObject(new(id,true,true,kind,kind.ToString(),null,null,0,0,z,0,tilt,0,parameters,bsdf));return id;}
        var original=MeasuredAngularCoverageTests.Partial(transmission);var data=new OpticalSystemDesign.Core.Scattering.MeasuredBsdfData(original.Name,original.SourceFileName,original.SourceSha256,original.Points,angularSupport:original.AngularSupport,logInterpolation:new(1e-6));
        var dto=new MeasuredBsdfDataDto(data.Name,data.SourceFileName,data.SourceSha256,data.DataSha256,data.Points.Select(p=>new MeasuredBsdfPointDto((int)p.Side,p.WavelengthNanometers,p.IncidentThetaDegrees,p.OutgoingThetaDegrees,p.OutgoingPhiDegrees,p.BrdfPerSteradian,p.BtdfPerSteradian)).ToArray(),AngularSupport:1,LogInterpolation:data.LogInterpolation);
        Add(NonSequentialObjectKind.SourcePoint,.032,new SourcePointParameters(1e-11,1,0,20,4096),160);
        var plane=Add(NonSequentialObjectKind.PlaneRectangle,0,new PlaneRectangleParameters(1,1,transmission?NonSequentialSurfaceBehavior.Refractive:NonSequentialSurfaceBehavior.Reflective,"AIR","AIR"),bsdf:new(NonSequentialBsdfKind.Measured,0,dto));
        var detector=Add(NonSequentialObjectKind.DetectorRectangle,transmission?-.064:.064,new DetectorRectangleParameters(3.2,3.2,32,32,false,true));
        var trace=await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode:NonSequentialSplittingMode.FullFresnel));
        Assert.InRange(Math.Abs((trace.DetectorPowerWatts+trace.AbsorbedPowerWatts+trace.EscapedPowerWatts+trace.TruncatedPowerWatts)/trace.SourcePowerWatts-1),0,1e-12);
        Assert.InRange(Math.Abs(1-trace.AbsorbedPowerWatts/trace.SourcePowerWatts-.1*Math.PI),0,1e-12);
        var input=await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector,transmission?"B2 & D3":"S2 & D3");Assert.Equal("MeasuredBsdfGrid/10",input.SurfaceBsdfProtocol);Assert.Equal(4096,input.BsdfStatistics!.SampleCount);
        var request=MeasuredBsdfChainTests.Request(input);request=request with{Sensor=request.Sensor! with{PixelPitchMicrometers=100}};var run=await app.CameraImaging.SimulateAsync(request);Assert.True(run.MeanPhotoElectrons>0);Assert.Contains(run.RawDn,v=>v>0);
        var project=files.Path("support.staropt");await app.Documents.SaveAsync(project);await app.NonSequentialAnalysis.ClearDetectorsAsync();app.Documents.NewBlank();await app.Documents.OpenAsync(project);
        Assert.Equal(1,app.NonSequential.GetDocument().Objects.Single(o=>o.Id==plane).SurfaceBsdf!.MeasuredData!.AngularSupport);
        var zip=files.Path("support.zip");await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier,1,run,run),zip);app.Documents.NewBlank();
        var opened=await app.CameraImaging.OpenExperimentAsync(zip);var replay=await app.CameraImaging.RecomputeAsync(opened.B);Assert.Equal(run.PhotoElectrons,replay.PhotoElectrons);Assert.Equal(run.RawDn,replay.RawDn);Assert.Equal(run.Developed.Rgb24,replay.Developed.Rgb24);
        var changed=input with{SurfaceBsdfProtocol="MeasuredBsdfGrid/2"};await Assert.ThrowsAsync<InvalidDataException>(()=>app.CameraImaging.SimulateAsync(request with{NscDetector=changed}));
    }
}
