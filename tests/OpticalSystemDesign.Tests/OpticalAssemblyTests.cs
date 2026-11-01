using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class OpticalAssemblyTests
{
    internal static Optic Prescription(bool immersed=false,bool curved=false,int glasses=2)
    {
        var optic=new Optic("Analytical cemented assembly");IMaterial ambient=immersed?new ConstantIndexMaterial("Declared liquid",1.33):new AirMaterial();
        var materials=new[]{ambient}.Concat(Enumerable.Range(0,glasses).Select(i=>(IMaterial)new ConstantIndexMaterial("Glass "+i,1.5+.1*i))).Append(ambient).ToArray();
        var list=new List<OpticalSurface>{new(){Thickness=0,MaterialBefore=ambient,MaterialAfter=ambient}};
        for(int i=0;i<=glasses;i++)list.Add(new(){Radius=curved?(i==0?40:-50-15*i):0,Thickness=i==glasses?10:4,
            MaterialBefore=materials[i],MaterialAfter=materials[i+1],SemiDiameter=10,MechanicalSemiDiameter=12,PhysicalAperture=new CircularAperture(10),IsStop=i==0});
        list.Add(new(){Radius=0,Thickness=0,MaterialBefore=ambient,MaterialAfter=ambient,SemiDiameter=20});
        optic.SurfaceGroup.Replace(list);optic.Wavelengths.Clear();optic.Wavelengths.Add(new(){Nanometers=550,IsPrimary=true});optic.Fields.Add(new());return optic;
    }
    internal static NonSequentialDocument Scene(Optic optic,bool backwardDetector=false)
    {
        var scene=SequentialOpticalAssemblyConverter.Convert(optic);var detector=scene.Objects[^1];
        scene.Replace(detector.Id,detector with{Parameters=((DetectorRectangleParameters)detector.Parameters) with{WidthMillimeters=100,HeightMillimeters=100,PixelsX=16,PixelsY=16,FrontOnly=false}});
        if(backwardDetector)scene.Insert(scene.Objects.Count,NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with{
            LocalCoordinateSystem=new(new(0,0,-4)),Parameters=new DetectorRectangleParameters(100,100,16,16,false,true)});
        return scene;
    }
    internal static RealRay Ray(double x=0,double angle=0,double z=-2,double power=1)=>new(new(x,0,z),new(Math.Sin(angle*Math.PI/180),0,Math.Cos(angle*Math.PI/180)),550,power);
    internal static NonSequentialDocumentTraceResult Trace(Optic optic,NonSequentialDocument scene,RealRay? ray=null,NonSequentialSplittingMode mode=NonSequentialSplittingMode.None,int segments=100)
        =>new NonSequentialDocumentTracer().Trace(scene,optic.Materials,new(DirectRay:ray??Ray(),SplittingMode:mode,MaximumSegmentsPerRay:segments,MinimumRelativeIntensity:1e-14,MaximumRetainedBranches:10000));
    internal static void Balanced(NonSequentialDocumentTraceResult trace)=>Assert.InRange(Math.Abs(trace.EnergyBalance.AccountedPowerWatts-trace.EnergyBalance.SourcePowerWatts),0,1e-12);
    internal static void Evidence(string name,object value)
    {
        var directory=Environment.GetEnvironmentVariable("OPTICAL_ASSEMBLY_EVIDENCE_DIR");if(string.IsNullOrWhiteSpace(directory))return;
        Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
    }

    [Theory] [InlineData(false,2)] [InlineData(true,2)] [InlineData(false,3)] [InlineData(true,3)]
    public void EveryOriginalInterfaceAndItsActualMediaArePreserved(bool immersed,int glasses)
    {
        var optic=Prescription(immersed,glasses:glasses);var scene=Scene(optic);var group=Assert.IsType<OpticalAssemblyParameters>(scene.Objects[0].Parameters);
        Assert.Equal(glasses+1,group.Interfaces.Count);Assert.Equal(immersed?"Declared liquid":"Air",scene.AmbientMaterial);
        for(int i=0;i<group.Interfaces.Count;i++)
        {
            var expected=optic.SurfaceGroup.Items[i+1];var actual=group.Interfaces[i];Assert.Equal(i+1,actual.SourceSurfaceNumber);
            Assert.Equal(expected.SemiDiameter,actual.SemiDiameterMillimeters);Assert.Equal(expected.MechanicalSemiDiameter,actual.MechanicalSemiDiameterMillimeters);
            Assert.Equal(expected.MaterialBefore.RefractiveIndex(550),actual.Before.RefractiveIndex(550));Assert.Equal(expected.MaterialAfter.RefractiveIndex(550),actual.After.RefractiveIndex(550));
        }
        var trace=Trace(optic,scene);Balanced(trace);var path=Assert.Single(trace.Branches).Segments;
        Assert.Equal(Enumerable.Range(1,glasses+1),path.Take(glasses+1).Select(s=>s.FaceNumber));Assert.Equal(glasses+2,path.Count);
        // 2 mm outside, 4 mm in each actual glass, then 10 mm outside.
        var outside=immersed?1.33:1;var expectedOpl=12*outside+Enumerable.Range(0,glasses).Sum(i=>4*(1.5+.1*i));
        Assert.InRange(Math.Abs(path[^1].CumulativeOpticalPathLength-expectedOpl),0,2e-11);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void CurvedMainPathMatchesSharedSequentialIntersectionsDirectionsAndOpticalPath(bool immersed)
    {
        var optic=Prescription(immersed,true);var scene=Scene(optic);var source=Ray(.3,2);var expected=source;double length=0,opl=0;
        var sequential=new List<(Vector3D Position,Vector3D Direction,double Opl)>();
        foreach(var surface in optic.SurfaceGroup.Items.Skip(1))
        {
            var result=surface.TraceRay(expected,surface.MaterialBefore,surface.MaterialAfter,length,opl);expected=result.Ray;length=result.CumulativePathLength;opl=result.CumulativeOpticalPathLength;
            sequential.Add((result.Sample.Position,result.Ray.Direction,opl));
        }
        var trace=Trace(optic,scene,source);Balanced(trace);var segments=Assert.Single(trace.Branches).Segments;Assert.Equal(sequential.Count,segments.Count);
        for(int i=0;i<segments.Count;i++){Assert.InRange((segments[i].End-sequential[i].Position).Length,0,2e-11);Assert.InRange((segments[i].OutgoingDirection-sequential[i].Direction).Length,0,2e-11);Assert.InRange(Math.Abs(segments[i].CumulativeOpticalPathLength-sequential[i].Opl),0,2e-11);}
        Evidence(immersed?"curved-immersion":"curved-air",new{AnalyticalNotMeasured=true,trace.EnergyBalance,Points=segments.Select((s,i)=>new{s.FaceNumber,s.End,s.OutgoingDirection,s.CumulativeOpticalPathLength,PositionErrorMillimeters=(s.End-sequential[i].Position).Length,DirectionError=(s.OutgoingDirection-sequential[i].Direction).Length,OplErrorMillimeters=Math.Abs(s.CumulativeOpticalPathLength-sequential[i].Opl)})});
    }
    [Fact]
    public void FullFresnelIncludesInternalGlassReflectionAndMatchesIndependentIncoherentPlatePower()
    {
        var optic=Prescription();var scene=Scene(optic,true);var trace=Trace(optic,scene,mode:NonSequentialSplittingMode.FullFresnel);Balanced(trace);
        double[] n=[1,1.5,1.6,1];double r=0,t=1;
        for(int i=2;i>=0;i--){var ri=Math.Pow((n[i]-n[i+1])/(n[i]+n[i+1]),2);var ti=1-ri;var denominator=1-ri*r;t=ti*t/denominator;r=ri+ti*ti*r/denominator;}
        Assert.InRange(Math.Abs(trace.Detectors.Single(d=>d.DetectorId==scene.Objects[1].Id).TotalPowerWatts-t),0,1e-12);
        Assert.Contains(trace.Branches,b=>b.Segments.Any(s=>s.FaceNumber==2&&s.InteractionKind==Core.Interactions.RayInteractionKind.Reflected));
        Assert.True(trace.EnergyBalance.TruncatedPowerWatts<1e-12);
        Evidence("full-branch-energy",new{AnalyticalNotMeasured=true,ExpectedTransmission=t,ExpectedReflection=r,trace.EnergyBalance,DetectorPower=trace.Detectors.Select(d=>new{d.DetectorId,d.TotalPowerWatts}),Branches=trace.TotalBranchCount});
    }
    [Fact]
    public void IndependentAperturesClipAtTheirOriginalFaceAndRemainImmutable()
    {
        var optic=Prescription();optic.SurfaceGroup.Items[2].PhysicalAperture=new RectangularAperture(.2,2);optic.SurfaceGroup.Items[2].SemiDiameter=2;
        var scene=Scene(optic);var group=(OpticalAssemblyParameters)scene.Objects[0].Parameters;var exported=group.Interfaces[1].Aperture!;exported.Numbers["halfWidth"]=100;
        Assert.Equal(.2,group.Interfaces[1].Aperture!.Numbers["halfWidth"]);
        var trace=Trace(optic,scene,Ray(.5));Balanced(trace);Assert.Equal(0,trace.Detectors[0].TotalPowerWatts);
        Assert.Equal(2,Assert.Single(trace.Branches).Segments[^1].FaceNumber);Assert.True(trace.EnergyBalance.AbsorbedPowerWatts>.9);
    }
    [Fact]
    public void ConstantCoatingsAreAppliedOnceAndTheirLossIsAbsorbed()
    {
        var optic=Prescription();double expected=1;foreach(var s in optic.SurfaceGroup.Items.Skip(1).Take(3)){s.CoatingModel=new SimpleCoatingModel(.8,.1);expected*=.8;}
        var trace=Trace(optic,Scene(optic));Balanced(trace);Assert.Equal(expected,trace.Detectors[0].TotalPowerWatts,12);
        Assert.Equal(.1*(1+.8+.64),trace.EnergyBalance.TruncatedPowerWatts,12);Assert.Equal(.1*(1+.8+.64),trace.EnergyBalance.AbsorbedPowerWatts,12);
    }
    [Fact]
    public void QuarterWaveFilmMatchesAnalyticalReflectanceAndSurvivesReverseTraversal()
    {
        var optic=Prescription(glasses:1);const double filmIndex=1.3;var first=optic.SurfaceGroup.Items[1];
        first.CoatingModel=new CoherentMultilayerCoating([new(new ConstantIndexMaterial("film",filmIndex),550/(4*filmIndex))]);
        var scene=Scene(optic);var detector=scene.Objects[1];scene.Replace(detector.Id,detector with{LocalCoordinateSystem=new(new(0,0,.5))});
        var expectedR=Math.Pow((1*1.5-filmIndex*filmIndex)/(1*1.5+filmIndex*filmIndex),2);
        var trace=Trace(optic,scene);Balanced(trace);Assert.Equal(1-expectedR,trace.Detectors[0].TotalPowerWatts,12);
        scene.Replace(detector.Id,detector with{LocalCoordinateSystem=new(new(0,0,-1))});
        var reverse=Trace(optic,scene,new(new(0,0,.25),new(0,0,-1),550));Balanced(reverse);Assert.Equal(trace.Detectors[0].TotalPowerWatts,reverse.Detectors[0].TotalPowerWatts,12);
    }
    [Fact]
    public void MultilayerOrderIsReversedWhenEnteringFromTheSubstrate()
    {
        var optic=Prescription(glasses:1);optic.SurfaceGroup.Items[1].CoatingModel=new CoherentMultilayerCoating([new(new ConstantIndexMaterial("L",1.3),70),new(new ConstantIndexMaterial("H",2.2),45)]);
        var scene=Scene(optic);var detector=scene.Objects[1];scene.Replace(detector.Id,detector with{LocalCoordinateSystem=new(new(0,0,.5))});
        var forward=Trace(optic,scene);scene.Replace(detector.Id,detector with{LocalCoordinateSystem=new(new(0,0,-1))});
        var backward=Trace(optic,scene,new(new(0,0,.25),new(0,0,-1),550));Balanced(forward);Balanced(backward);
        Assert.Equal(forward.Detectors[0].TotalPowerWatts,backward.Detectors[0].TotalPowerWatts,12);
    }
    [Fact]
    public void InteriorSourcesRimsAndTotalInternalReflectionKeepTheCorrectMedium()
    {
        var optic=Prescription(glasses:1);var scene=Scene(optic);var direct=Trace(optic,scene,Ray(z:2));Balanced(direct);
        Assert.Equal(2,Assert.Single(direct.Branches).Segments[0].FaceNumber);
        var tir=Trace(optic,scene,Ray(angle:50,z:2),segments:4);Balanced(tir);Assert.Contains(tir.Branches.SelectMany(b=>b.Segments),s=>s.InteractionKind==Core.Interactions.RayInteractionKind.TotalInternalReflection);
        var rim=Trace(optic,scene,new(new(0,0,2),new(1,0,0),550));Balanced(rim);Assert.Equal(3,Assert.Single(rim.Branches).Segments[0].FaceNumber);
    }
    [Fact]
    public void RolledFaceRetainsTheOrientationOfItsIndependentRectangularAperture()
    {
        var optic=Prescription();var face=optic.SurfaceGroup.Items[2];face.PhysicalAperture=new RectangularAperture(.2,2);
        face.Label="Cemented stop";face.CoordinateSystem=face.CoordinateSystem with{RotationZDegrees=90};
        var scene=Scene(optic);var saved=((OpticalAssemblyParameters)scene.Objects[0].Parameters).Interfaces[1];
        Assert.Equal(90,saved.RollDegrees,12);Assert.Equal(face.Label,saved.SourceLabel);
        Assert.True(Trace(optic,scene,Ray(.5)).Detectors[0].TotalPowerWatts>0);
        var blocked=Trace(optic,scene,new(new(0,.5,-2),new(0,0,1),550));Balanced(blocked);Assert.Equal(0,blocked.Detectors[0].TotalPowerWatts);
        Assert.Equal(2,Assert.Single(blocked.Branches).Segments[^1].FaceNumber);
    }
    [Fact]
    public void ImageApertureAndFrozenFileApertureAreBothRetained()
    {
        var optic=Prescription();optic.SurfaceGroup.Items[2].PhysicalAperture=new FileAperture([(-1,-1),(1,-1),(1,1),(-1,1)],"unavailable-original.aperture",",",0);
        optic.SurfaceGroup.Items[^1].PhysicalAperture=new CircularAperture(.1);
        var scene=Scene(optic);var clipped=Trace(optic,scene,Ray(.5));Balanced(clipped);Assert.Equal(0,clipped.Detectors[0].TotalPowerWatts);
        Assert.Equal(scene.Objects[1].Id,Assert.Single(clipped.Branches).Segments[^1].ObjectId);
        Assert.True(Trace(optic,scene).Detectors[0].TotalPowerWatts>0);
        var fileClip=Trace(optic,scene,Ray(2));Assert.Equal(2,Assert.Single(fileClip.Branches).Segments[^1].FaceNumber);
    }
    [Theory] [InlineData(450)] [InlineData(550)] [InlineData(650)]
    public void DispersiveGlassBoundariesRetainTheirActualSpectralPowerAndOpticalPath(double wavelength)
    {
        var optic=Prescription(true);IMaterial[] media=[optic.SurfaceGroup.Items[1].MaterialBefore,optic.Materials.Resolve("Fused Silica"),new CauchyMaterial("declared dispersive glass",1.5,.004),optic.SurfaceGroup.Items[^1].MaterialBefore];
        for(int i=0;i<3;i++){optic.SurfaceGroup.Items[i+1].MaterialBefore=media[i];optic.SurfaceGroup.Items[i+1].MaterialAfter=media[i+1];}
        optic.Wavelengths[0].Nanometers=wavelength;var trace=Trace(optic,Scene(optic),Ray() with{WavelengthNanometers=wavelength});Balanced(trace);
        var n=media.Select(m=>m.RefractiveIndex(wavelength)).ToArray();var expected=Enumerable.Range(0,3).Aggregate(1.0,(p,i)=>p*(1-Math.Pow((n[i]-n[i+1])/(n[i]+n[i+1]),2)));
        Assert.Equal(expected,trace.Detectors[0].TotalPowerWatts,12);
        Assert.Equal(12*n[0]+4*n[1]+4*n[2],Assert.Single(trace.Branches).Segments[^1].CumulativeOpticalPathLength,11);
    }
    [Fact]
    public void ThinGlueLayerIsNotSkippedByTheLegacyRayOriginOffset()
    {
        var optic=Prescription(glasses:3);optic.SurfaceGroup.Items[2].Thickness=1e-8;optic.SurfaceGroup.Renumber();
        var trace=Trace(optic,Scene(optic));Balanced(trace);var path=Assert.Single(trace.Branches).Segments;
        Assert.Equal(new[]{1,2,3,4},path.Take(4).Select(s=>s.FaceNumber));Assert.InRange(path[2].SegmentLength,9.9999e-9,1.00001e-8);
        Assert.Equal(12+4*1.5+1e-8*1.6+4*1.7,path[^1].CumulativeOpticalPathLength,11);
    }
    [Fact]
    public void AbsorbingFilmAccountsForLossAndHasReciprocalPowerTransmission()
    {
        var optic=Prescription(glasses:1);optic.SurfaceGroup.Items[1].CoatingModel=new CoherentMultilayerCoating([new(new ConstantIndexMaterial("absorbing film",1.7,.15),100)]);
        var scene=Scene(optic);var detector=scene.Objects[1];scene.Replace(detector.Id,detector with{LocalCoordinateSystem=new(new(0,0,.5))});
        var forward=Trace(optic,scene);Balanced(forward);Assert.True(forward.EnergyBalance.AbsorbedPowerWatts>.1);
        scene.Replace(detector.Id,detector with{LocalCoordinateSystem=new(new(0,0,-1))});var reverse=Trace(optic,scene,new(new(0,0,.25),new(0,0,-1),550));Balanced(reverse);
        Assert.Equal(forward.Detectors[0].TotalPowerWatts,reverse.Detectors[0].TotalPowerWatts,12);
    }
    [Fact]
    public void StochasticSplittingIsSeededAndAgreesWithFullBranchPowerWithinSamplingError()
    {
        const int count=40000;var optic=Prescription();var scene=Scene(optic,true);
        scene.Insert(scene.Objects.Count,NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourcePoint) with{
            LocalCoordinateSystem=new(new(0,0,-2)),Parameters=new SourcePointParameters(ConeHalfAngleDegrees:0,AnalysisRayCount:count)});
        var tracer=new NonSequentialDocumentTracer();var options=new NonSequentialDocumentTraceRequest(SplittingMode:NonSequentialSplittingMode.SimpleStochastic,RandomSeed:617,OutputMode:NonSequentialTraceOutputMode.SummaryOnly);
        var a=tracer.Trace(scene,optic.Materials,options);var b=tracer.Trace(scene,optic.Materials,options);Balanced(a);Balanced(b);
        var expected=Trace(optic,scene,mode:NonSequentialSplittingMode.FullFresnel).Detectors.Single(d=>d.DetectorId==scene.Objects[1].Id).TotalPowerWatts;
        var actual=a.Detectors.Single(d=>d.DetectorId==scene.Objects[1].Id).TotalPowerWatts;
        Assert.Equal(a.EnergyBalance,b.EnergyBalance);Assert.Equal(a.Detectors[0].TotalPowerWatts,b.Detectors[0].TotalPowerWatts);
        Assert.InRange(Math.Abs(actual-expected),0,6*Math.Sqrt(expected*(1-expected)/count));
        Evidence("stochastic-statistics",new{AnalyticalNotMeasured=true,RayCount=count,Seed=617,ExpectedTransmission=expected,ActualTransmission=actual,Sigma=Math.Sqrt(expected*(1-expected)/count),ExactSeedReplay=true,a.EnergyBalance});
    }
    [Fact]
    public void CancellationAndInconsistentSceneAmbientFailExplicitly()
    {
        var optic=Prescription();using(var cancelled=Core.Services.ComputationCancellation.Push(new CancellationToken(true)))
            Assert.Throws<OperationCanceledException>(()=>SequentialOpticalAssemblyConverter.Convert(optic));
        var scene=Scene(optic);scene.AmbientMaterial="Water";Assert.Throws<InvalidDataException>(()=>scene.Validate());
    }
    [Fact]
    public void LegacyDefaultBeforeFieldsDoNotReplaceTheSequentialCarriedMedium()
    {
        var optic=Prescription(true);foreach(var surface in optic.SurfaceGroup.Items)surface.MaterialBefore=new AirMaterial();
        var scene=Scene(optic);var group=(OpticalAssemblyParameters)scene.Objects[0].Parameters;
        Assert.Equal("Declared liquid",group.Interfaces[0].MaterialBeforeName);Assert.Equal("Glass 0",group.Interfaces[1].MaterialBeforeName);
        Assert.All(optic.SurfaceGroup.Items,s=>Assert.IsType<AirMaterial>(s.MaterialBefore));Balanced(Trace(optic,scene));
    }
    [Fact]
    public void WeakBulkAbsorptionUsesActualKAndTheSharedSequentialAttenuation()
    {
        var optic=Prescription(glasses:1);const double k=2e-9;var glass=new ConstantIndexMaterial("weakly absorbing glass",1.5,k);
        optic.SurfaceGroup.Items[1].MaterialAfter=glass;optic.SurfaceGroup.Items[2].MaterialBefore=glass;
        var trace=Trace(optic,Scene(optic));Balanced(trace);
        var transmission=1-Math.Pow((1-1.5)/(1+1.5),2);var expected=transmission*transmission*Math.Exp(-4*Math.PI*k*4/(550e-6));
        Assert.Equal(expected,trace.Detectors[0].TotalPowerWatts,12);Assert.True(trace.EnergyBalance.AbsorbedPowerWatts>1e-5);
        var before=Ray(z:.1);var result=optic.SurfaceGroup.Items[2].TraceRay(before,glass,new AirMaterial(),0,0);
        Assert.Equal(Core.Propagation.HomogeneousPowerAttenuation.Evaluate(k,550,3.9),result.Ray.Intensity,14);
    }
    [Fact]
    public async Task NewPayloadRequiresV20IncludingDisabledObjectsAndRetainsItsEmbeddedComponents()
    {
        var optic=Prescription(true);optic.SurfaceGroup.Items[2].CoatingModel=new SimpleCoatingModel(.8,.1);var scene=Scene(optic);var obj=scene.Objects[0];scene.Replace(obj.Id,obj with{Enabled=false});
        using var files=new CameraSensorFileTests.TemporaryFiles();var file=files.Path("assembly.staropt");await StarOptProjectStore.SaveAsync(new([optic],0,NonSequentialDocument:scene),file);var bytes=File.ReadAllBytes(file);
        using var raw=new MemoryStream(bytes);raw.Position=52;using var unzip=new BrotliStream(raw,CompressionMode.Decompress);using var json=JsonDocument.Parse(unzip);Assert.Equal(20,json.RootElement.GetProperty("FormatVersion").GetInt32());
        var restored=StarOptProjectStore.Deserialize(bytes);Assert.IsType<OpticalAssemblyParameters>(restored.NonSequentialDocument!.Objects[0].Parameters);
        var changed=System.Text.Json.Nodes.JsonNode.Parse(json.RootElement.GetRawText())!;changed["FormatVersion"]=19;var payload=System.Text.Encoding.UTF8.GetBytes(changed.ToJsonString());using var packed=new MemoryStream();using(var compressor=new BrotliStream(packed,CompressionLevel.Optimal,true))compressor.Write(payload);
        var header=bytes[..52];System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12),payload.Length);System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16),(int)packed.Length);System.Security.Cryptography.SHA256.HashData(payload).CopyTo(header,20);
        Assert.Contains("version 20",Assert.Throws<InvalidDataException>(()=>StarOptProjectStore.Deserialize(header.Concat(packed.ToArray()).ToArray())).Message);
    }
    [Fact]
    public async Task InfinitePlaneRadiusRetainsItsValueThroughSceneHashAndProjectRoundtrip()
    {
        var optic=Prescription();foreach(var face in optic.SurfaceGroup.Items.Skip(1).Take(3))face.Radius=double.PositiveInfinity;
        var scene=Scene(optic);var hash=NonSequentialSceneHasher.Compute(scene,optic.Materials);
        using var files=new CameraSensorFileTests.TemporaryFiles();var file=files.Path("infinite-plane.staropt");
        await StarOptProjectStore.SaveAsync(new([optic],0,NonSequentialDocument:scene),file);
        var restored=StarOptProjectStore.Deserialize(File.ReadAllBytes(file)).NonSequentialDocument!;
        Assert.All(((OpticalAssemblyParameters)restored.Objects[0].Parameters).Interfaces,f=>Assert.True(double.IsPositiveInfinity(f.RadiusMillimeters)));
        Assert.Equal(hash,NonSequentialSceneHasher.Compute(restored,optic.Materials));Balanced(Trace(optic,restored));
    }
}
