using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using CoreNsc=OpticalSystemDesign.Core.NonSequential;

namespace OpticalSystemDesign.Tests;

public sealed class XyzAbsoluteRbfChainTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task AbsoluteXyzModelFeedsOnePhotonConversionAndReplaysWithoutSources(bool transmission)
    {
        using var app=WorkbenchApplication.Create();using var files=new CameraSensorFileTests.TemporaryFiles();var scene=await MeasuredBsdfChainTests.Setup(app,files);
        if(!transmission)app.NonSequential.UpdateObject(new(scene.Plane,true,true,NonSequentialObjectKind.PlaneRectangle,"positive control",null,null,0,0,0,0,0,0,new PlaneRectangleParameters(1,1,NonSequentialSurfaceBehavior.Reflective,"AIR","AIR")));
        var xyz=files.Path("positive.BSDF");var calibration=files.Path("positive.json");File.WriteAllText(xyz,SpeosV11XyzTests.XyzText(transmission));File.WriteAllText(calibration,XyzAbsoluteRbfTests.CalibrationJson());
        var model=await app.NonSequential.ImportMeasuredBsdfAsync(scene.Plane,xyz,options:new(BsdfImportSides.BothDeclaredIdentical,ZemaxXyzCalibrationPath:calibration));
        var trace=await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode:NonSequentialSplittingMode.FullFresnel));
        Assert.InRange(Math.Abs((trace.AbsorbedPowerWatts+trace.DetectorPowerWatts+trace.EscapedPowerWatts+trace.TruncatedPowerWatts)/trace.SourcePowerWatts-1),0,1e-12);
        Assert.InRange(Math.Abs(1-trace.AbsorbedPowerWatts/trace.SourcePowerWatts-XyzAbsoluteRbfTests.Energy(550)),0,1e-11);
        var detector=transmission?scene.TransmittedDetector:app.NonSequential.GetDocument().Objects.First(o=>o.Kind==NonSequentialObjectKind.DetectorRectangle).Id;
        var input=await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector,transmission?"B2 & D4":"S2 & R2 & D3");Assert.Equal("MeasuredBsdfGrid/11",input.SurfaceBsdfProtocol);
        Assert.Equal(8192,input.BsdfStatistics!.SampleCount);Assert.InRange(Math.Abs(input.BsdfStatistics.MeanCosine-2d/3),0,6*Math.Sqrt(1d/(18*8192)));
        var run=await app.CameraImaging.SimulateAsync(MeasuredBsdfChainTests.Request(input));Assert.True(run.MeanPhotoElectrons>0);double max=0;const double hc=6.62607015e-34*299792458;
        for(int y=0;y<32;y++)for(int x=0;x<32;x++){var qe=(y%2,x%2) switch{(0,0)=>.2,(1,1)=>.8,_=>.5};var expected=input.Bands.Sum(b=>b.PixelPowerWatts[(31-y)*32+x]*.01*b.WavelengthNanometers*1e-9/hc*qe);max=Math.Max(max,Math.Abs(expected-run.PhotoElectrons[y*32+x]));Assert.InRange(Math.Abs(expected-run.PhotoElectrons[y*32+x]),0,1e-11);}
        var experiment=files.Path("positive.camera-experiment.zip");await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier,1,run,run),experiment);
        var project=files.Path("positive.staropt");await app.Documents.SaveAsync(project);
        using(var raw=File.OpenRead(project)){raw.Position=52;using var z=new BrotliStream(raw,CompressionMode.Decompress);using var json=await JsonDocument.ParseAsync(z);Assert.Equal(19,json.RootElement.GetProperty("FormatVersion").GetInt32());}
        File.Delete(xyz);File.Delete(calibration);await app.NonSequentialAnalysis.ClearDetectorsAsync();app.Documents.NewBlank();await app.Documents.OpenAsync(project);
        Assert.Equal(model.MeasuredData!.DataSha256,app.NonSequential.GetDocument().Objects.Single(o=>o.Id==scene.Plane).SurfaceBsdf!.MeasuredData!.DataSha256);app.Documents.NewBlank();
        var opened=await app.CameraImaging.OpenExperimentAsync(experiment);var replay=await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(run.PhotoElectrons,replay.PhotoElectrons);Assert.Equal(run.RawDn,replay.RawDn);Assert.Equal(run.Developed.Rgb24,replay.Developed.Rgb24);
        var output=Environment.GetEnvironmentVariable("OPTICAL_XYZ_ABSOLUTE_EVIDENCE_DIR");if(!string.IsNullOrWhiteSpace(output)){Directory.CreateDirectory(output);File.Copy(experiment,Path.Combine(output,"positive-chain-"+transmission+".camera-experiment.zip"),true);File.WriteAllText(Path.Combine(output,"positive-chain-"+transmission+".json"),JsonSerializer.Serialize(new{Kind="Synthetic independent absolute XYZ control, not measured material",ProjectVersion=19,MaximumPhotonConversionError=max,input.BsdfStatistics,ExactReplay=true},new JsonSerializerOptions{WriteIndented=true}));}
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void AbsoluteXyzProtocolSurvivesRayDatabaseAndConservesPower(bool transmission)
    {
        var data=XyzAbsoluteRbfTests.Read(transmission);var scene=CommercialBsdfNscTests.Scene();var plane=scene.Objects[1];
        scene.Replace(plane.Id,plane with{Parameters=new CoreNsc.PlaneRectangleParameters(1,1,transmission?CoreNsc.NonSequentialSurfaceBehavior.Refractive:CoreNsc.NonSequentialSurfaceBehavior.Reflective,"AIR","AIR"),SurfaceBsdf=new(CoreNsc.NonSequentialBsdfKind.Measured,0,data)});
        var materials=Optic.CreateBlank().Materials;using var stream=new MemoryStream();CoreNsc.NonSequentialDocumentTraceResult trace;
        using(var writer=new CoreNsc.NonSequentialRayDatabaseWriter(stream,CoreNsc.NonSequentialRayDatabaseHeader.Create(scene,materials:materials),true)){trace=new CoreNsc.NonSequentialDocumentTracer().Trace(scene,materials,sink:writer);writer.Complete();}
        Assert.InRange(Math.Abs(trace.EnergyBalance.AccountedPowerWatts/trace.EnergyBalance.SourcePowerWatts-1),0,1e-12);
        stream.Position=0;using var reader=new CoreNsc.NonSequentialRayDatabaseReader(stream);Assert.Equal("MeasuredBsdfGrid/11",reader.Header.SurfaceBsdfProtocol);Assert.NotEmpty(reader.ReadBranches(CoreNsc.NonSequentialPathFilter.Parse(transmission?"B2":"S2")));
    }
    [Fact]
    public async Task DisabledAbsoluteXyzModelRejectsProjectDowngradeAndOldGridEightReplaysExactly()
    {
        var data=XyzAbsoluteRbfTests.Read();var scene=CommercialBsdfNscTests.Scene();var plane=scene.Objects[1];
        scene.Replace(plane.Id,plane with{Enabled=false,Parameters=new CoreNsc.PlaneRectangleParameters(1,1,CoreNsc.NonSequentialSurfaceBehavior.Reflective,"AIR","AIR"),SurfaceBsdf=new(CoreNsc.NonSequentialBsdfKind.Measured,0,data)});
        using var files=new CameraSensorFileTests.TemporaryFiles();var file=files.Path("positive.staropt");await StarOptProjectStore.SaveAsync(new([Optic.CreateBlank()],0,NonSequentialDocument:scene),file);var bytes=await File.ReadAllBytesAsync(file);
        using var raw=new MemoryStream(bytes);raw.Position=52;using var z=new BrotliStream(raw,CompressionMode.Decompress);using var json=JsonDocument.Parse(z);var changed=System.Text.Json.Nodes.JsonNode.Parse(json.RootElement.GetRawText())!;changed["FormatVersion"]=18;
        var payload=System.Text.Encoding.UTF8.GetBytes(changed.ToJsonString());using var packed=new MemoryStream();using(var compressor=new BrotliStream(packed,CompressionLevel.Optimal,true))compressor.Write(payload);
        var header=bytes[..52];System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12),payload.Length);System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16),(int)packed.Length);System.Security.Cryptography.SHA256.HashData(payload).CopyTo(header,20);
        Assert.Contains("version 19",Assert.Throws<InvalidDataException>(()=>StarOptProjectStore.Deserialize(header.Concat(packed.ToArray()).ToArray())).Message);
        using var app=WorkbenchApplication.Create();var opened=await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","camera-xyz-grid8-frozen.zip"));Assert.Equal("MeasuredBsdfGrid/8",opened.B.Request.NscDetector!.SurfaceBsdfProtocol);
        var replay=await app.CameraImaging.RecomputeAsync(opened.B);Assert.Equal(opened.B.PhotoElectrons,replay.PhotoElectrons);Assert.Equal(opened.B.RawDn,replay.RawDn);Assert.Equal(opened.B.Developed.Rgb24,replay.Developed.Rgb24);
    }
}
