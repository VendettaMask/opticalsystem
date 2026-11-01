using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

internal static class CurvatureTrialAudit
{
    internal static async Task Run(string curvaturePath,string retained,string output)
    {
        Directory.CreateDirectory(output);using var json=JsonDocument.Parse(await File.ReadAllTextAsync(curvaturePath));var summaries=new List<object>();var service=new DesignService();
        foreach(var row in json.RootElement.EnumerateArray())
        {
            var name=row.GetProperty("Name").GetString()!;var d=await ExperimentStore.OpenAsync(Path.Combine(retained,name+".coating.json"));if(d.Result!.Passed)continue;
            var indices=row.GetProperty("Indices").Deserialize<int[]>()!;var units=row.GetProperty("Units").Deserialize<double[]>()!;var weights=row.GetProperty("Weights").Deserialize<double[]>()!;var hs=row.GetProperty("Hessians").Deserialize<double[][][]>()!;var n=indices.Length;var scale=1-d.Result.Metrics.MinimumReflectance;
            var h=new double[n,n];for(var k=0;k<weights.Length;k++)for(var i=0;i<n;i++)for(var j=0;j<n;j++)h[i,j]+=weights[k]*hs[k][i][j]/scale;
            var hscale=Enumerable.Range(0,n).Max(i=>Math.Abs(h[i,i]));double[,]? l=null;var shift=0.0;
            for(var attempt=0;attempt<32;attempt++)
            {
                var factor=new double[n,n];var valid=true;
                for(var i=0;i<n&&valid;i++)for(var j=0;j<=i;j++){var v=h[i,j]+(i==j?shift:0);for(var k=0;k<j;k++)v-=factor[i,k]*factor[j,k];if(i==j){if(!(v>0)){valid=false;break;}factor[i,j]=Math.Sqrt(v);}else factor[i,j]=v/factor[j,j];}
                if(valid){l=factor;break;}shift=shift==0?Math.Sqrt(Math.BitIncrement(1.0)-1)*hscale:shift*10;
            }
            if(l is null)continue;
            var mats=d.Materials.ToDictionary(m=>m.Id,m=>m.Resolve(new MaterialRegistry()));var snapshot=new CoherentThinFilmSolver(mats[d.IncidentId],mats[d.SubstrateId],d.Layers.Select(f=>new CoherentFilm(mats[f.MaterialId],f.ThicknessNm)));var waves=d.Result.Spectrum.Select(s=>s.WavelengthNanometers).ToArray();var layers=d.Layers;var worst=scale;var lambda=.001;var trace=new List<object>();
            for(var iteration=0;iteration<12;iteration++)
            {
                var solver=snapshot.WithThicknesses(layers.Select(f=>f.ThicknessNm).ToArray());var current=waves.Select(w=>solver.EvaluateThicknessSensitivity(w,d.Target.AngleDegrees,d.Target.Polarization)).ToArray();var loss=current.Select(s=>1-s.Power.Reflectance).ToArray();var level=loss.Max()/scale;
                var jac=new double[n+1,n+1];var residual=new double[n+1];jac[0,n]=1;residual[0]=level;for(var i=0;i<n;i++)for(var j=0;j<n;j++)jac[i+1,j]=l[j,i];
                var a=new double[waves.Length+2*n+2,n+1];var b=new double[a.GetLength(0)];
                for(var i=0;i<waves.Length;i++){a[i,n]=-1;b[i]=Math.Max(0,level-loss[i]/scale);for(var j=0;j<n;j++)a[i,j]=-current[i].Layers[indices[j]].Reflectance*units[j]/scale;}
                for(var j=0;j<n;j++){a[waves.Length+2*j,j]=1;a[waves.Length+2*j+1,j]=-1;b[waves.Length+2*j]=Math.Max(0,Math.Min(1,(d.Settings.MaximumThicknessNm-layers[indices[j]].ThicknessNm)/units[j]));b[waves.Length+2*j+1]=Math.Max(0,Math.Min(1,(layers[indices[j]].ThicknessNm-d.Settings.MinimumThicknessNm)/units[j]));}
                a[a.GetLength(0)-2,n]=-1;b[^2]=level;a[a.GetLength(0)-1,n]=1;var accepted=false;
                for(var attempt=0;attempt<6&&!accepted;attempt++)
                {
                    var step=InequalityLeastSquaresStep.Solve(jac,residual,a,b,lambda);
                    for(var fraction=1.0;fraction>=1.0/128;fraction/=2)
                    {
                        var candidate=layers.ToArray();for(var j=0;j<n;j++)candidate[indices[j]]=candidate[indices[j]] with{ThicknessNm=Math.Clamp(candidate[indices[j]].ThicknessNm+fraction*step.Step[j]*units[j],d.Settings.MinimumThicknessNm,d.Settings.MaximumThicknessNm)};
                        var trial=snapshot.WithThicknesses(candidate.Select(f=>f.ThicknessNm).ToArray());var v=waves.Max(w=>1-trial.Evaluate(w,d.Target.AngleDegrees,d.Target.Polarization).Reflectance);if(v>=worst)continue;
                        layers=candidate;worst=v;accepted=true;lambda=Math.Max(1e-10,lambda/3);break;
                    }
                    if(!accepted)lambda=Math.Min(1e12,lambda*10);
                }
                trace.Add(new{Iteration=iteration+1,Loss=worst,Damping=lambda,Accepted=accepted});if(!accepted)break;
            }
            var certified=service.Evaluate(d with{Layers=layers,Result=null,Candidates=null});await ExperimentStore.SaveAsync(certified,Path.Combine(output,name+".coating.json"));
            summaries.Add(new{Name=name,Scope="Offline frozen-curvature trial; not product output or an acceptance-policy replacement",Shift=shift,InitialLoss=scale,FinalLoss=1-certified.Result!.Metrics.MinimumReflectance,Trace=trace});
            Console.WriteLine($"{name}: curvature {scale:G10} -> {1-certified.Result.Metrics.MinimumReflectance:G10}");
        }
        await File.WriteAllTextAsync(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(summaries,Experiment.JsonOptions));
    }
}
