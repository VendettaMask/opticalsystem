using OpticalSystemDesign.Validation;
using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class PosedOpticalAssemblyTests
{
    internal static Optic Prescription(bool immersed=false,bool odd=false,bool posed=true)
    {
        var optic=OpticalAssemblyTests.Prescription(immersed);
        foreach(var face in optic.SurfaceGroup.Items.Skip(1).Take(3))
        {face.SemiDiameter=3;face.SemiDiameterFixed=true;face.MechanicalSemiDiameter=4;face.PhysicalAperture=new CircularAperture(3);}
        optic.SurfaceGroup.Items[1].Geometry=odd?new OddAsphereGeometry(80,-.4,[0,5e-5,1e-6,-2e-8]):new EvenAsphereGeometry(80,-.4,[5e-5,2e-6,-1e-8]);
        optic.SurfaceGroup.Items[2].Geometry=new EvenAsphereGeometry(-120,-.2,[-3e-5,1e-6]);
        optic.SurfaceGroup.Items[3].Geometry=new StandardGeometry(-200);
        if(posed)
        {
            optic.SurfaceGroup.Items[2].CoordinateSystem=new(new(.12,-.08,4),.35,-.6,23);
            optic.SurfaceGroup.Items[3].CoordinateSystem=new(new(.2,-.1,8),-.2,.4,-17);
            optic.SurfaceGroup.Items[^1].CoordinateSystem=new(new(.15,-.07,18),.3,-.2,10);
        }
        optic.SurfaceGroup.Items[^1].SemiDiameter=10;
        optic.SurfaceGroup.Items[^1].SemiDiameterFixed=true;
        return optic;
    }
    internal static void Evidence(string name,object value)
    {
        var path=Environment.GetEnvironmentVariable("OPTICAL_POSED_ASSEMBLY_EVIDENCE_DIR");if(string.IsNullOrWhiteSpace(path))return;
        Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
    }
    internal static RealRay Source(double wavelength=550)=>new(new(.2,.1,-2),new Vector3D(.012,.004,1)/new Vector3D(.012,.004,1).Length,wavelength);

    [Theory]
    [InlineData(false,false,false)] [InlineData(false,false,true)] [InlineData(false,true,false)] [InlineData(false,true,true)]
    [InlineData(true,false,false)] [InlineData(true,false,true)] [InlineData(true,true,false)] [InlineData(true,true,true)]
    public void AsphericAndPosedMainPathsMatchTheSharedSequentialGeometryAndCarriedMedia(bool immersed,bool odd,bool posed)
    {
        var optic=Prescription(immersed,odd,posed);var scene=OpticalAssemblyTests.Scene(optic);var group=(OpticalAssemblyParameters)scene.Objects[0].Parameters;
        Assert.True(group.HasExtendedSurfaces);Assert.Equal("OpticalAssembly/2",group.GeometryProtocol);Assert.True(group.GeometryCertificateMargin>0);
        var ray=Source();var current=optic.SurfaceGroup.Items[0].MaterialAfter;double path=0,opl=0;
        var expected=new List<(Vector3D Position,Vector3D Direction,double Opl)>();
        foreach(var surface in optic.SurfaceGroup.Items.Skip(1))
        {
            var result=surface.TraceRay(ray,current,surface.MaterialAfter,path,opl);Assert.False(result.Sample.Vignetted);
            ray=result.Ray;current=surface.MaterialAfter;path=result.CumulativePathLength;opl=result.CumulativeOpticalPathLength;
            expected.Add((result.Sample.Position,ray.Direction,opl));
        }
        var trace=OpticalAssemblyTests.Trace(optic,scene,Source());OpticalAssemblyTests.Balanced(trace);
        var actual=Assert.Single(trace.Branches).Segments;Assert.Equal(expected.Count,actual.Count);
        var errors=actual.Select((s,i)=>new{s.FaceNumber,PositionErrorMillimeters=(s.End-expected[i].Position).Length,DirectionError=(s.OutgoingDirection-expected[i].Direction).Length,OplErrorMillimeters=Math.Abs(s.CumulativeOpticalPathLength-expected[i].Opl)}).ToArray();
        Assert.All(errors,e=>{Assert.InRange(e.PositionErrorMillimeters,0,2e-8);Assert.InRange(e.DirectionError,0,2e-8);Assert.InRange(e.OplErrorMillimeters,0,2e-8);});
        for(int i=0;i<3;i++)
        {
            var source=optic.SurfaceGroup.Items[i+1];var face=group.Interfaces[i];var frame=face.Frame!;
            Assert.InRange((scene.ToWorldPoint(scene.Objects[0].Id,frame.Origin)-source.CoordinateSystem.Origin).Length,0,1e-12);
            Assert.Equal(source.Geometry.Kind,face.GeometryKind);Assert.Equal(source.SemiDiameter,face.SemiDiameterMillimeters);Assert.Equal(source.MechanicalSemiDiameter,face.MechanicalSemiDiameterMillimeters);
        }
        Evidence($"path-{immersed}-{odd}-{posed}",new{AnalyticalNotMeasured=true,Immersed=immersed,Odd=odd,Posed=posed,group.GeometryProtocol,group.GeometryCertificateMargin,trace.EnergyBalance,Errors=errors});
    }

    [Theory] [InlineData(0)] [InlineData(1)]
    public void InternalSourcesAreLocatedInTheirActualTiltedGlassRegion(int region)
    {
        var optic=Prescription();var scene=OpticalAssemblyTests.Scene(optic);var group=(OpticalAssemblyParameters)scene.Objects[0].Parameters;
        var p=(group.Interfaces[region].Frame!.Origin+group.Interfaces[region+1].Frame!.Origin)/2;
        var trace=OpticalAssemblyTests.Trace(optic,scene,new(p,new(0,0,1),550));OpticalAssemblyTests.Balanced(trace);
        var first=Assert.Single(trace.Branches).Segments[0];Assert.Equal(region+2,first.FaceNumber);
        Assert.Equal(group.Interfaces[region].After.RefractiveIndex(550),first.CumulativeOpticalPathLength/first.SegmentLength,10);
    }

    [Theory] [InlineData(0,.25)] [InlineData(1.5707963267948966,.75)] [InlineData(3.141592653589793,.25)] [InlineData(4.71238898038469,.75)]
    public void ExactTiltedRimsMatchIndependentParametricPointsAndNormals(double angle,double fraction)
    {
        var ca=Vector3D.Zero;var cb=new Vector3D(.3,-.2,4);var frame=new CoordinateSystem(cb,3,-5,0);
        var ua=new Vector3D(4,0,0);var va=new Vector3D(0,4,0);var ub=frame.ToGlobalDirection(new(4,0,0));var vb=frame.ToGlobalDirection(new(0,4,0));
        var rim=new RuledCircularRim(ca,ua,va,cb,ub,vb);var c=Math.Cos(angle);var s=Math.Sin(angle);
        var a=ca+ua*c+va*s;var b=cb+ub*c+vb*s;var point=a*(1-fraction)+b*fraction;
        var tangent=(-ua*s+va*c)*(1-fraction)+(-ub*s+vb*c)*fraction;
        var normal=OpticalAssemblySurfaceFrame.Cross(tangent,b-a);normal/=normal.Length;
        var hit=rim.Intersect(point+normal*2,-normal);Assert.NotNull(hit);Assert.InRange((hit.Value.Point-point).Length,0,2e-10);
        Assert.Equal(2,hit.Value.Distance,10);Assert.InRange((hit.Value.Normal-normal).Length,0,2e-10);
        var reverse=rim.Intersect(point-normal*.1,normal);Assert.NotNull(reverse);Assert.Equal(.1,reverse.Value.Distance,10);
    }

    [Fact]
    public void DecenteredRolledApertureUsesItsCompleteSurfaceFrame()
    {
        var optic=Prescription();var original=optic.SurfaceGroup.Items[2];original.PhysicalAperture=new RectangularAperture(.2,2);
        original.CoordinateSystem=original.CoordinateSystem with{RotationZDegrees=67};
        var scene=OpticalAssemblyTests.Scene(optic);var group=(OpticalAssemblyParameters)scene.Objects[0].Parameters;var face=group.Interfaces[1];
        NonSequentialDocumentTraceResult At(Vector3D local)
        {
            var target=face.Frame!.ToAssemblyPoint(local with{Z=face.Geometry.Sag(local.X,local.Y)});var direction=face.Frame.AxisZ;
            return OpticalAssemblyTests.Trace(optic,scene,new(target-direction*.1,direction,550));
        }
        var blocked=At(new(.5,0,0));OpticalAssemblyTests.Balanced(blocked);Assert.Equal(0,blocked.Detectors[0].TotalPowerWatts);Assert.Equal(2,Assert.Single(blocked.Branches).Segments[^1].FaceNumber);
        var passed=At(new(0,.5,0));OpticalAssemblyTests.Balanced(passed);Assert.True(passed.Detectors[0].TotalPowerWatts>0);
    }

    [Fact]
    public void RigidSceneReferenceTransformsPreserveFullBranchPowerDirectionsAndOpticalPath()
    {
        var optic=Prescription();optic.SurfaceGroup.Items[2].CoatingModel=new CoherentMultilayerCoating([new(new ConstantIndexMaterial("film L",1.3),70),new(new ConstantIndexMaterial("film H",2.2),45)]);
        var scene=OpticalAssemblyTests.Scene(optic,true);var before=OpticalAssemblyTests.Trace(optic,scene,Source(),NonSequentialSplittingMode.FullFresnel);OpticalAssemblyTests.Balanced(before);
        var frame=new CoordinateSystem(new(7,-11,4),23,-14,51);
        var root=NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay) with{Enabled=false,LocalCoordinateSystem=frame};scene.Insert(scene.Objects.Count,root);
        foreach(var item in scene.Objects.Where(o=>o.Id!=root.Id).ToArray())scene.Replace(item.Id,item with{ReferenceObjectId=root.Id});
        var source=Source();var after=OpticalAssemblyTests.Trace(optic,scene,source with{Origin=frame.ToGlobalPoint(source.Origin),Direction=frame.ToGlobalDirection(source.Direction)},NonSequentialSplittingMode.FullFresnel);OpticalAssemblyTests.Balanced(after);
        Assert.Equal(before.TotalBranchCount,after.TotalBranchCount);
        foreach(var detector in before.Detectors)Assert.InRange(Math.Abs(detector.TotalPowerWatts-after.Detectors.Single(d=>d.DetectorId==detector.DetectorId).TotalPowerWatts),0,1e-10);
        foreach(var branch in before.Branches)
        {
            var other=after.Branches.Single(b=>b.Id==branch.Id);Assert.Equal(branch.Segments.Count,other.Segments.Count);
            for(int i=0;i<branch.Segments.Count;i++){Assert.InRange((frame.ToGlobalPoint(branch.Segments[i].End)-other.Segments[i].End).Length,0,2e-8);Assert.InRange(Math.Abs(branch.Segments[i].CumulativeOpticalPathLength-other.Segments[i].CumulativeOpticalPathLength),0,2e-8);}
        }
    }

    [Fact]
    public void AnInteriorAsphericCrossingIsRejectedEvenWhenVerticesAndEdgesAreSeparated()
    {
        var optic=OpticalAssemblyTests.Prescription(glasses:1);var front=optic.SurfaceGroup.Items[1];front.SemiDiameter=3;front.MechanicalSemiDiameter=4;
        front.Geometry=new EvenAsphereGeometry(0,0,[.8,-.05]);front.Thickness=2;
        var back=optic.SurfaceGroup.Items[2];back.SemiDiameter=3;back.MechanicalSemiDiameter=4;optic.SurfaceGroup.Renumber();
        Assert.Equal(0,front.Geometry.Sag(4,0),12);Assert.True(front.Geometry.Sag(Math.Sqrt(8),0)>2);
        Assert.Throws<NotSupportedException>(()=>SequentialOpticalAssemblyConverter.Convert(optic));
    }

    [Theory] [InlineData(-3,.2,1)] [InlineData(-5,.2,3)] [InlineData(-1,.2,3)] [InlineData(-3,0,3)]
    public void BoundedGrazingAspheresReturnTheFirstPhysicalRootIncludingTangency(double x,double z,double expectedDistance)
    {
        var domain=new RotationalSurfaceDomain(new EvenAsphereGeometry(0,0,[.05]),4);
        var hit=domain.Intersect(new(x,0,z),new(1,0,0));Assert.True(hit.IsHit);
        Assert.Equal(expectedDistance,hit.Distance,10);Assert.InRange(Math.Abs(hit.Point.Z-.05*hit.Point.X*hit.Point.X),0,1e-14);
    }

    [Fact]
    public void TiltedParallelAsphericPlaneDefinitionsMatchAnalyticalPowerAndStochasticStatistics()
    {
        var optic=OpticalAssemblyTests.Prescription(glasses:1);var front=optic.SurfaceGroup.Items[1];var back=optic.SurfaceGroup.Items[2];
        foreach(var face in new[]{front,back}){face.SemiDiameter=3;face.MechanicalSemiDiameter=4;face.PhysicalAperture=new CircularAperture(3);face.Geometry=new EvenAsphereGeometry(double.PositiveInfinity,0,[0]);}
        front.CoordinateSystem=new(Vector3D.Zero,0,12);var normal=front.CoordinateSystem.ToGlobalDirection(new(0,0,1));back.CoordinateSystem=new(normal*4,0,12);
        var scene=OpticalAssemblyTests.Scene(optic,true);var r=FresnelPower.Evaluate(1,1.5,Math.Cos(12*Math.PI/180));
        var reflectance=(r.ReflectionS.Magnitude*r.ReflectionS.Magnitude+r.ReflectionP.Magnitude*r.ReflectionP.Magnitude)/2;
        var infinitePlate=(1-reflectance)/(1+reflectance);
        var transmittedSine=Math.Sin(12*Math.PI/180)/1.5;var walkoff=4*transmittedSine/Math.Sqrt(1-transmittedSine*transmittedSine);
        double expected=0,remaining=1-reflectance;
        // Each crossing advances along the face by thickness*tan(theta_t).
        // The same finite 3 mm apertures as the traced plate clip high orders.
        for(int crossing=1;crossing*walkoff<=3;crossing++)
        {
            if(crossing%2==1)expected+=remaining*(1-reflectance);
            remaining*=reflectance;
        }
        var full=OpticalAssemblyTests.Trace(optic,scene,mode:NonSequentialSplittingMode.FullFresnel);OpticalAssemblyTests.Balanced(full);
        Assert.Equal(expected,full.Detectors[0].TotalPowerWatts,12);
        const int count=8192;scene.Insert(scene.Objects.Count,NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourcePoint) with{LocalCoordinateSystem=new(new(0,0,-2)),Parameters=new SourcePointParameters(ConeHalfAngleDegrees:0,AnalysisRayCount:count)});
        var options=new NonSequentialDocumentTraceRequest(SplittingMode:NonSequentialSplittingMode.SimpleStochastic,RandomSeed:941,OutputMode:NonSequentialTraceOutputMode.SummaryOnly);
        var tracer=new NonSequentialDocumentTracer();var a=tracer.Trace(scene,optic.Materials,options);var b=tracer.Trace(scene,optic.Materials,options);OpticalAssemblyTests.Balanced(a);
        Assert.Equal(a.EnergyBalance,b.EnergyBalance);Assert.Equal(a.Detectors[0].PowerByWavelength[1],b.Detectors[0].PowerByWavelength[1]);
        var sigma=Math.Sqrt(expected*(1-expected)/count);Assert.InRange(Math.Abs(a.Detectors[0].TotalPowerWatts-expected),0,6*sigma);
        Evidence("tilted-plane-statistics",new{AnalyticalNotMeasured=true,Rays=count,ExpectedFiniteAperturePower=expected,InfinitePlatePower=infinitePlate,WalkoffPerCrossingMillimeters=walkoff,FullTracePower=full.Detectors[0].TotalPowerWatts,StochasticPower=a.Detectors[0].TotalPowerWatts,Sigma=sigma,ExactSeedReplay=true,a.EnergyBalance});
    }

    [Theory] [InlineData("air")] [InlineData("immersion")]
    public void ExistingV20AssemblyScenesKeepTheirFrozenHashAndCanBeTraced(string name)
    {
        static string Repository([System.Runtime.CompilerServices.CallerFilePath]string source="")
        {
            for(var directory=new DirectoryInfo(Path.GetDirectoryName(source)!);directory is not null;directory=directory.Parent)
                if(EvidenceFiles.Exists(Path.Combine(directory.FullName,"OpticalSystemDesign.slnx")))return directory.FullName;
            throw new DirectoryNotFoundException("Repository for retained assembly regression input.");
        }
        var root=Path.Combine(Repository(),"validation/rechecks/2026-11-01/cemented-immersion/final-controls");
        var project=StarOptProjectStore.Deserialize(EvidenceFiles.ReadAllBytes(Path.Combine(root,name+".staropt")));var scene=project.NonSequentialDocument!;
        using var stream=EvidenceFiles.OpenRead(Path.Combine(root,name+".starrdb"));using var database=new NonSequentialRayDatabaseReader(stream);
        Assert.False(((OpticalAssemblyParameters)scene.Objects[0].Parameters).HasExtendedSurfaces);
        Assert.Equal(database.Header.SceneHash,NonSequentialSceneHasher.Compute(scene,project.Configurations[0].Materials));
        var trace=new NonSequentialDocumentTracer().Trace(scene,project.Configurations[0].Materials,new(SplittingMode:NonSequentialSplittingMode.FullFresnel));OpticalAssemblyTests.Balanced(trace);
        Assert.True(trace.Detectors[0].TotalPowerWatts>0);
    }

    [Fact]
    public void InvalidFramesCuspPayloadsAndMismatchedGeometryCannotBecomeTraceableObjects()
    {
        Assert.Throws<InvalidDataException>(()=>new OpticalAssemblySurfaceFrame(Vector3D.Zero,new(1,0,0),new(0,-1,0),new(0,0,1)));
        Assert.Throws<InvalidDataException>(()=>new OpticalAssemblySurfaceGeometry("odd_asphere",80,0,[.1]));
        Assert.Throws<InvalidDataException>(()=>new OpticalAssemblySurfaceGeometry("even_asphere",80,0,new double[33]));
        var optic=Prescription();var group=(OpticalAssemblyParameters)OpticalAssemblyTests.Scene(optic).Objects[0].Parameters;var face=group.Interfaces[0];
        Assert.Throws<InvalidDataException>(()=>new NonSequentialOpticalInterface(face.SourceSurfaceNumber,face.VertexZMillimeters,999,face.Conic,face.SemiDiameterMillimeters,face.MechanicalSemiDiameterMillimeters,
            face.MaterialBefore,face.MaterialAfter,face.Coating,face.Aperture,face.IsStop,face.RollDegrees,face.SourceLabel,face.SurfaceGeometry,face.Frame));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ExtendedDisabledPayloadRequiresV21AndRetainsImmutableCoefficientsAndFrames(bool planarBases)
    {
        var optic=Prescription(true,true);var scene=OpticalAssemblyTests.Scene(optic);var obj=scene.Objects[0];scene.Replace(obj.Id,obj with{Enabled=false});
        if(planarBases)
        {
            foreach(var f in optic.SurfaceGroup.Items.Skip(1).Take(3))f.Geometry=new EvenAsphereGeometry(double.PositiveInfinity,0,[1e-5]);
            optic.SurfaceGroup.Items[^1].Radius=double.PositiveInfinity;
            scene=OpticalAssemblyTests.Scene(optic);obj=scene.Objects[0];scene.Replace(obj.Id,obj with{Enabled=false});
        }
        var group=(OpticalAssemblyParameters)obj.Parameters;Assert.Throws<NotSupportedException>(()=>((IList<double>)group.Interfaces[0].SurfaceGeometry!.Coefficients)[1]=999);
        using var files=new CameraSensorFileTests.TemporaryFiles();var file=files.Path("posed.staropt");await StarOptProjectStore.SaveAsync(new([optic],0,NonSequentialDocument:scene),file);
        var bytes=EvidenceFiles.ReadAllBytes(file);var restored=StarOptProjectStore.Deserialize(bytes).NonSequentialDocument!;
        Assert.Equal(NonSequentialSceneHasher.Compute(scene,optic.Materials),NonSequentialSceneHasher.Compute(restored,optic.Materials));
        using var raw=new MemoryStream(bytes);raw.Position=52;using var decompressor=new BrotliStream(raw,CompressionMode.Decompress);using var json=JsonDocument.Parse(decompressor);
        Assert.Equal(21,json.RootElement.GetProperty("FormatVersion").GetInt32());
        var changed=System.Text.Json.Nodes.JsonNode.Parse(json.RootElement.GetRawText())!;changed["FormatVersion"]=20;var payload=System.Text.Encoding.UTF8.GetBytes(changed.ToJsonString());
        using var packed=new MemoryStream();using(var compressor=new BrotliStream(packed,CompressionLevel.Optimal,true))compressor.Write(payload);
        var header=bytes[..52];System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12),payload.Length);System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16),(int)packed.Length);System.Security.Cryptography.SHA256.HashData(payload).CopyTo(header,20);
        Assert.Contains("version 21",Assert.Throws<InvalidDataException>(()=>StarOptProjectStore.Deserialize(header.Concat(packed.ToArray()).ToArray())).Message);
    }
}
