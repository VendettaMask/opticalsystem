using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

internal static class CurvatureAudit
{
    internal static async Task Run(string directory,string output)
    {
        var rows=new List<object>();
        foreach(var path in Directory.GetFiles(directory,"*.coating.json").Order(StringComparer.Ordinal))
        {
            var d=await ExperimentStore.OpenAsync(path);if(d.Target.Kind!=DesignKind.HighReflector)continue;
            var indices=Enumerable.Range(0,d.Layers.Length).Where(i=>d.Layers[i].Variable).ToArray();if(indices.Length==0)continue;
            var mats=d.Materials.ToDictionary(m=>m.Id,m=>m.Resolve(new MaterialRegistry()));var t=d.Target;var n=indices.Length;
            var units=indices.Select(i=>CoherentThinFilmSolver.CharacteristicThickness(mats[d.Layers[i].MaterialId],mats[d.IncidentId],t.CenterNm,t.AngleDegrees)).ToArray();
            var thickness=d.Layers.Select(l=>l.ThicknessNm).ToArray();var solver=new CoherentThinFilmSolver(mats[d.IncidentId],mats[d.SubstrateId],d.Layers.Select(l=>new CoherentFilm(mats[l.MaterialId],l.ThicknessNm)));
            var spectrum=d.Result!.Spectrum;var losses=spectrum.Select(s=>1-s.Power.Reflectance).ToArray();var worst=losses.Max();
            var active=Enumerable.Range(0,losses.Length).Where(i=>(i==0||losses[i]>=losses[i-1])&&(i==losses.Length-1||losses[i]>=losses[i+1])&&worst-losses[i]<=1e-5).Select(i=>spectrum[i].WavelengthNanometers).ToArray();if(active.Length>100)continue;
            double[] Gradient(double wavelength,double[] values){var s=solver.WithThicknesses(values).EvaluateThicknessSensitivity(wavelength,t.AngleDegrees,t.Polarization);return indices.Select((i,j)=>-s.Layers[i].Reflectance*units[j]).ToArray();}
            var gradients=active.Select(w=>Gradient(w,thickness)).ToArray();var jac=new double[n,active.Length];var residual=new double[n];
            for(var i=0;i<active.Length;i++)for(var j=0;j<n;j++){jac[j,i]=gradients[i][j];residual[j]+=gradients[i][j]/active.Length;}
            var normals=new double[active.Length+2,active.Length];var bounds=new double[active.Length+2];
            for(var i=0;i<active.Length;i++){normals[i,i]=-1;bounds[i]=1.0/active.Length;normals[active.Length,i]=1;normals[active.Length+1,i]=-1;}
            var step=InequalityLeastSquaresStep.Solve(jac,residual,normals,bounds,1e-8);var weights=step.Step.Select(v=>v+1.0/active.Length).ToArray();
            var hessians=new List<double[][]>();var hessian=new double[n,n];
            for(var k=0;k<active.Length;k++)
            {
                var h=new double[n,n];
                for(var j=0;j<n;j++)
                {
                    var delta=Math.Pow(Math.BitIncrement(1.0)-1,1.0/3)*Math.Max(1,thickness[indices[j]]/units[j]);
                    var forward=(d.Settings.MaximumThicknessNm-thickness[indices[j]])/units[j];var backward=(thickness[indices[j]]-d.Settings.MinimumThicknessNm)/units[j];
                    if(forward>=delta&&backward>=delta)
                    {
                        var plus=thickness.ToArray();var minus=thickness.ToArray();plus[indices[j]]+=delta*units[j];minus[indices[j]]-=delta*units[j];
                        var a=Gradient(active[k],plus);var b=Gradient(active[k],minus);for(var i=0;i<n;i++)h[i,j]=(a[i]-b[i])/(2*delta);
                    }
                    else
                    {
                        var sign=forward>=backward?1:-1;delta=Math.Min(delta,Math.Max(forward,backward)/2);if(delta<=0)continue;
                        var first=thickness.ToArray();var second=thickness.ToArray();first[indices[j]]+=sign*delta*units[j];second[indices[j]]+=2*sign*delta*units[j];
                        var a=Gradient(active[k],first);var b=Gradient(active[k],second);for(var i=0;i<n;i++)h[i,j]=sign*(-3*gradients[k][i]+4*a[i]-b[i])/(2*delta);
                    }
                }
                var values=new double[n][];for(var i=0;i<n;i++){values[i]=new double[n];for(var j=0;j<n;j++){values[i][j]=(h[i,j]+h[j,i])/2;hessian[i,j]+=weights[k]*values[i][j];}}
                hessians.Add(values);
            }
            var g=new double[active.Length,n];for(var i=0;i<active.Length;i++)for(var j=0;j<n;j++)g[i,j]=gradients[i][j];var projector=InequalityLeastSquaresStep.TangentProjector(g);
            static double[] Multiply(double[,] m,double[] v)=>Enumerable.Range(0,v.Length).Select(i=>Enumerable.Range(0,v.Length).Sum(j=>m[i,j]*v[j])).ToArray();
            double[] H(double[] v)=>Multiply(projector,Multiply(hessian,Multiply(projector,v)));
            var shift=Enumerable.Range(0,n).Max(i=>Enumerable.Range(0,n).Sum(j=>Math.Abs(hessian[i,j])));var random=new Random(d.Settings.RandomSeed);var vector=Multiply(projector,Enumerable.Range(0,n).Select(_=>random.NextDouble()-.5).ToArray());
            for(var iteration=0;iteration<32*n;iteration++){var hv=H(vector);var next=vector.Select((v,i)=>shift*v-hv[i]).ToArray();var norm=Math.Sqrt(next.Sum(v=>v*v));if(norm==0)break;vector=next.Select(v=>v/norm).ToArray();}
            var projected=H(vector);var curvature=projected.Zip(vector,(a,b)=>a*b).Sum();var combined=Enumerable.Range(0,n).Select(j=>Enumerable.Range(0,active.Length).Sum(i=>weights[i]*gradients[i][j])).ToArray();
            double[] Index(string id,double w)=>[mats[id].RefractiveIndex(w),mats[id].ExtinctionCoefficient(w)];
            rows.Add(new{d.Name,WorstLoss=worst,ActiveWavelengths=active,Weights=weights,GradientNorm=Math.Sqrt(combined.Sum(v=>v*v)),Curvature=curvature,Direction=vector,Units=units,Indices=indices,Hessians=hessians,
                Points=active.Select(w=>new{Wavelength=w,Angle=t.AngleDegrees,Polarization=t.Polarization.ToString(),N0=Index(d.IncidentId,w),Ns=Index(d.SubstrateId,w),Layers=d.Layers.Select(l=>new{N=Index(l.MaterialId,w),D=l.ThicknessNm})}).ToArray()});
            Console.WriteLine($"{d.Name}: active={active.Length}; gradient={Math.Sqrt(combined.Sum(v=>v*v)):G6}; curvature={curvature:G8}");
        }
        await File.WriteAllTextAsync(output,JsonSerializer.Serialize(rows,Experiment.JsonOptions));
    }
}
