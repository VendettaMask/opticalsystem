using OpticalSystemDesign.Validation;
using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using CoreNsc = OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class XyzGamutChainTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task SignedDomainCalibrationReachesDatabaseProjectAndFrozenRawIsp(bool transmission)
    {
        using var app=WorkbenchApplication.Create();using var files=new CameraSensorFileTests.TemporaryFiles();var scene=await MeasuredBsdfChainTests.Setup(app,files);
        if(!transmission)app.NonSequential.UpdateObject(new(scene.Plane,true,true,NonSequentialObjectKind.PlaneRectangle,"gamut control",null,null,0,0,0,0,0,0,new PlaneRectangleParameters(1,1,NonSequentialSurfaceBehavior.Reflective,"AIR","AIR")));
        var xyz=files.Path("gamut.BSDF");var calibration=files.Path("gamut.json");File.WriteAllText(xyz,SpeosV11XyzTests.XyzText(transmission));File.WriteAllText(calibration,XyzGamutCalibrationTests.CalibrationJson());
        var model=await app.NonSequential.ImportMeasuredBsdfAsync(scene.Plane,xyz,options:new(BsdfImportSides.BothDeclaredIdentical,ZemaxXyzCalibrationPath:calibration));
        Assert.NotNull(model.MeasuredData!.ZemaxXyz!.Calibration.FitEvidence!.Gamut);
        var trace=await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode:NonSequentialSplittingMode.FullFresnel));
        Assert.InRange(Math.Abs((trace.AbsorbedPowerWatts+trace.DetectorPowerWatts+trace.EscapedPowerWatts+trace.TruncatedPowerWatts)/trace.SourcePowerWatts-1),0,1e-12);
        Assert.InRange(Math.Abs(trace.AbsorbedPowerWatts/trace.SourcePowerWatts-.68),0,1e-12);
        var detector=transmission?scene.TransmittedDetector:app.NonSequential.GetDocument().Objects.First(o=>o.Kind==NonSequentialObjectKind.DetectorRectangle).Id;
        var input=await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector,transmission?"B2 & D4":"S2 & R2 & D3");Assert.Equal("MeasuredBsdfGrid/7",input.SurfaceBsdfProtocol);
        var count=input.BsdfStatistics!.SampleCount;Assert.Equal(8192,count);Assert.InRange(Math.Abs(input.BsdfStatistics.MeanCosine-2d/3),0,6*Math.Sqrt(1d/(18*count)));
        var run=await app.CameraImaging.SimulateAsync(MeasuredBsdfChainTests.Request(input));Assert.True(run.MeanPhotoElectrons>0);double maximum=0;const double hc=6.62607015e-34*299792458;
        for(var y=0;y<32;y++)for(var x=0;x<32;x++){var qe=(y%2,x%2) switch{(0,0)=>.2,(1,1)=>.8,_=>.5};var expected=input.Bands.Sum(b=>b.PixelPowerWatts[(31-y)*32+x]*.01*b.WavelengthNanometers*1e-9/hc*qe);maximum=Math.Max(maximum,Math.Abs(expected-run.PhotoElectrons[y*32+x]));Assert.InRange(Math.Abs(expected-run.PhotoElectrons[y*32+x]),0,1e-11);}
        var experiment=files.Path("gamut.camera-experiment.zip");await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier,1,run,run),experiment);
        var project=files.Path("gamut.staropt");await app.Documents.SaveAsync(project);
        using(var raw=EvidenceFiles.OpenRead(project)){raw.Position=52;using var z=new BrotliStream(raw,CompressionMode.Decompress);using var json=await JsonDocument.ParseAsync(z);Assert.Equal(15,json.RootElement.GetProperty("FormatVersion").GetInt32());}
        File.Delete(xyz);File.Delete(calibration);await app.NonSequentialAnalysis.ClearDetectorsAsync();app.Documents.NewBlank();await app.Documents.OpenAsync(project);
        Assert.Equal(model.MeasuredData.DataSha256,app.NonSequential.GetDocument().Objects.Single(o=>o.Id==scene.Plane).SurfaceBsdf!.MeasuredData!.DataSha256);app.Documents.NewBlank();
        var opened=await app.CameraImaging.OpenExperimentAsync(experiment);var replay=await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(run.PhotoElectrons,replay.PhotoElectrons);Assert.Equal(run.RawDn,replay.RawDn);Assert.Equal(run.Developed.Rgb24,replay.Developed.Rgb24);
        Assert.NotNull(replay.Request.NscDetector!.SurfaceBsdfs![0].Model.MeasuredData!.ZemaxXyz!.Calibration.FitEvidence!.Gamut);
        var output=Environment.GetEnvironmentVariable("OPTICAL_XYZ_GAMUT_EVIDENCE_DIR");if(!string.IsNullOrWhiteSpace(output)){Directory.CreateDirectory(output);File.Copy(experiment,Path.Combine(output,"gamut-chain-"+transmission+".camera-experiment.zip"),true);File.WriteAllText(Path.Combine(output,"gamut-chain-"+transmission+".json"),JsonSerializer.Serialize(new{Kind="Synthetic analytic transfer, not measured certification",ProjectVersion=15,Protocol=input.SurfaceBsdfProtocol,MaximumPhotonConversionError=maximum,input.BsdfStatistics,ExactReplay=true},new JsonSerializerOptions{WriteIndented=true}));}
    }
    [Fact]
    public async Task DisabledSignedModelsCannotBeSmuggledIntoOldProjectVersions()
    {
        var data=XyzGamutCalibrationTests.Read();var scene=CommercialBsdfNscTests.Scene();var plane=scene.Objects[1];
        scene.Replace(plane.Id,plane with{Enabled=false,Parameters=new CoreNsc.PlaneRectangleParameters(1,1,CoreNsc.NonSequentialSurfaceBehavior.Reflective,"AIR","AIR"),SurfaceBsdf=new(CoreNsc.NonSequentialBsdfKind.Measured,0,data)});
        using var files=new CameraSensorFileTests.TemporaryFiles();var project=files.Path("disabled.staropt");await StarOptProjectStore.SaveAsync(new([Optic.CreateBlank()],0,NonSequentialDocument:scene),project);var bytes=await EvidenceFiles.ReadAllBytesAsync(project);
        using var raw=new MemoryStream(bytes);raw.Position=52;using var z=new BrotliStream(raw,CompressionMode.Decompress);using var json=JsonDocument.Parse(z);
        var downgraded=System.Text.Json.Nodes.JsonNode.Parse(json.RootElement.GetRawText())!;Assert.Equal(15,(int)downgraded["FormatVersion"]!);downgraded["FormatVersion"]=14;
        var payload=System.Text.Encoding.UTF8.GetBytes(downgraded.ToJsonString());using var compressed=new MemoryStream();using(var compressor=new BrotliStream(compressed,CompressionLevel.Optimal,true))compressor.Write(payload);
        var header=bytes[..52];System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12),payload.Length);System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16),(int)compressed.Length);System.Security.Cryptography.SHA256.HashData(payload).CopyTo(header,20);
        Assert.Contains("version 15",Assert.Throws<InvalidDataException>(()=>StarOptProjectStore.Deserialize(header.Concat(compressed.ToArray()).ToArray())).Message);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void NewProtocolPersistsInRayDatabaseAndPreservesPhysicalPower(bool transmission)
    {
        var scene=CommercialBsdfNscTests.Scene();var plane=scene.Objects[1];var data=XyzGamutCalibrationTests.Read(transmission);
        scene.Replace(plane.Id,plane with{Parameters=new CoreNsc.PlaneRectangleParameters(1,1,transmission?CoreNsc.NonSequentialSurfaceBehavior.Refractive:CoreNsc.NonSequentialSurfaceBehavior.Reflective,"AIR","AIR"),SurfaceBsdf=new(CoreNsc.NonSequentialBsdfKind.Measured,0,data)});
        var materials=Optic.CreateBlank().Materials;using var stream=new MemoryStream();CoreNsc.NonSequentialDocumentTraceResult result;
        using(var writer=new CoreNsc.NonSequentialRayDatabaseWriter(stream,CoreNsc.NonSequentialRayDatabaseHeader.Create(scene,materials:materials),true)){result=new CoreNsc.NonSequentialDocumentTracer().Trace(scene,materials,sink:writer);writer.Complete();}
        Assert.InRange(Math.Abs(result.EnergyBalance.AccountedPowerWatts/result.EnergyBalance.SourcePowerWatts-1),0,1e-12);
        stream.Position=0;using var reader=new CoreNsc.NonSequentialRayDatabaseReader(stream);Assert.Equal("MeasuredBsdfGrid/7",reader.Header.SurfaceBsdfProtocol);
        Assert.NotEmpty(reader.ReadBranches(CoreNsc.NonSequentialPathFilter.Parse(transmission?"B2":"S2")));
    }
    [Fact]
    public async Task PriorGridSixWhiteExperimentKeepsExactFrozenReplay()
    {
        using var app=WorkbenchApplication.Create();var opened=await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","camera-speos-grid6-frozen.zip"));
        Assert.Equal("MeasuredBsdfGrid/6",opened.B.Request.NscDetector!.SurfaceBsdfProtocol);var replay=await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(opened.B.PhotoElectrons,replay.PhotoElectrons);Assert.Equal(opened.B.RawDn,replay.RawDn);Assert.Equal(opened.B.Developed.Rgb24,replay.Developed.Rgb24);
    }
    [Fact]
    public async Task OutOfDomainImportLeavesThePreviousEmbeddedModelUnchanged()
    {
        using var app=WorkbenchApplication.Create();using var files=new CameraSensorFileTests.TemporaryFiles();var scene=await MeasuredBsdfChainTests.Setup(app,files);
        var xyz=files.Path("domain.BSDF");var calibration=files.Path("domain.json");File.WriteAllText(xyz,SpeosV11XyzTests.XyzText(true));File.WriteAllText(calibration,XyzGamutCalibrationTests.CalibrationJson());
        var options=new BsdfImportOptions(BsdfImportSides.BothDeclaredIdentical,ZemaxXyzCalibrationPath:calibration);
        var original=await app.NonSequential.ImportMeasuredBsdfAsync(scene.Plane,xyz,options:options);
        File.WriteAllText(xyz,SpeosV11XyzTests.XyzText(true).Replace("TIS 0.2","TIS 0.9"));
        await Assert.ThrowsAsync<InvalidDataException>(()=>app.NonSequential.ImportMeasuredBsdfAsync(scene.Plane,xyz,options:options));
        Assert.Equal(original.MeasuredData!.DataSha256,app.NonSequential.GetDocument().Objects.Single(o=>o.Id==scene.Plane).SurfaceBsdf!.MeasuredData!.DataSha256);
    }
}
