using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Serialization;
using ContractKind=OpticalSystemDesign.Application.Contracts.NonSequentialObjectKind;
using ContractDetector=OpticalSystemDesign.Application.Contracts.DetectorRectangleParameters;

namespace OpticalSystemDesign.Tests;

public sealed class OpticalAssemblyApplicationTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CompleteConversionPreservesSourceAndFeedsDatabaseCameraAndFrozenReplay(bool immersed)
    {
        var optic=OpticalAssemblyTests.Prescription(immersed);optic.SurfaceGroup.Items[2].CoatingModel=new SimpleCoatingModel(.9,.05);
        using var files=new CameraSensorFileTests.TemporaryFiles();var input=files.Path("source.staropt");await StarOptProjectStore.SaveAsync(new([optic],0),input);
        using var app=WorkbenchApplication.Create();await app.Documents.OpenAsync(input);var before=JsonSerializer.Serialize(app.Prescription.GetSurfaces());
        app.NonSequential.ConvertFromSequential();Assert.Equal(before,JsonSerializer.Serialize(app.Prescription.GetSurfaces()));app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
        var document=app.NonSequential.GetDocument();var assembly=document.Objects.Single(o=>o.Kind==ContractKind.OpticalAssembly);
        var detector=document.Objects.Single(o=>o.Kind==ContractKind.DetectorRectangle);var parameters=(ContractDetector)detector.Parameters;
        Assert.NotNull(parameters.IncidentSurface);
        app.NonSequential.UpdateObject(new(detector.Id,true,true,detector.Kind,detector.Name,null,null,detector.X,detector.Y,detector.Z,0,0,0,
            parameters with{WidthMillimeters=3.2,HeightMillimeters=3.2,PixelsX=32,PixelsY=32}));
        var source=app.NonSequential.AddObject(ContractKind.SourceRay);
        app.NonSequential.UpdateObject(new(source,true,true,ContractKind.SourceRay,"fixed analytical light",null,null,0,0,-2,0,0,0,
            new OpticalSystemDesign.Application.Contracts.SourceRayParameters(1e-13,1,new(0,0,0),new(0,0,1))));
        var trace=await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode:OpticalSystemDesign.Application.Contracts.NonSequentialSplittingMode.FullFresnel));
        Assert.InRange(Math.Abs((trace.AbsorbedPowerWatts+trace.DetectorPowerWatts+trace.EscapedPowerWatts+trace.TruncatedPowerWatts)/trace.SourcePowerWatts-1),0,1e-12);
        var evidence=Environment.GetEnvironmentVariable("OPTICAL_ASSEMBLY_EVIDENCE_DIR");
        if(!string.IsNullOrWhiteSpace(evidence)){Directory.CreateDirectory(evidence);File.Copy(trace.RayDatabasePath!,Path.Combine(evidence,(immersed?"immersion":"air")+".starrdb"),true);}
        var capture=await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector.Id,"D2");
        var request=CameraNonSequentialServiceTests.Request(capture) with{Sensor=CameraNonSequentialServiceTests.Clean with{PixelPitchMicrometers=100,GainDnPerElectron=1}};
        var run=await app.CameraImaging.SimulateAsync(request);Assert.True(run.MeanPhotoElectrons>0);Assert.Contains(run.RawDn,v=>v>0);Assert.Contains(run.Developed.Rgb24,v=>v>0);
        const double hc=6.62607015e-34*299792458;double photonError=0;
        for(int y=0;y<32;y++)for(int x=0;x<32;x++)
        {
            var qe=(y%2,x%2) switch{(0,0)=>.2,(1,1)=>.8,_=>.5};var expected=capture.Bands.Sum(b=>b.PixelPowerWatts[(31-y)*32+x]*.01*b.WavelengthNanometers*1e-9/hc*qe);
            photonError=Math.Max(photonError,Math.Abs(expected-run.PhotoElectrons[y*32+x]));
        }
        Assert.InRange(photonError,0,1e-11);
        var zip=files.Path("assembly.camera-experiment.zip");await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier,1,run,run),zip);
        var project=files.Path("converted.staropt");await app.Documents.SaveAsync(project);await app.NonSequentialAnalysis.ClearDetectorsAsync();app.Documents.NewBlank();await app.Documents.OpenAsync(project);
        var reopened=Assert.IsType<OpticalSystemDesign.Application.Contracts.OpticalAssemblyParameters>(app.NonSequential.GetDocument().Objects.Single(o=>o.Id==assembly.Id).Parameters);
        Assert.Equal(3,reopened.Assembly.Interfaces.Count);Assert.Equal(.9,reopened.Assembly.Interfaces[1].Coating.Numbers["transmittance"]);
        app.Documents.NewBlank();var experiment=await app.CameraImaging.OpenExperimentAsync(zip);var replay=await app.CameraImaging.RecomputeAsync(experiment.B);
        Assert.Equal(run.PhotoElectrons,replay.PhotoElectrons);Assert.Equal(run.RawDn,replay.RawDn);Assert.Equal(run.Developed.Rgb24,replay.Developed.Rgb24);
        if(!string.IsNullOrWhiteSpace(evidence))
        {
            Directory.CreateDirectory(evidence);var name=immersed?"immersion":"air";File.Copy(project,Path.Combine(evidence,name+".staropt"),true);File.Copy(zip,Path.Combine(evidence,name+".camera-experiment.zip"),true);
            File.WriteAllText(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(new{AnalyticalNotMeasured=true,Immersed=immersed,ProjectVersion=20,PhotonErrorElectrons=photonError,trace.SourcePowerWatts,trace.DetectorPowerWatts,trace.AbsorbedPowerWatts,trace.EscapedPowerWatts,trace.TruncatedPowerWatts,ExactFrozenReplay=true},new JsonSerializerOptions{WriteIndented=true}));
        }
    }

    [Fact]
    public void UnsupportedConversionLeavesOriginalSceneDocumentStateAndRowsUntouched()
    {
        using var app=WorkbenchApplication.Create("cooke");var existing=app.NonSequential.AddObject(ContractKind.Sphere);var row=app.Prescription.GetSurfaces()[1];app.Prescription.UpdateSurface(row with{Thickness=-1});
        var options=new JsonSerializerOptions{NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals};
        var before=JsonSerializer.Serialize(app.NonSequential.GetDocument());var originalDocument=app.Documents.GetSnapshot();var originalRows=JsonSerializer.Serialize(app.Prescription.GetSurfaces(),options);
        Assert.Throws<InvalidDataException>(()=>app.NonSequential.ConvertFromSequential());
        Assert.Equal(before,JsonSerializer.Serialize(app.NonSequential.GetDocument()));Assert.Equal(originalRows,JsonSerializer.Serialize(app.Prescription.GetSurfaces(),options));Assert.Equal(originalDocument,app.Documents.GetSnapshot());
        Assert.Equal(existing,Assert.Single(app.NonSequential.GetDocument().Objects).Id);
    }
    [Fact]
    public void UnsupportedPhysicalPropertiesFailExplicitlyRatherThanBeingDropped()
    {
        var optic=OpticalAssemblyTests.Prescription();optic.SurfaceGroup.Items[2].Geometry=new EvenAsphereGeometry(-50,0,[1e-5]);
        Assert.Throws<NotSupportedException>(()=>SequentialOpticalAssemblyConverter.Convert(optic));
        optic=OpticalAssemblyTests.Prescription();optic.SurfaceGroup.Items[1].CoatingModel=new ApproximateTransmissionRippleCoating([]);
        Assert.Throws<NotSupportedException>(()=>SequentialOpticalAssemblyConverter.Convert(optic));
        optic=OpticalAssemblyTests.Prescription();optic.SurfaceGroup.Items[^1].CoatingModel=new SimpleCoatingModel(.8,.1);
        Assert.Throws<NotSupportedException>(()=>SequentialOpticalAssemblyConverter.Convert(optic));
    }
}
