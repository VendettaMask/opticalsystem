using System.Collections;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class ReferenceFocusProbe
{
    static readonly Type Evaluator=typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.MtfMethodEvaluator")!;
    internal static void Focus(string audit,string output)
    {
        if(Directory.Exists(output))throw new IOException("Fresh output required");Directory.CreateDirectory(output);
        var result=new List<object>();
        foreach(var row in JsonFiles.Read<JsonElement>(Path.Combine(audit,"fixed-grid-controls/items.json")).EnumerateArray().Where(r=>r.GetProperty("key").GetString()=="Huygens Through Focus MTF"))
        {
            var id=row.GetProperty("id").GetString()!;
            var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(row.GetProperty("snapshot").GetString()!));
            var request=row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var wave=optic.Wavelengths[request.Wavelength-1];
            var native=JsonFiles.Read<JsonElement>(Path.Combine(audit,"fixed-grid-controls",id,"native/data.json")).GetProperty("dataSeries")[0];
            var pitch=double.Parse(request.WorkbenchSettings["ImageDeltaMicrometers"],System.Globalization.CultureInfo.InvariantCulture);
            var settings=new MtfComputationSettings(PupilSampling:64,ImageSize:128,PixelPitchMillimeters:pitch,ZemaxCompatible:true);
            var points=new List<object>();
            foreach(var index in new[]{0,25,50,75,100})
            {
                var d=native.GetProperty("x")[index].GetDouble();var frequency=double.Parse(request.WorkbenchSettings["SpatialFrequency"],System.Globalization.CultureInfo.InvariantCulture);
                var psf=(PsfResult)Evaluator.GetMethod("ComputeHuygensPolychromaticPsf",BindingFlags.Static|BindingFlags.NonPublic)!
                    .Invoke(null,[optic,(0d,0d),new[]{wave},settings,d])!;
                var mtf=DiffractionEngine.ComputePsfMtf(psf);
                (double,double) Sample(double f,bool endpoint)=>(ValueTuple<double,double>)Evaluator.GetMethod("SampleHuygensMtf",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[mtf,128,f,endpoint])!;
                var e=Sample(frequency,true);var c=Sample(frequency*128/127,true);var l=Sample(frequency,false);
                var direct=DiffractionEngine.ComputePsfMtfAtFrequencies(psf,[frequency]);
                points.Add(new{d,frequency,native=native.GetProperty("y")[index],endpointCubic=new[]{e.Item1,e.Item2},periodCubic=new[]{c.Item1,c.Item2},periodLinear=new[]{l.Item1,l.Item2},direct=new[]{direct.Tangential[0],direct.Sagittal[0]}});
                Console.WriteLine($"Focus {id} {d}");
            }
            result.Add(new{id,lens=row.GetProperty("lens").GetString(),points});
            JsonFiles.Write(Path.Combine(output,"summary.json"),result);
        }
    }

    internal static void Reference(string audit,string output)
    {
        if(Directory.Exists(output))throw new IOException("Fresh output required");Directory.CreateDirectory(output);
        var rows=new List<object>();
        var support=typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.EnergyCurveSupport")!;
        var sampleType=typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.EnergySample")!;
        var sampling=typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.EnergyPlotSampling")!;
        var policy=ComparisonConfiguration.Load(Path.Combine(audit,"baseline-v4/comparison-settings.json"));
        foreach(var item in JsonFiles.Read<JsonElement>(Path.Combine(audit,"expanded/items.json")).EnumerateArray().Where(i=>i.GetProperty("key").GetString()=="Encircled Energy"))
        {
            var id=item.GetProperty("id").GetString()!;var optic=Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
            var r=item.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var scale=PhysicalNormalization.UnitScale(r.NativeAfocalAngleUnit!,"Milliradian");
            var radius=((JsonElement)r.ZemaxSettings["RadiusMaximum"]).GetDouble()*scale;
            var spot=new SpotDiagramAnalysis(optic,new SpotDiagramSettings(RayDensity:33,Pattern:"uniform",WavelengthNumber:r.Wavelength,Reference:"absolute")).GenerateData();
            var series=new Dictionary<string,List<AnalysisSeries>>{{"centroid",[]},{"nativeReferenceCoordinate",[]},{"chief",[]},{"spatialCentroid",[]},{"spatialCentroidOffset",[]}};
            var centers=new List<object>();var max=optic.Fields.Max(f=>Math.Sqrt(f.X*f.X+f.Y*f.Y));
            for(var i=0;i<optic.Fields.Count;i++)
            {
                var points=spot.PlotPanes![i].Series.Single().Points;var f=optic.Fields[i];
                var chief=optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(max==0?0:f.X/max,max==0?0:f.Y/max,0,0,optic.Wavelengths[r.Wavelength-1].Micrometers,optic.RayAimingEnabled))[0]!;
                var position=optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(chief.Position);
                var direction=optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalDirection(chief.Direction);
                var mean=(X:points.Average(p=>p.X),Y:points.Average(p=>p.Y));
                var pupils=(IReadOnlyList<OpticalSystemDesign.Core.Raytrace.PupilSample>)typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.SpotAnalysisEngine")!.GetMethod("CreatePupilSamples",BindingFlags.Static|BindingFlags.Public)!.Invoke(null,[33,"uniform",6])!;
                var bundle=optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(max==0?0:f.X/max,max==0?0:f.Y/max,optic.Wavelengths[r.Wavelength-1].Micrometers,pupils,optic.RayAimingEnabled);
                var intercepts=optic.SequentialRayTracer.TraceFinalSamples(bundle).Where(s=>s is {Vignetted:false,Intensity:>0}).Select(s=>optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(s!.Position)).ToArray();
                var spatial=(X:intercepts.Average(p=>p.X),Y:intercepts.Average(p=>p.Y));
                var samples=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(sampleType))!;
                foreach(var p in points)samples.Add(Activator.CreateInstance(sampleType,[p.X,p.Y,1d]));
                centers.Add(new{field=i+1,mean,chiefMillimeters=new[]{position.X,position.Y},relativeAngularMean=new[]{(mean.X-Math.Atan2(direction.X,direction.Z)*1000)/scale,(mean.Y-Math.Atan2(direction.Y,direction.Z)*1000)/scale}});
                foreach(var (variant,curves) in series)
                {
                    var chiefAngle=(X:Math.Atan2(direction.X,direction.Z)*1000,Y:Math.Atan2(direction.Y,direction.Z)*1000);
                    var center=variant switch {
                        "centroid" => mean,
                        "nativeReferenceCoordinate" => (mean.X+position.X*scale,mean.Y+position.Y*scale),
                        "chief" => chiefAngle,
                        "spatialCentroid" => (chiefAngle.X+spatial.X*scale,chiefAngle.Y+spatial.Y*scale),
                        _ => (chiefAngle.X+(spatial.X-position.X)*scale,chiefAngle.Y+(spatial.Y-position.Y)*scale)
                    };
                    var data=(AnalysisData)support.GetMethod("CreateCurve",BindingFlags.Static|BindingFlags.Public)!.Invoke(null,["Encircled Energy",samples,center,"encircled",radius,100,new Dictionary<string,object>(),false])!;
                    var curve=(IReadOnlyList<AnalysisPoint>)sampling.GetMethod("Geometric",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,[data.Series!.Points])!;
                    curves.Add(new("Radius (mrad)","Fraction of Energy",curve,Name:"Field "+(i+1),XQuantity:AnalysisAxisQuantity.Radius,XUnit:AnalysisAxisUnit.Milliradian,YQuantity:AnalysisAxisQuantity.EnergyFraction,YUnit:AnalysisAxisUnit.Dimensionless));
                }
            }
            foreach(var (variant,curves) in series)
            {
                var data=new AnalysisData("Encircled Energy",new Dictionary<string,object>(),curves[0],curves);
                var folder=Path.Combine(output,id+"-"+variant);Directory.CreateDirectory(folder);JsonFiles.Write(Path.Combine(folder,"data.json"),data);
                var run=new AnalysisRun{Key="Encircled Energy",Directory=Path.GetFileName(folder),Request=r};
                ComparisonRunner.Compare(run,AnalysisComparisonRegistry.Get("Encircled Energy"),policy.Analyses["Encircled Energy"],ExtendedResultNormalizer.Workbench(data,r),ExtendedResultNormalizer.Zemax(Path.Combine(audit,"expanded",id,"native/data.json"),r),folder);
                rows.Add(new{id,variant,conclusion=run.Conclusion.ToString(),run.Metrics,centers});
            }
        }
        JsonFiles.Write(Path.Combine(output,"summary.json"),rows);
    }
}
