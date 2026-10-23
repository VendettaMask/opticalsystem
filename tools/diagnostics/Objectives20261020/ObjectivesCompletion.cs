using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class ObjectivesCompletion
{
    internal static void Compare(string output, string nativeRoot)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required");
        Directory.CreateDirectory(output);
        var policyPath = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json";
        var policy = ComparisonConfiguration.Load(policyPath);
        var manifest = JsonFiles.Read<JsonElement>(Path.Combine(nativeRoot, "request-manifest.json"));
        var rows = new List<object>();
        foreach (var job in manifest.EnumerateArray())
        {
            if (job.GetProperty("kind").GetString() == "fft-energy") continue;
            var path = job.GetProperty("request").GetString()!;
            var raw = JsonFiles.Read<JsonElement>(path);
            var request = JsonFiles.Read<CanonicalAnalysisRequest>(path);
            var source = raw.GetProperty("input").GetString()!;
            if (JsonFiles.Hash(File.ReadAllBytes(source)) != job.GetProperty("sourceSha256").GetString())
                throw new InvalidDataException("Source changed");
            var optic = WorkbenchExecutor.Import(source).GetAwaiter().GetResult().Configurations[request.Configuration - 1];
            if (raw.TryGetProperty("referencePrimaryWavelength", out var primary))
                for (var wi=0;wi<optic.Wavelengths.Count;wi++) optic.Wavelengths[wi].IsPrimary=wi==primary.GetInt32()-1;
            var nativeDirectory = job.GetProperty("output").GetString()!;
            var native = JsonFiles.Read<JsonElement>(Path.Combine(nativeDirectory, "point-reference.json"));
            var id = Path.GetFileName(nativeDirectory); var folder = Path.Combine(output, id); Directory.CreateDirectory(folder);
            var inputs = native.GetProperty("inputs").EnumerateArray().ToArray();
            var rays = inputs.GroupBy(p => (p.GetProperty("hx").GetDouble(), p.GetProperty("hy").GetDouble())).SelectMany(g =>
                optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                    g.Key.Item1, g.Key.Item2, optic.Wavelengths[request.Wavelength - 1].Micrometers,
                    g.Select(p => new PupilSample(p.GetProperty("px").GetDouble(), p.GetProperty("py").GetDouble(), 1)).ToArray(),
                    aimAtStop: optic.RayAimingEnabled).Rays).ToArray();
            JsonFiles.Write(Path.Combine(folder,"core-launches.json"),rays);
            var histories = optic.SequentialRayTracer.Trace(new(rays)).RayHistories.ToArray();
            JsonFiles.Write(Path.Combine(folder, "core-rays.json"), histories);
            var surfaces = native.GetProperty("surfaces").EnumerateArray().ToArray(); var image = surfaces[^1].GetProperty("rays");
            var maskMismatches = 0; var coordinateFailures = 0; var directionFailures = 0; var compared = 0;
            var maximumCoordinate = 0d; var maximumDirection = 0d;
            var failures = new List<object>();
            for (var i = 0; i < histories.Length; i++)
            {
                var last = histories[i][^1]; var expectedLast = image[i];
                var accepted = Valid(expectedLast);
                if (accepted != (last.SurfaceNumber == optic.SurfaceGroup.Items.Count - 1 && !last.Vignetted && last.Intensity > 0)) maskMismatches++;
                foreach (var surface in surfaces)
                {
                    var si = surface.GetProperty("surface").GetInt32(); var expected = surface.GetProperty("rays")[i];
                    if (!Valid(expected)) continue;
                    var actual = histories[i].FirstOrDefault(s => s.SurfaceNumber == si);
                    if (actual is null || actual.Vignetted) { coordinateFailures++; continue; }
                    var frame = optic.SurfaceGroup.Items[si].CoordinateSystem;
                    var p = frame.ToLocalPoint(actual.Position); var d = frame.ToLocalDirection(actual.Direction); compared++;
                    foreach (var (value, name) in new[] { (p.X,"x"),(p.Y,"y"),(p.Z,"z") })
                    { var z=expected.GetProperty(name).GetDouble(); var delta=Math.Abs(value-z);maximumCoordinate=Math.Max(maximumCoordinate,delta);if(delta>1e-6+1e-6*Math.Abs(z)){coordinateFailures++;failures.Add(new{i,input=inputs[i],si,name,actual=value,expected=z,delta});} }
                    foreach (var (value, name) in new[] { (d.X,"l"),(d.Y,"m"),(d.Z,"n") })
                    { var z=expected.GetProperty(name).GetDouble(); var delta=Math.Abs(value-z);maximumDirection=Math.Max(maximumDirection,delta);if(delta>1e-8+1e-6*Math.Abs(z)){directionFailures++;failures.Add(new{i,input=inputs[i],si,name,actual=value,expected=z,delta});} }
                }
            }
            JsonFiles.Write(Path.Combine(folder, "ray-comparison.json"), new { inputs = inputs.Length, compared, maskMismatches, coordinateFailures, directionFailures, maximumCoordinate, maximumDirection });
            JsonFiles.Write(Path.Combine(folder,"ray-failures.json"),failures);
            var launchPath=Path.Combine(nativeDirectory,"direct-launches.json");
            if(File.Exists(launchPath))
            {
                var launches=JsonFiles.Read<JsonElement>(launchPath);
                var frame=optic.SurfaceGroup.Items[1].CoordinateSystem;
                var direct=rays.Select((r,i)=> {
                    var origin=frame.ToGlobalPoint(new Vector3D(launches[i].GetProperty("x").GetDouble(),launches[i].GetProperty("y").GetDouble(),launches[i].GetProperty("z").GetDouble()));
                    var direction=frame.ToGlobalDirection(new Vector3D(launches[i].GetProperty("l").GetDouble(),launches[i].GetProperty("m").GetDouble(),launches[i].GetProperty("n").GetDouble()));
                    // Native direct coordinates use the first vertex plane,
                    // which can be inside the first sphere at transverse fields.
                    // Translate along the unchanged native line to the Core
                    // launch plane so both tracers select the same entry branch.
                    var t=Math.Abs(direction.Z)>=.5?(r.Origin.Z-origin.Z)/direction.Z:
                        Math.Abs(direction.Y)>=Math.Abs(direction.X)?(r.Origin.Y-origin.Y)/direction.Y:(r.Origin.X-origin.X)/direction.X;
                    return r with {Origin=origin+direction*t,Direction=direction,Intensity=1,IsNormalized=true}; }).ToArray();
                var directHistories=optic.SequentialRayTracer.Trace(new(direct)).RayHistories.ToArray();
                JsonFiles.Write(Path.Combine(folder,"core-native-launch-rays.json"),directHistories);
                var directFailures=new List<object>(); var directCompared=0; var directMaxCoordinate=0d;var directMaxDirection=0d;
                for(var i=0;i<directHistories.Length;i++) foreach(var surface in surfaces)
                {
                    var si=surface.GetProperty("surface").GetInt32();var e=surface.GetProperty("rays")[i];if(!Valid(e))continue;
                    var a=directHistories[i].FirstOrDefault(s=>s.SurfaceNumber==si);
                    if(a is null||a.Vignetted){directFailures.Add(new{i,si,reason="Missing or clipped"});continue;}
                    var cs=optic.SurfaceGroup.Items[si].CoordinateSystem;var p=cs.ToLocalPoint(a.Position);var d=cs.ToLocalDirection(a.Direction);directCompared++;
                    foreach(var (v,name,absolute) in new[]{(p.X,"x",1e-6),(p.Y,"y",1e-6),(p.Z,"z",1e-6),(d.X,"l",1e-8),(d.Y,"m",1e-8),(d.Z,"n",1e-8)})
                    {
                        var z=e.GetProperty(name).GetDouble();var delta=Math.Abs(v-z);
                        if(absolute==1e-6)directMaxCoordinate=Math.Max(directMaxCoordinate,delta);else directMaxDirection=Math.Max(directMaxDirection,delta);
                        if(delta>absolute+1e-6*Math.Abs(z))directFailures.Add(new{i,input=inputs[i],si,name,actual=v,expected=z,delta});
                    }
                }
                JsonFiles.Write(Path.Combine(folder,"native-launch-comparison.json"),new{directCompared,directMaxCoordinate,directMaxDirection,failures=directFailures});
            }
            foreach (var key in job.GetProperty("originalKeys").EnumerateArray().Select(x => x.GetString()!))
            {
                var kind = key.Split('|')[3];
                var actualRequest = request with { CanonicalAnalysisKey = kind };
                NumericResult actual; var expected = new NumericResult();
                if (job.GetProperty("kind").GetString() == "fan")
                {
                    var data = new WorkbenchRuntime(optic).BuildAnalysisData(kind, request.WorkbenchSettings);
                    var dataPath = Path.Combine(folder, JsonFiles.Slug(kind) + "-data.json"); JsonFiles.Write(dataPath, data);
                    var entry = AnalysisComparisonRegistry.Get(kind);
                    actual = ResultNormalizer.Workbench(dataPath, entry, actualRequest);
                    expected=NativeExplicitFanNormalizer.Read(Path.Combine(nativeDirectory,"point-reference.json"),entry,actualRequest);
                }
                else
                {
                    actual = new NumericResult();
                    foreach (var (code,axis,sagittal) in new[] { ("FCGT","tangential",false),("FCGS","sagittal",true) })
                    {
                        var terms = native.GetProperty("operands").EnumerateArray().Where(p => p.GetProperty("code").GetString() == code).ToArray();
                        var x = terms.Select(p => p.GetProperty("hy").GetDouble()).ToArray();
                        var y = terms.Select(p => p.GetProperty("value").GetDouble()).ToArray();
                        expected.Series.Add(new(axis,"Native generalized curvature",x,y,new("NormalizedField","Dimensionless"),new("Defocus","Millimeter")));
                        var curveErrors=new List<object>();
                        var core = x.Select(hy => {try{return FieldCurvatureMetrics.Evaluate(optic,optic.Wavelengths[request.Wavelength-1].Micrometers,0,hy,sagittal);}
                            catch(Exception e){curveErrors.Add(new{hy,error=e.ToString()});return double.NaN;}}).ToArray();
                        JsonFiles.Write(Path.Combine(folder,axis+"-errors.json"),curveErrors);
                        actual.Series.Add(new(axis,"Shared generalized curvature",x,core,new("NormalizedField","Dimensionless"),new("Defocus","Millimeter")));
                    }
                }
                var run = new AnalysisRun { Key=kind,Directory=JsonFiles.Slug(key),Request=actualRequest };
                string? error = null;
                try { ComparisonRunner.Compare(run,AnalysisComparisonRegistry.Get(kind),policy.Analyses[kind],actual,expected,folder); }
                catch(Exception e) { error=e.ToString();run.Conclusion=Conclusion.Incomparable; }
                if(maskMismatches+coordinateFailures+directionFailures>0)run.Conclusion=Conclusion.Difference;
                rows.Add(new { key,route=job.GetProperty("kind").GetString(),conclusion=run.Conclusion,metrics=run.Metrics,error,
                    rayInputs=inputs.Length,maskMismatches,coordinateFailures,directionFailures,maximumCoordinate,maximumDirection });
                JsonFiles.Write(Path.Combine(output,"summary.json"),rows); Console.WriteLine($"{rows.Count}: {key}: {run.Conclusion}");
            }
        }
        JsonFiles.Write(Path.Combine(output,"identity.json"),new {coreSha256=JsonFiles.Hash(File.ReadAllBytes(typeof(FieldCurvatureMetrics).Assembly.Location)),policySha256=JsonFiles.Hash(File.ReadAllBytes(policyPath))});
    }
    private static bool Valid(JsonElement p) => p.GetProperty("error").GetInt32()==0 && p.GetProperty("vignette").GetInt32()==0;
}
