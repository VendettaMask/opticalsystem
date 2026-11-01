// Standalone offline probe. It calls only the frozen product Core and design engine.
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class PhaseOrderAudit
{
    public static async Task Main(string[] args)
    {
        if (args[0] == "polish") { await Polish(args[1], args[2], args[3]); return; }
        Directory.CreateDirectory(args[1]); var service = new DesignService(); var summaries = new List<object>();
        var refine = typeof(DesignService).GetMethod("RefineGeneralThickness", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var grid = typeof(DesignService).GetMethod("ObjectiveGrid", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var file in Directory.GetFiles(args[0], "*.coating.json").Order(StringComparer.Ordinal))
        {
            var d = await ExperimentStore.OpenAsync(file); d = d with { Result = null, Candidates = null, Tolerance = null };
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(15)); var timer = System.Diagnostics.Stopwatch.StartNew();
            var initial = service.Evaluate(d); var mats = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
            double[] Od(Experiment e) => (e.Target.StopBands ?? []).Select(b => e.Result!.Spectrum.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity)).ToArray();
            double[] V(Experiment e)
            {
                var t = e.Target; var m = e.Result!.Metrics;
                return t.Kind != DesignKind.NarrowBand ? [Math.Max(0, t.Reflectance-m.MinimumReflectance)/(1-t.Reflectance)] :
                    [Math.Max(0,t.PeakTransmittance-m.PeakTransmittance)/t.PeakTransmittance,
                    m.FwhmNanometers is {} w ? Math.Max(0,Math.Abs(w-t.FwhmNm)-t.FwhmToleranceNm)/t.FwhmNm : 1,
                    Math.Max(0,Math.Abs(m.PeakWavelengthNanometers-t.CenterNm)-t.CenterToleranceNm)/t.FwhmNm,
                    ..Od(e).Select(x=>Math.Max(0,t.BlockingOd-x)/Math.Max(1,t.BlockingOd))];
            }
            bool Joint(Experiment e) => (!initial.Result!.SamplingConverged || e.Result!.SamplingConverged)
                && (!initial.Result.Passed || e.Result!.Passed) && (d.Target.Kind != DesignKind.NarrowBand
                    ? e.Result!.Metrics.MinimumReflectance >= initial.Result.Metrics.MinimumReflectance
                    : e.Result!.Metrics.PeakTransmittance >= initial.Result.Metrics.PeakTransmittance
                    && e.Result.Metrics.FwhmNanometers is {} w && Math.Abs(w-d.Target.FwhmNm) <= Math.Abs(initial.Result.Metrics.FwhmNanometers!.Value-d.Target.FwhmNm)
                    && Od(e).Zip(Od(initial)).All(x=>x.First>=x.Second) && V(e).Zip(V(initial)).All(x=>x.First<=x.Second));
            var candidates = new List<(string Role, Experiment Value)>(); var traces = new List<object>();
            var free = Enumerable.Range(0,d.Layers.Length).Where(i=>d.Layers[i].Variable).ToArray();
            var periods = free.ToDictionary(i=>i,i=>2*CoherentThinFilmSolver.QuarterWaveThickness(mats[d.Layers[i].MaterialId],mats[d.IncidentId],d.Target.CenterNm,d.Target.AngleDegrees));
            var identities = new HashSet<string>();
            void Check(int[] indices,int[] orders)
            {
                var layers=d.Layers.ToArray();for(var j=0;j<indices.Length;j++)layers[indices[j]]=layers[indices[j]] with{ThicknessNm=layers[indices[j]].ThicknessNm+orders[j]*periods[indices[j]]};
                if(layers.Any(l=>l.ThicknessNm<d.Settings.MinimumThicknessNm||l.ThicknessNm>d.Settings.MaximumThicknessNm))return;
                var id=string.Join(';',layers.Select(l=>l.ThicknessNm.ToString("R")));if(!identities.Add(id))return;
                var value=service.Evaluate(d with{Layers=layers},token:limit.Token);var role=string.Join(',',indices);
                candidates.Add((role,value));traces.Add(new{Indices=indices,Orders=orders,value.Result!.Metrics,Od=Od(value),value.Result.Passed,Violations=V(value),Joint=Joint(value)});
            }
            foreach(var i in free)
            {
                var count=Math.Min((int)Math.Ceiling(Math.Sqrt(d.Settings.MaximumIterations)),(int)Math.Floor((d.Layers[i].ThicknessNm-d.Settings.MinimumThicknessNm)/periods[i]));
                for(var k=1;k<=count;k++)Check([i],[-k]);Check([i],[1]);
            }
            // Every eligible pair participates, independent of a material ID or fixed layer position.
            for(var a=0;a<free.Length;a++)for(var b=a+1;b<free.Length;b++)
                if(d.Layers[free[a]].ThicknessNm>periods[free[a]]||d.Layers[free[b]].ThicknessNm>periods[free[b]])
                    foreach(var x in new[]{-1,1})foreach(var y in new[]{-1,1})Check([free[a],free[b]],[x,y]);
            var selected=candidates.OrderByDescending(x=>x.Value.Result!.Passed).ThenBy(x=>V(x.Value).Max()).ThenBy(x=>V(x.Value).Sum(v=>v*v))
                .DistinctBy(x=>x.Role).Take(12).ToArray();
            var best=initial;var runs=new List<object>();
            foreach(var candidate in candidates.Where(x=>Joint(x.Value)))if(candidate.Value.Result!.Metrics.PeakTransmittance>best.Result!.Metrics.PeakTransmittance||d.Target.Kind==DesignKind.HighReflector&&candidate.Value.Result.Metrics.MinimumReflectance>best.Result.Metrics.MinimumReflectance)best=candidate.Value;
            foreach(var candidate in selected)
            {
                var allowance=Math.Min(d.Settings.MaximumIterations,Math.Max(1,(int)Math.Ceiling(Math.Sqrt(d.Settings.MaximumIterations))));
                var result=((FilmLayer[] Layers,double Merit,RunRecord Run))refine.Invoke(service,[candidate.Value,mats,grid.Invoke(null,[d])!,allowance,null,limit.Token,candidate.Value])!;
                var value=service.Evaluate(d with{Layers=result.Layers},result.Run,token:limit.Token);
                var retained=Joint(value)&&(value.Result!.Passed&&!best.Result!.Passed||value.Result!.Passed==best.Result!.Passed&&V(value).Sum(v=>v*v)<V(best).Sum(v=>v*v));
                if(retained)best=value;
                runs.Add(new{candidate.Role,InitialLayers=candidate.Value.Layers,FinalLayers=value.Layers,InitialMetrics=candidate.Value.Result!.Metrics,value.Result!.Metrics,Od=Od(value),value.Result.Passed,Violations=V(value),Joint=Joint(value),Retained=retained,result.Run});
            }
            await ExperimentStore.SaveAsync(best,Path.Combine(args[1],Path.GetFileName(file)));
            summaries.Add(new{d.Name,Scope="Offline half-wave order neighbors; product code unchanged",Seconds=timer.Elapsed.TotalSeconds,Evaluated=candidates.Count,Initial=initial.Result!.Metrics,InitialOd=Od(initial),Final=best.Result!.Metrics,FinalOd=Od(best),best.Result.Passed,Joint=Joint(best),Trials=traces,Refinements=runs});
            await File.WriteAllTextAsync(Path.Combine(args[1],"summary.json"),JsonSerializer.Serialize(summaries,Experiment.JsonOptions));
            Console.WriteLine($"{d.Name}: candidates={candidates.Count}; T={best.Result.Metrics.PeakTransmittance:G10}; W={best.Result.Metrics.FwhmNanometers:G10}; R={best.Result.Metrics.MinimumReflectance:G10}; passed={best.Result.Passed}; {timer.Elapsed.TotalSeconds:F1}s");
        }
    }

    private static async Task Polish(string inputs,string scan,string output)
    {
        Directory.CreateDirectory(output);var summaries=new List<object>();var service=new DesignService();
        using var json=JsonDocument.Parse(await File.ReadAllTextAsync(scan));
        foreach(var row in json.RootElement.EnumerateArray())
        {
            var name=row.GetProperty("Name").GetString()!;var d=await ExperimentStore.OpenAsync(Path.Combine(inputs,name+".coating.json"));
            if(d.Target.Kind!=DesignKind.NarrowBand)continue;
            d=d with{Result=null,Candidates=null,Tolerance=null};var mats=d.Materials.ToDictionary(m=>m.Id,m=>m.Resolve(new MaterialRegistry()));
            var trials=row.GetProperty("Trials").EnumerateArray().ToArray();
            double V(JsonElement e)=>e.GetProperty("Violations").EnumerateArray().Max(x=>x.GetDouble());
            var selected=trials.OrderBy(V).Take(1).Concat(trials.Where(t=>t.GetProperty("Od").EnumerateArray().All(x=>x.GetDouble()>=d.Target.BlockingOd))
                .OrderByDescending(t=>t.GetProperty("Metrics").GetProperty("PeakTransmittance").GetDouble()).Take(1)).DistinctBy(t=>t.GetRawText()).ToArray();
            var start=service.Evaluate(d);var id=0;
            foreach(var trial in selected)
            {
                var layers=d.Layers.ToArray();var indices=trial.GetProperty("Indices").Deserialize<int[]>()!;var orders=trial.GetProperty("Orders").Deserialize<int[]>()!;
                for(var j=0;j<indices.Length;j++){var i=indices[j];layers[i]=layers[i] with{ThicknessNm=layers[i].ThicknessNm+orders[j]*2*CoherentThinFilmSolver.QuarterWaveThickness(mats[layers[i].MaterialId],mats[d.IncidentId],d.Target.CenterNm,d.Target.AngleDegrees)};}
                using var limit=new CancellationTokenSource(TimeSpan.FromMinutes(15));var watch=System.Diagnostics.Stopwatch.StartNew();
                var result=service.Optimize(d with{Layers=layers},token:limit.Token);id++;
                var file=name+"-phase-"+id+".coating.json";await ExperimentStore.SaveAsync(result,Path.Combine(output,file));
                double[] Od(Experiment e)=>e.Target.StopBands!.Select(b=>e.Result!.Spectrum.Where(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).Min(s=>s.Power.OpticalDensity)).ToArray();
                var joint=result.Result!.Metrics.PeakTransmittance>=start.Result!.Metrics.PeakTransmittance&&Math.Abs(result.Result.Metrics.FwhmNanometers!.Value-d.Target.FwhmNm)<=Math.Abs(start.Result.Metrics.FwhmNanometers!.Value-d.Target.FwhmNm)
                    &&Od(result).Zip(Od(start)).All(x=>x.First>=x.Second)&&(!start.Result.SamplingConverged||result.Result.SamplingConverged)&&(!start.Result.Passed||result.Result.Passed);
                summaries.Add(new{Name=name,File=file,Indices=indices,Orders=orders,Seconds=watch.Elapsed.TotalSeconds,Initial=start.Result.Metrics,InitialOd=Od(start),Final=result.Result.Metrics,FinalOd=Od(result),result.Result.Passed,Joint=joint,result.Result.Run});
                await File.WriteAllTextAsync(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(summaries,Experiment.JsonOptions));
                Console.WriteLine($"{file}: T={result.Result.Metrics.PeakTransmittance:G10}; W={result.Result.Metrics.FwhmNanometers:G10}; passed={result.Result.Passed}; joint={joint}; {watch.Elapsed.TotalSeconds:F1}s");
            }
        }
    }
}
