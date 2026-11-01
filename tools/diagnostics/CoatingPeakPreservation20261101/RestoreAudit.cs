using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

internal static class RestoreAudit
{
    public static async Task Main(string[] args)
    {
        using var directions=JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
        var rows=new List<object>();Directory.CreateDirectory(args[2]);var service=new DesignService();
        foreach(var file in Directory.GetFiles(args[0],"*.coating.json").Order(StringComparer.Ordinal))
        {
            var source=await ExperimentStore.OpenAsync(file);var d=source with{Result=null,Candidates=null,Tolerance=null};var t=d.Target;
            var initial=service.Evaluate(d);var materials=d.Materials.ToDictionary(m=>m.Id,m=>m.Resolve(new MaterialRegistry()));
            var indices=Enumerable.Range(0,d.Layers.Length).Where(i=>d.Layers[i].Variable).ToArray();
            var units=indices.Select(i=>CoherentThinFilmSolver.CharacteristicThickness(materials[d.Layers[i].MaterialId],materials[d.IncidentId],t.CenterNm,t.AngleDegrees)).ToArray();
            var phase=indices.Select((i,j)=>d.Layers[i].ThicknessNm/units[j]).ToArray();
            var lower=units.Select(u=>d.Settings.MinimumThicknessNm/u).ToArray();var upper=units.Select(u=>d.Settings.MaximumThicknessNm/u).ToArray();
            double[] Od(Experiment e)=>t.StopBands!.Select(b=>e.Result!.Spectrum.Where(s=>s.WavelengthNanometers>=b.MinimumNm&&s.WavelengthNanometers<=b.MaximumNm).Min(s=>s.Power.OpticalDensity)).ToArray();
            var anchorOd=Od(initial);var anchor=initial.Result!.Metrics;var evaluations=0;var cache=new Dictionary<string,Experiment>();
            Experiment At(double[] point)
            {
                var key=string.Join(';',point.Select(x=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
                if(cache.TryGetValue(key,out var old))return old;
                var layers=d.Layers.ToArray();for(var j=0;j<point.Length;j++)layers[indices[j]]=layers[indices[j]] with{ThicknessNm=point[j]==phase[j]?d.Layers[indices[j]].ThicknessNm:Math.Clamp(point[j]*units[j],d.Settings.MinimumThicknessNm,d.Settings.MaximumThicknessNm)};
                var value=service.Evaluate(d with{Layers=layers});evaluations++;cache.Add(key,value);return value;
            }
            double[] Values(Experiment e)
            {
                var m=e.Result!.Metrics;
                return [(anchor.PeakTransmittance-m.PeakTransmittance)/t.PeakTransmittance,
                    (Math.Abs(m.FwhmNanometers!.Value-t.FwhmNm)-Math.Abs(anchor.FwhmNanometers!.Value-t.FwhmNm))/t.FwhmNm,
                    (m.PeakWavelengthNanometers-t.CenterNm-t.CenterToleranceNm)/t.FwhmNm,
                    (t.CenterNm-m.PeakWavelengthNanometers-t.CenterToleranceNm)/t.FwhmNm,
                    .. Od(e).Zip(anchorOd).Select(p=>(p.Second-p.First)/Math.Max(1,t.BlockingOd))];
            }
            InequalityLinearization Read(double[] point,bool derivatives)
            {
                var values=Values(At(point));if(!derivatives)return new(values,null);
                var jac=new double[values.Length,point.Length];
                for(var j=0;j<point.Length;j++)
                {
                    var plus=point.ToArray();var minus=point.ToArray();plus[j]=Math.Min(upper[j],point[j]+1e-5);minus[j]=Math.Max(lower[j],point[j]-1e-5);
                    if(plus[j]==minus[j])continue;
                    var a=Values(At(plus));var b=Values(At(minus));for(var k=0;k<values.Length;k++)jac[k,j]=(a[k]-b[k])/(plus[j]-minus[j]);
                }
                return new(values,jac);
            }
            var sourceRow=directions.RootElement.EnumerateArray().Single(x=>x.GetProperty("Name").GetString()==d.Name);
            foreach(var row in sourceRow.GetProperty("Directions").EnumerateArray().Take(2))
            {
                var step=row.GetProperty("Step").Deserialize<double[]>()!;var trial=phase.Select((p,j)=>Math.Clamp(p+step[j],lower[j],upper[j])).ToArray();
                var raw=At(trial);var before=evaluations;var corrected=NonlinearInequalityCorrection.Correct(phase,trial,lower,upper,Read);var value=At(corrected.Point);var constraints=Values(value);
                var joint=constraints.Max()<=0&&value.Result!.SamplingConverged&&value.Result.Metrics.PeakTransmittance>anchor.PeakTransmittance;
                var radius=row.GetProperty("Radius").GetDouble();
                rows.Add(new{d.Name,Radius=radius,Initial=anchor,InitialOd=anchorOd,Raw=raw.Result!.Metrics,RawOd=Od(raw),Final=value.Result!.Metrics,FinalOd=Od(value),Joint=joint,
                    corrected.Iterations,corrected.InitialViolation,corrected.FinalViolation,ExactConstraintValues=constraints,ExtraEvaluateCalls=evaluations-before,CoreCorrection=NonlinearInequalityCorrection.Version,
                    Scope="Root-cause probe using actual formal metrics and finite-difference correction; not a changed design gate or production optimizer"});
                await ExperimentStore.SaveAsync(value,Path.Combine(args[2],d.Name+"-"+radius.ToString(System.Globalization.CultureInfo.InvariantCulture)+".coating.json"));
                await File.WriteAllTextAsync(Path.Combine(args[2],"summary.json"),JsonSerializer.Serialize(rows,Experiment.JsonOptions));
                Console.WriteLine($"{d.Name} radius {radius}: correction joint={joint}; T={value.Result.Metrics.PeakTransmittance}; excess={constraints.Max()}");
            }
        }
    }
}
