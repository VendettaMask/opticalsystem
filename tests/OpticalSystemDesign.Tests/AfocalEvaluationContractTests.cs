using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Capabilities;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class AfocalEvaluationContractTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void ReferenceOpdSurvivesImportSnapshotAndExport(int mode)
    {
        var optic=OpticalFormatCatalog.Import($"MODE SEQ\nENPD 2\nROPD {mode}\nSURF 0\n DISZ INFINITY\nSURF 1\n STOP\n DISZ 5\nSURF 2", ".zmx");
        Assert.Equal((ReferenceOpdMode)mode,optic.ReferenceOpd);
        var restored=Optic.FromSnapshot(optic.ToSnapshot());
        Assert.Equal(optic.ReferenceOpd,restored.ReferenceOpd);
        Assert.Equal(optic.ReferenceOpd,OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(restored,".zmx"),".zmx").ReferenceOpd);
        var json=JsonSerializer.Serialize(optic.ToSnapshot(),new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
            NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals });
        Assert.Equal(mode!=2,json.Contains("referenceOpd",StringComparison.Ordinal));
    }

    [Fact]
    public void MixedConfigurationsRetainTheirOwnImageSpaceMode()
    {
        var imported= new ZemaxZmxImporter().ImportConfigurationSet("MODE SEQ\nENPD 2\nMNUM 2 1\nAFOC 0 1 0\nAFOC 0 2 1\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDISZ 5\nSURF 2");
        Assert.False(imported.Configurations[0].ImageSpaceAfocal);
        Assert.True(imported.Configurations[1].ImageSpaceAfocal);
        Assert.True(Optic.FromSnapshot(imported.Configurations[1].ToSnapshot()).ImageSpaceAfocal);
    }

    [Fact]
    public void ReverseCoordinateBreakUndoesCompoundRotationsAndDecenter()
    {
        var optic=OpticalFormatCatalog.Import("""
            MODE SEQ
            ENPD 2
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
            SURF 2
              TYPE COORDBRK
              PARM 1 1
              PARM 2 2
              PARM 3 10
              PARM 4 -20
              PARM 5 30
            SURF 3
              TYPE COORDBRK
              PARM 1 -1
              PARM 2 -2
              PARM 3 -10
              PARM 4 20
              PARM 5 -30
              PARM 6 1
            SURF 4
            """,".zmx");
        var frame=optic.SurfaceGroup.Items[^1].CoordinateSystem;
        Near(Vector3D.Zero,frame.Origin,1e-12);
        Near(new(0,0,1),frame.ToGlobalDirection(new(0,0,1)),1e-12);
        Near(new(1,0,0),frame.ToGlobalDirection(new(1,0,0)),1e-12);
    }

    [Theory]
    [InlineData(ReferenceOpdMode.Absolute)]
    [InlineData(ReferenceOpdMode.ExitPupil)]
    [InlineData(ReferenceOpdMode.Absolute2)]
    public void AimedAfocalPhaseUsesTheActualLaunchPlaneAndPrimaryReference(ReferenceOpdMode mode)
    {
        var optic=OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(Root(),"src/OpticalSystemDesign.App/Assets/SequentialExamples/Afocal/Afocal Riflescope.ZMX")),".zmx");
        optic.ReferenceOpd=mode;
        var wave=optic.Wavelengths[0]; var primary=optic.Wavelengths.Single(w=>w.IsPrimary);
        var pupils=new[]{(0d,0d),(.5,0d),(0d,.5),(0d,-.5)};
        var result=WavefrontEngine.GenerateChiefRaySamples(optic,(0,1),wave,pupils,aimAtStop:true);
        var chiefBundle=optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,1,0,0,wave.Micrometers,true);
        var chief=optic.SequentialRayTracer.TraceFinalSamples(chiefBundle).Single()!;
        var reference=optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,1,0,0,primary.Micrometers,true)).Single()!;
        var frame=optic.SurfaceGroup.Items[^1].CoordinateSystem;
        var normal=mode==ReferenceOpdMode.Absolute2?frame.ToGlobalDirection(new(0,0,1)):reference.Direction;
        var anchor=mode==ReferenceOpdMode.ExitPupil?reference.Position+reference.Direction
            *((optic.Paraxial.EstimateExitPupilLocation(primary.Micrometers)-frame.ToLocalPoint(reference.Position).Z)/frame.ToLocalDirection(reference.Direction).Z)
            :mode==ReferenceOpdMode.Absolute2?frame.Origin:reference.Position;
        double PlanePath(RayTraceSample ray) => Dot(ray.Position-anchor,normal)/Dot(ray.Direction,normal);
        var chiefPath=chief.CumulativeOpticalPathLength-PlanePath(chief);
        for(var i=0;i<pupils.Length;i++)
        {
            var bundle=optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,1,pupils[i].Item1,pupils[i].Item2,wave.Micrometers,true);
            var ray=optic.SequentialRayTracer.TraceFinalSamples(bundle).Single()!;
            var launchPhase=Dot(bundle.Rays[0].Origin-chiefBundle.Rays[0].Origin,chiefBundle.Rays[0].Direction);
            var expected=(chiefPath-(ray.CumulativeOpticalPathLength-PlanePath(ray)+launchPhase))/(wave.Micrometers*.001);
            Assert.InRange(Math.Abs(result.Samples[i].OpdWaves-expected),0,1e-7);
        }
        Assert.Equal(primary.Nanometers,result.SourceReferenceWavelengthNanometers);
    }

    [Fact]
    public void ClosedPolygonTransportPreservesPathAndSnapshotAndRejectsMissingGlass()
    {
        Vector3D[] front=[new(-1,-1,0),new(1,-1,0),new(1,1,0),new(-1,1,0)];
        var back=front.Select(p=>p+new Vector3D(0,0,2)).ToArray();
        var faces=new List<PolygonGroupGeometry.Face>{new(front,false,false),new(back,false,false)};
        for(var i=0;i<4;i++) faces.Add(new([front[i],front[(i+1)%4],back[(i+1)%4],back[i]],false,false));
        var group=new PolygonGroupGeometry(faces,new ConstantIndexMaterial("glass",1.5));
        Assert.Equal(12,group.Faces.Count);
        Assert.Throws<NotSupportedException>(()=>((IList<Vector3D>)group.Faces[0].Vertices)[0]=Vector3D.Zero);
        var frame=new CoordinateSystem(new(4,5,6),20,10,30);
        var port=new OpticalSurface{Geometry=group,CoordinateSystem=frame};
        // The exit plane is z=0 in the port frame, so translate the volume upstream.
        group=new PolygonGroupGeometry(group.Faces.Select(f=>f with{Vertices=f.Vertices.Select(p=>p-new Vector3D(0,0,3)).ToArray()}).ToArray(),group.VolumeMaterial);
        port.Geometry=group;
        var ray=new RayState(frame.ToGlobalPoint(new(.2,.1,-4)),frame.ToGlobalDirection(new(0,0,1)),550,1,0,null,true);
        var result=port.TraceRayState(ray,new AirMaterial(),new AirMaterial(),0,0);
        Assert.False(result.StopTracing);
        Near(frame.ToGlobalPoint(new(.2,.1,0)),result.Ray.Origin,1e-12);
        Near(frame.ToGlobalDirection(new(0,0,1)),result.Sample.IncidentDirection!.Value,1e-12);
        Assert.Equal(4,result.CumulativePathLength,10);
        Assert.Equal(5,result.CumulativeOpticalPathLength,10);
        var restored=Assert.IsType<PolygonGroupGeometry>(ComponentSnapshotFactory.ToGeometry(ComponentSnapshotFactory.FromGeometry(group),0,0));
        Assert.Equal(group.Faces.Count,restored.Faces.Count);
        Assert.Equal(1.5,restored.VolumeMaterial.RefractiveIndex(550),12);
        Assert.Throws<OpticCapabilityException>(()=>OpticCapabilityPreflight.EnsureSurfaceSupported(port,OpticCapabilityOperation.Visualization));
        port.Geometry=new PolygonGroupGeometry(group.Faces,new UnresolvedMaterial("missing"));
        Assert.Throws<OpticCapabilityException>(()=>OpticCapabilityPreflight.EnsureSurfaceSupported(port,OpticCapabilityOperation.RayTrace));
    }

    [Fact]
    public async Task BinocularConfigurationsResolveDistinctAssetsAndSnapshotBothPhysicalPaths()
    {
        var importer=new ZemaxZmxImporter();
        var path=Path.Combine(Root(),"src/OpticalSystemDesign.App/Assets/SequentialExamples/Afocal/Binocular_System.ZMX");
        var imported=await importer.ImportConfigurationSetFileAsync(path);
        Assert.Equal(2,imported.Configurations.Count);
        var groups=imported.Configurations.Select(o=>Assert.IsType<PolygonGroupGeometry>(o.SurfaceGroup.Items[6].Geometry)).ToArray();
        Assert.NotEqual(groups[0].Faces[0].Vertices[0],groups[1].Faces[0].Vertices[0]);
        foreach(var original in imported.Configurations)
        foreach(var optic in new[]{original,Optic.FromSnapshot(original.ToSnapshot())})
        {
            Assert.Empty(OpticCapabilityPreflight.Inspect(optic));
            var rays=optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0,1,.2,.1,optic.Wavelengths[1].Micrometers,false);
            var sample=optic.SequentialRayTracer.TraceFinalSamples(rays).Single();
            Assert.NotNull(sample);
            Assert.False(sample.Vignetted);
            Assert.True(sample.Intensity>0);
        }
    }

    [Theory]
    [InlineData("NSOD 2 1.000000000000E+00", "NSOD 2 0.000000000000E+00")]
    [InlineData("NSCS 1 0 _NONE_", "NSCS 1 0 TEST_COATING")]
    [InlineData("UNIT MM", "UNIT CM")]
    public async Task UnsupportedPolygonOptionsCannotSilentlyProduceResults(string old,string replacement)
    {
        var directory=Path.Combine(Path.GetTempPath(),"afocal-polygon-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var assets=Path.Combine(Root(),"src/OpticalSystemDesign.App/Assets/SequentialExamples");
            foreach(var name in new[]{"PORRO_PRISM-LH.POB","PORRO_PRISM-RH.POB"})
                File.Copy(Path.Combine(assets,"Objects/Polygon Objects",name),Path.Combine(directory,name));
            var source=File.ReadAllText(Path.Combine(assets,"Afocal/Binocular_System.ZMX"));
            Assert.Contains(old,source,StringComparison.Ordinal);
            var path=Path.Combine(directory,"source.zmx");File.WriteAllText(path,source.Replace(old,replacement,StringComparison.Ordinal));
            await Assert.ThrowsAsync<NotSupportedException>(()=>new ZemaxZmxImporter().ImportConfigurationSetFileAsync(path));
        }
        finally{Directory.Delete(directory,true);}
    }

    [Theory]
    [InlineData(ParabasalRayPair.Symmetric)]
    [InlineData(ParabasalRayPair.ChiefAndPositive)]
    public void CollimatedOutputHasZeroVergenceInDiopters(ParabasalRayPair pair)
    {
        var optic=OpticalFormatCatalog.Import("MODE SEQ\nENPD 2\nFTYP 0 0 1 1 0 0 1\nXFLN 0\nYFLN 2\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDISZ 10\nSURF 2",".zmx");
        var (s,t)=FieldCurvatureMetrics.AfocalVergences(optic,0,1,.55,.001,pair);
        Assert.Equal(0,s,12);Assert.Equal(0,t,12);
        var data=new FieldCurvatureAnalysis(optic,3,parabasalRayPair:pair).GenerateData();
        Assert.All(data.PlotSeries,series=>Assert.Equal(AnalysisAxisUnit.Diopter,series.XUnit));
    }

    private static double Dot(Vector3D a,Vector3D b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    [Fact]
    public void ZeroPowerInOneAxisDoesNotEraseTheOtherAxisVergence()
    {
        var (s,t)=FieldCurvatureMetrics.AfocalVergencesFromResponse(2,0,0,3,0,0,0,-.03,1);
        Assert.Equal(0,s,12);Assert.Equal(10,t,12);
        // Rotate the transverse pupil parameter basis; physical powers stay.
        var rotated=FieldCurvatureMetrics.AfocalVergencesFromResponse(2,3,-2,3,0,-.03,0,-.03,1);
        Assert.Equal(s,rotated.Sagittal,12);Assert.Equal(t,rotated.Tangential,12);
        Assert.Throws<InvalidOperationException>(()=>FieldCurvatureMetrics.AfocalVergencesFromResponse(1,1,1,1,0,0,0,0,1));
    }
    private static void Near(Vector3D a,Vector3D b,double tolerance)=>Assert.InRange((a-b).Length,0,tolerance);
    private static string Root()
    {
        var d=new DirectoryInfo(AppContext.BaseDirectory);
        while(d is not null&&!File.Exists(Path.Combine(d.FullName,"OpticalSystemDesign.slnx")))d=d.Parent;
        return d?.FullName??throw new DirectoryNotFoundException();
    }
}
