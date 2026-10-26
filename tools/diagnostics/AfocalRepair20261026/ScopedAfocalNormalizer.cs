using System.Text.Json;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

// Validation-only unit decoding. It never changes product data, ray masks,
// reference arrays, grid origins, optical prescriptions or comparison gates.
internal static class ScopedAfocalNormalizer
{
    internal static (NumericResult Actual,NumericResult Expected,AnalysisConfiguration Settings) Read(
        string workbench,string native,AnalysisComparisonEntry entry,CanonicalAnalysisRequest r,AnalysisConfiguration settings,Tolerances angularRadius)
    {
        AfocalAnalysisContracts.ValidateNativeModel(native,r);
        using var doc=JsonDocument.Parse(File.ReadAllText(native));NativeReferenceDiagnostics.Validate(doc.RootElement);
        var data=JsonFiles.Read<AnalysisData>(workbench);
        if(data.Outcome!=AnalysisOutcome.Success)throw new InvalidDataException(data.OutcomeReason);
        if(r.CanonicalAnalysisKey=="First Order")
        {
            if(data.Values.TryGetValue("CardinalParametersApplicable",out var applicable)&&applicable is JsonElement flag&&!flag.GetBoolean())
                throw new InvalidDataException("Not applicable: a mixed-mode NSC group has no general paraxial equivalent; focal EFL/F-number are explicitly withheld, not interpreted as AF accuracy failures.");
            if(data.Values.TryGetValue("EffectiveFocalLengthStatus",out var status)&&status is JsonElement state&&state.GetString()=="Infinite")
            {
                var model=JsonFiles.Read<JsonElement>(Path.Combine(Path.GetDirectoryName(native)!,"model.json"));
                var planar=model.GetProperty("surfaces").EnumerateArray().All(s=>s.GetProperty("Type").GetString() is "Standard" or "CoordinateBreak"
                    && (s.GetProperty("Radius").ValueKind!=JsonValueKind.Number||s.GetProperty("Radius").GetDouble()==0));
                if(!planar||doc.RootElement.GetProperty("scalars").GetProperty("EffectiveFocalLength").GetDouble()!=1e10)
                    throw new InvalidDataException("Native infinite-power sentinel is not independently verified by the plane-only prescription");
                var a=new NumericResult();var e=new NumericResult();
                a.Scalars.Add(new("OpticalPower",((JsonElement)data.Values["OpticalPowerInverseMillimeters"]).GetDouble(),"InverseMillimeter"));
                e.Scalars.Add(new("OpticalPower",0,"InverseMillimeter"));
                e.Transformations.Add("Plane-only prescription and native EFFL=1e10 sentinel explicitly mean zero power. Core publishes infinity, not a zero focal length. This verifies the zero-power state, not a finite EFL value.");
                return(a,e,settings with{Quantities=new(){["OpticalPower"]=new Tolerances(0,0,0,0,1)}});
            }
        }
        if(r.CanonicalAnalysisKey=="Single Ray Trace")
        {
            var directory=Path.GetDirectoryName(native)!;
            var model=JsonFiles.Read<JsonElement>(Path.Combine(directory,"model.json"));
            var surfaces=model.GetProperty("surfaces").EnumerateArray().ToArray();
            var source=JsonFiles.Read<JsonElement>(Path.Combine(directory,"request.json")).GetProperty("input").GetString()!;
            var retained=File.ReadLines(source).Select(line=>line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries))
                .Where(t=>t.Length>1&&t[0] is "REAX" or "REAY" or "REAZ" or "REAR" or "REAA" or "REAB" or "REAC")
                .Select(t=>int.Parse(t[1],System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
            var map=Enumerable.Range(0,surfaces.Length).Where(i=>surfaces[i].GetProperty("Type").GetString()!="CoordinateBreak"||retained.Contains(i)).ToArray();
            if(map.Length!=r.SurfaceCount)throw new InvalidDataException("Unverified source/Core surface correspondence");
            var a=ResultNormalizer.Workbench(workbench,entry,r);
            var e=ResultNormalizer.Zemax(native,entry,r with{SurfaceCount=surfaces.Length});
            var compoundPaths=data.Values.TryGetValue("CompoundPortOpticalPathLengths",out var optical)
                ? ((JsonElement)optical).Deserialize<double[][]>()!.ToDictionary(p=>(int)p[0],p=>p[1]) : new Dictionary<int,double>();
            var physical=Enumerable.Range(1,map.Length-1).Where(i=>surfaces[map[i]].GetProperty("Type").GetString() is not ("CoordinateBreak" or "NonSequential")).ToArray();
            for(var column=0;column<a.Series.Count;column++)
            {
                var actual=a.Series[column];var expected=e.Series[column];var path=actual.Id=="path-length";
                double A(int index)=>path?actual.X.Select((x,i)=>(x,i)).Where(p=>p.x>Previous(index)&&p.x<=index).Sum(p=>compoundPaths.GetValueOrDefault((int)p.x,actual.Y[p.i])):actual.Y[Array.IndexOf(actual.X,(double)index)];
                double E(int index)=>path?expected.X.Select((x,i)=>(x,i)).Where(p=>p.x>map[Previous(index)]&&p.x<=map[index]).Sum(p=>expected.Y[p.i]):expected.Y[Array.IndexOf(expected.X,(double)map[index])];
                int Previous(int index)=>physical.TakeWhile(i=>i<index).LastOrDefault();
                a.Series[column]=actual with{X=physical.Select(i=>(double)i).ToArray(),Y=physical.Select(A).ToArray()};
                e.Series[column]=expected with{X=physical.Select(i=>(double)i).ToArray(),Y=physical.Select(E).ToArray()};
            }
            e.Transformations.Add("Physical surface correspondence from original source rows and retained ray operands. Transparent breaks and differing NSC entry/exit endpoints are explicit exclusions; next physical port and accumulated path remain compared. No fitted coordinates or propagation data.");
            return(a,e,settings);
        }
        if(r.CanonicalAnalysisKey=="Spot Diagram")
        {
            var pane=data.PlotPanes?.Single()??throw new InvalidDataException("Expected selected angular spot pane");
            var s=pane.Series.Single();
            if(s.XQuantity!=AnalysisAxisQuantity.IncidentAngle||s.YQuantity!=AnalysisAxisQuantity.IncidentAngle
                ||s.XUnit!=AnalysisAxisUnit.Milliradian||s.YUnit!=AnalysisAxisUnit.Milliradian||pane.Metrics?.Count!=2)
                throw new InvalidDataException("Core spot axes/metrics are not the declared angular contract");
            var text=File.ReadAllText(Path.ChangeExtension(native,".txt"));
            // Native spot metrics use arcseconds or microradians, independently
            // of plotted coordinates and of the system AF unit. Require the
            // explicit RMS metric-unit readback for each individual case.
            var metricLine=Regex.Match(text,@"RMS[^\r\n]*(?:半径|radius)[^\r\n]*",RegexOptions.IgnoreCase).Value;
            var metricUnit=Regex.IsMatch(metricLine,@"弧度-秒|arcsec|seconds",RegexOptions.IgnoreCase)?"ArcSecond"
                :Regex.IsMatch(metricLine,@"µr|μr|microrad|微弧度",RegexOptions.IgnoreCase)?"Microradian"
                :throw new InvalidDataException("Native spot metric angular unit readback is missing");
            if(r.Configuration>1)
            {
                using var readback=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(native)!,"captured-settings.json")));
                if(readback.RootElement.GetProperty("properties").GetProperty("Configuration").GetInt32()!=r.Configuration+1)
                    throw new InvalidDataException("Native numbered spot configuration differs");
            }
            var nativeSpot=doc.RootElement.GetProperty("spot");var scale=PhysicalNormalization.UnitScale(metricUnit,"Milliradian");
            var a=new NumericResult();var e=new NumericResult();
            a.Scalars.Add(new("RmsSpotRadius",pane.Metrics[0].Value,"Milliradian"));a.Scalars.Add(new("GeoSpotRadius",pane.Metrics[1].Value,"Milliradian"));
            e.Scalars.Add(new("RmsSpotRadius",nativeSpot.GetProperty("rms").GetDouble()*scale,"Milliradian"));
            e.Scalars.Add(new("GeoSpotRadius",nativeSpot.GetProperty("geo").GetDouble()*scale,"Milliradian"));
            e.Transformations.Add($"Native text explicitly identifies spot RMS/GEO metrics as {metricUnit}; convert to mrad. Plot/system units are independent.");
            return(a,e,settings with{Quantities=new(){["RmsSpotRadius"]=angularRadius,["GeoSpotRadius"]=angularRadius}});
        }
        if(entry.ZemaxSettingsMapper is "fft-psf" or "huygens-psf")
        {
            var a=ResultNormalizer.Workbench(workbench,entry,r);var g=doc.RootElement.GetProperty("dataGrids");
            if(g.GetArrayLength()!=1)throw new InvalidDataException("Expected one angular native PSF grid");
            var grid=g[0];var unit=r.NativeAfocalAngleUnit!;var scale=PhysicalNormalization.UnitScale(unit,"Milliradian");
            var xs=Enumerable.Range(0,grid.GetProperty("nx").GetInt32()).Select(i=>(grid.GetProperty("minX").GetDouble()+i*grid.GetProperty("dx").GetDouble())*scale).ToArray();
            var ys=Enumerable.Range(0,grid.GetProperty("ny").GetInt32()).Select(i=>(grid.GetProperty("minY").GetDouble()+i*grid.GetProperty("dy").GetDouble())*scale).ToArray();
            var e=new NumericResult();e.Grids.Add(new("grid",xs,ys,grid.GetProperty("values").Deserialize<double?[][]>()!,new("IncidentAngle","Milliradian"),new("IncidentAngle","Milliradian"),new("Irradiance","Dimensionless")));
            if(a.Grids.Single().XAxis.Quantity!="IncidentAngle"||a.Grids.Single().YAxis.Quantity!="IncidentAngle")throw new InvalidDataException("Core PSF axes are not angular");
            e.Transformations.Add("Native AF PSF coordinates decoded in model AF angle units and converted to mrad; raw grid, origin, ordering and intensity unchanged.");
            return(a,e,settings);
        }
        if(r.CanonicalAnalysisKey=="Huygens MTF")
        {
            var actualEntry=entry with{XAxis=new("SpatialFrequency","CyclesPerMilliradian")};
            var nativeEntry=entry with{XAxis=new("SpatialFrequency",AfocalAnalysisContracts.FrequencyUnit(r))};
            var a=ResultNormalizer.Workbench(workbench,actualEntry,r);var e=ResultNormalizer.Zemax(native,nativeEntry,r);
            for(var i=0;i<e.Series.Count;i++)e.Series[i]=PhysicalNormalization.Convert(e.Series[i],actualEntry.XAxis!,new("Modulation","Dimensionless"),e.Transformations);
            return(a,e,settings);
        }
        if(r.CanonicalAnalysisKey=="Huygens Through Focus MTF")
        {
            var a=new NumericResult();var e=new NumericResult();var groups=doc.RootElement.GetProperty("dataSeries");
            if(groups.GetArrayLength()!=1||data.PlotSeries.Count!=2)throw new InvalidDataException("Unexpected through-focus curve grouping");
            var group=groups[0];var x=group.GetProperty("x").Deserialize<double[]>()!;var y=group.GetProperty("y").Deserialize<double[][]>()!;
            if(y.Any(row=>row.Length!=2))throw new InvalidDataException("Expected native T/S through-focus columns");
            if(!File.ReadAllText(Path.ChangeExtension(native,".txt")).Contains("屈光度",StringComparison.Ordinal))throw new InvalidDataException("Native diopter readback is missing");
            for(var i=0;i<2;i++)
            {
                var s=data.PlotSeries[i];if(s.XQuantity!=AnalysisAxisQuantity.Defocus||s.XUnit!=AnalysisAxisUnit.Diopter)throw new InvalidDataException("Core through-focus axis is not diopters");
                a.Series.Add(new("curve:"+i,s.Name,s.Points.Select(p=>p.X).ToArray(),s.Points.Select(p=>p.Y).ToArray(),new("Defocus","Diopter"),new("Modulation","Dimensionless")));
                e.Series.Add(new("curve:"+i,"Native T/S",x,y.Select(row=>row[i]).ToArray(),new("Defocus","Diopter"),new("Modulation","Dimensionless")));
            }
            return(a,e,settings);
        }
        if(r.CanonicalAnalysisKey=="RMS vs Field"&&data.PlotSeries.Any(s=>s.YUnit is AnalysisAxisUnit.Millimeter or AnalysisAxisUnit.Micrometer))
            throw new InvalidDataException("Core RMS-vs-field publishes a length radius for an afocal system; native reports angular radius. No dimensional substitution.");
        if(r.CanonicalAnalysisKey=="RMS vs Field")
            settings=settings with{Quantities=new(){["Radius"]=settings.Quantities["Radius"] with{Absolute=angularRadius.Absolute}}};
        return(ResultNormalizer.Workbench(workbench,entry,r),ResultNormalizer.Zemax(native,entry,r),settings);
    }
}
