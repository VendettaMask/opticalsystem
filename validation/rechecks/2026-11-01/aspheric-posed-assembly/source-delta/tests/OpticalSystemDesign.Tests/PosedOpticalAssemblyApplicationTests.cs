using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class PosedOpticalAssemblyApplicationTests
{
    [Theory] [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public async Task ThreeBandPosedAspheresAndScatteringFeedOnePhotonConversionAndExactFrozenReplay(bool immersed,bool scattered)
    {
        var optic=PosedOpticalAssemblyTests.Prescription(immersed,scattered);
        optic.SurfaceGroup.Items[2].CoatingModel=new CoherentMultilayerCoating([new(new ConstantIndexMaterial("physical L",1.3),70),new(new ConstantIndexMaterial("physical H",2.2,.01),45)]);
        optic.Wavelengths.Clear();foreach(var (wave,weight) in new[]{(450d,.2),(550d,.3),(650d,.5)})optic.Wavelengths.Add(new(){Label=wave+" nm",Nanometers=wave,Weight=weight,IsPrimary=wave==550});
        using var files=new CameraSensorFileTests.TemporaryFiles();var input=files.Path("source.staropt");await StarOptProjectStore.SaveAsync(new([optic],0),input);
        using var app=WorkbenchApplication.Create();await app.Documents.OpenAsync(input);var sourceRows=JsonSerializer.Serialize(app.Prescription.GetSurfaces());
        app.NonSequential.ConvertFromSequential();Assert.Equal(sourceRows,JsonSerializer.Serialize(app.Prescription.GetSurfaces()));app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
        var detector=app.NonSequential.GetDocument().Objects.Single(o=>o.Kind==NonSequentialObjectKind.DetectorRectangle);
        app.NonSequential.UpdateObject(new(detector.Id,true,true,detector.Kind,detector.Name,null,null,detector.X,detector.Y,detector.Z,detector.TiltXDegrees,detector.TiltYDegrees,detector.TiltZDegrees,
            ((DetectorRectangleParameters)detector.Parameters) with{WidthMillimeters=3.2,HeightMillimeters=3.2,PixelsX=32,PixelsY=32}));
        var count=scattered?1024:64;
        for(int band=0;band<3;band++)
        {
            var source=app.NonSequential.AddObject(NonSequentialObjectKind.SourceRectangle);
            app.NonSequential.UpdateObject(new(source,true,true,NonSequentialObjectKind.SourceRectangle,"declared spectral source",null,null,0,0,scattered?-1:-2,0,scattered?180:0,0,
                new SourceRectangleParameters(.1,.1,0,new[]{.2,.3,.5}[band]*1e-12,band+1,20,count)));
        }
        if(scattered)
        {
            var plane=app.NonSequential.AddObject(NonSequentialObjectKind.PlaneRectangle);
            app.NonSequential.UpdateObject(new(plane,true,true,NonSequentialObjectKind.PlaneRectangle,"Lambertian illumination",null,null,0,0,-2,0,0,0,
                new PlaneRectangleParameters(30,30,NonSequentialSurfaceBehavior.Absorbing,"Air","Air"),new(NonSequentialBsdfKind.LambertianReflection,.6)));
        }
        var options=new NonSequentialTraceRunRequestDto(SplittingMode:NonSequentialSplittingMode.FullFresnel,RandomSeed:617,RayCountOverride:count);
        var trace=await app.NonSequentialAnalysis.TraceAsync(options);
        var closure=Math.Abs((trace.DetectorPowerWatts+trace.AbsorbedPowerWatts+trace.EscapedPowerWatts+trace.TruncatedPowerWatts)/trace.SourcePowerWatts-1);Assert.InRange(closure,0,1e-11);
        if(scattered)Assert.True(trace.BsdfStatistics!.SampleCount>=3*count);
        var capture=await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector.Id,"D2");Assert.Equal(3,capture.Bands.Count);
        var all=await app.CameraImaging.SimulateAsync(CameraNonSequentialServiceTests.Request(capture) with{Sensor=CameraNonSequentialServiceTests.Clean with{PixelPitchMicrometers=100,GainDnPerElectron=1}});
        Assert.True(all.MeanPhotoElectrons>0);Assert.Contains(all.RawDn,d=>d>0);Assert.Contains(all.Developed.Rgb24,d=>d>0);
        const double hc=6.62607015e-34*299792458;double photonError=0;
        for(int y=0;y<32;y++)for(int x=0;x<32;x++)
        {
            var qe=(y%2,x%2) switch{(0,0)=>.2,(1,1)=>.8,_=>.5};var expected=capture.Bands.Sum(b=>b.PixelPowerWatts[(31-y)*32+x]*.01*b.WavelengthNanometers*1e-9/hc*qe);
            photonError=Math.Max(photonError,Math.Abs(expected-all.PhotoElectrons[y*32+x]));
        }
        Assert.InRange(photonError,0,1e-11);
        var primary=await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector.Id,"!R1 & D2");var ghost=await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector.Id,"R1 & D2");
        double partitionError=0;
        for(int b=0;b<3;b++)for(int p=0;p<1024;p++)partitionError=Math.Max(partitionError,Math.Abs(capture.Bands[b].PixelPowerWatts[p]-primary.Bands[b].PixelPowerWatts[p]-ghost.Bands[b].PixelPowerWatts[p]));
        Assert.InRange(partitionError,0,1e-25);
        var evidence=Environment.GetEnvironmentVariable("OPTICAL_POSED_ASSEMBLY_EVIDENCE_DIR");var name=$"chain-{immersed}-{scattered}";
        if(!string.IsNullOrWhiteSpace(evidence)){Directory.CreateDirectory(evidence);File.Copy(trace.RayDatabasePath!,Path.Combine(evidence,name+".starrdb"),true);}
        var project=files.Path("posed.staropt");await app.Documents.SaveAsync(project);var experimentPath=files.Path("posed.camera-experiment.zip");await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier,1,all,all),experimentPath);
        await app.NonSequentialAnalysis.ClearDetectorsAsync();app.Documents.NewBlank();await app.Documents.OpenAsync(project);
        var assembly=(OpticalAssemblyParameters)app.NonSequential.GetDocument().Objects.Single(o=>o.Kind==NonSequentialObjectKind.OpticalAssembly).Parameters;
        Assert.Equal("OpticalAssembly/2",assembly.Assembly.GeometryProtocol);Assert.Equal(3,assembly.Assembly.Interfaces.Count);Assert.NotEqual(0,assembly.Assembly.Interfaces[1].Frame!.Origin.X);
        await app.NonSequentialAnalysis.TraceAsync(options);var recapture=await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector.Id,"D2");
        Assert.Equal(capture.Bands.SelectMany(b=>b.PixelPowerWatts),recapture.Bands.SelectMany(b=>b.PixelPowerWatts));
        app.Documents.NewBlank();var reopened=await app.CameraImaging.OpenExperimentAsync(experimentPath);var replay=await app.CameraImaging.RecomputeAsync(reopened.B);
        Assert.Equal(all.PhotoElectrons,replay.PhotoElectrons);Assert.Equal(all.RawDn,replay.RawDn);Assert.Equal(all.Developed.Rgb24,replay.Developed.Rgb24);
        PosedOpticalAssemblyTests.Evidence(name,new{AnalyticalNotMeasured=true,Immersed=immersed,Scattered=scattered,ProjectVersion=21,Bands=3,SourceRaysPerBand=count,EnergyRelativeError=closure,PhotonErrorElectrons=photonError,PartitionErrorWatts=partitionError,ExactNscRetrace=true,ExactFrozenReplay=true,trace.SourcePowerWatts,trace.DetectorPowerWatts,trace.AbsorbedPowerWatts,trace.EscapedPowerWatts,trace.TruncatedPowerWatts,trace.BsdfStatistics});
        if(!string.IsNullOrWhiteSpace(evidence)){File.Copy(project,Path.Combine(evidence,name+".staropt"),true);File.Copy(experimentPath,Path.Combine(evidence,name+".camera-experiment.zip"),true);}
    }

    [Fact]
    public async Task FailedCrossingOrUncertifiableTiltKeepsTheOriginalSceneAndDirtyState()
    {
        using var files=new CameraSensorFileTests.TemporaryFiles();var optic=PosedOpticalAssemblyTests.Prescription();optic.SurfaceGroup.Items[2].CoordinateSystem=optic.SurfaceGroup.Items[2].CoordinateSystem with{RotationYDegrees=89};
        var file=files.Path("uncertifiable.staropt");await StarOptProjectStore.SaveAsync(new([optic],0),file);using var app=WorkbenchApplication.Create();await app.Documents.OpenAsync(file);
        app.NonSequential.AddObject(NonSequentialObjectKind.Sphere);var before=JsonSerializer.Serialize(app.NonSequential.GetDocument());var state=app.Documents.GetSnapshot();var rows=JsonSerializer.Serialize(app.Prescription.GetSurfaces());
        Assert.Throws<NotSupportedException>(()=>app.NonSequential.ConvertFromSequential());Assert.Equal(before,JsonSerializer.Serialize(app.NonSequential.GetDocument()));Assert.Equal(state,app.Documents.GetSnapshot());Assert.Equal(rows,JsonSerializer.Serialize(app.Prescription.GetSurfaces()));
    }
}
