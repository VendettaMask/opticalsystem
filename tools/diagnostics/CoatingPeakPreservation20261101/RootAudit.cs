using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

internal static class RootAudit
{
    private static readonly JsonSerializerOptions Json = Experiment.JsonOptions;
    private static double[] Od(Experiment d) => (d.Target.StopBands ?? []).Select(b => d.Result!.Spectrum
        .Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity)).ToArray();
    private static double[] Metrics(Experiment d) => [d.Result!.Metrics.PeakTransmittance, d.Result.Metrics.FwhmNanometers ?? double.NaN,
        d.Result.Metrics.PeakWavelengthNanometers, .. Od(d)];
    public static async Task Main(string[] args)
    {
        var mode = args[0]; var service = new DesignService(); var rows = new List<object>(); var pointRows = new List<object>();
        foreach (var file in Directory.GetFiles(args[1], "*.coating.json").Order(StringComparer.Ordinal))
        {
            var source = await ExperimentStore.OpenAsync(file);
            var d = source with { Result = null, Candidates = null, Tolerance = null }; var t = d.Target;
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15)); var token = deadline.Token;
            var baseline = service.Evaluate(d, token: token); var spectrum = baseline.Result!.Spectrum;
            var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
            var solver = new CoherentThinFilmSolver(materials[d.IncidentId], materials[d.SubstrateId], d.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
            var peak = t.Kind == DesignKind.HighReflector ? spectrum.MinBy(s => s.Power.Reflectance)!
                : spectrum.Where(s => Math.Abs(s.WavelengthNanometers-t.CenterNm) <= 2*t.FwhmNm).MaxBy(s => s.Power.LogTransmittance)!;
            double[] Index(string id, double w) => [materials[id].RefractiveIndex(w), materials[id].ExtinctionCoefficient(w)];
            if (mode == "profiles")
            {
                var waves = new[] { peak.WavelengthNanometers, t.CenterNm, t.MinimumNm, t.MaximumNm }.Distinct();
                foreach (var wave in waves) foreach (var pol in Enum.GetValues<ThinFilmPolarization>())
                {
                    var sensitivity = solver.EvaluateThicknessSensitivity(wave,t.AngleDegrees,pol);
                    pointRows.Add(new { File=Path.GetFileName(file),d.Name,Lambda=wave,Angle=t.AngleDegrees,Pol=pol.ToString(),N0=Index(d.IncidentId,wave),Ns=Index(d.SubstrateId,wave),
                        Layers=d.Layers.Select(l=>new{N=Index(l.MaterialId,wave),D=l.ThicknessNm}),Core=sensitivity.Power,Derivatives=sensitivity.Layers });
                }
                rows.Add(new { File=Path.GetFileName(file),d.Name,Point=peak,baseline.Result.Metrics,baseline.Result.Checks,baseline.Result.Passed,
                    MinimumAbsorptionReductionWithZeroReflection=t.Kind==DesignKind.NarrowBand?Math.Max(0,peak.Power.Absorptance-(1-t.PeakTransmittance)):Math.Max(0,peak.Power.Absorptance-(1-t.Reflectance)),
                    MaterialColumns=new[]{d.LowId,d.HighId}.Select(id=>new{Id=id,NK=Index(id,peak.WavelengthNanometers),TotalThicknessNm=d.Layers.Where(l=>l.MaterialId==id).Sum(l=>l.ThicknessNm)}),
                    Scope="Exact current stack loss allocation; no global feasibility bound or changed n/k" });
            }
            else if (mode == "directions")
            {
                var indices=Enumerable.Range(0,d.Layers.Length).Where(i=>d.Layers[i].Variable).ToArray();
                var units=indices.Select(i=>CoherentThinFilmSolver.CharacteristicThickness(materials[d.Layers[i].MaterialId],materials[d.IncidentId],t.CenterNm,t.AngleDegrees)).ToArray();
                var anchor=Metrics(baseline);var checks=new List<object>(); var gradients=new List<double[,]>();
                var pointSensitivity=solver.EvaluateThicknessSensitivity(peak.WavelengthNanometers,t.AngleDegrees,t.Polarization);
                foreach(var h in new[]{1e-5,5e-6})
                {
                    var jac=new double[anchor.Length,indices.Length];
                    for(var j=0;j<indices.Length;j++)
                    {
                        var i=indices[j];var plus=d.Layers.ToArray();var minus=d.Layers.ToArray();var delta=h*units[j];
                        if(d.Layers[i].ThicknessNm-delta<d.Settings.MinimumThicknessNm||d.Layers[i].ThicknessNm+delta>d.Settings.MaximumThicknessNm)
                            throw new InvalidOperationException("Central derivative meets a physical bound; use a one-sided audit for this stack.");
                        plus[i]=plus[i] with{ThicknessNm=plus[i].ThicknessNm+delta};minus[i]=minus[i] with{ThicknessNm=minus[i].ThicknessNm-delta};
                        var a=Metrics(service.Evaluate(d with{Layers=plus},token:token));var b=Metrics(service.Evaluate(d with{Layers=minus},token:token));
                        for(var k=0;k<anchor.Length;k++)jac[k,j]=(a[k]-b[k])/(2*h);
                        var p=solver.WithThicknesses(plus.Select(l=>l.ThicknessNm).ToArray()).Evaluate(peak.WavelengthNanometers,t.AngleDegrees,t.Polarization);
                        var m=solver.WithThicknesses(minus.Select(l=>l.ThicknessNm).ToArray()).Evaluate(peak.WavelengthNanometers,t.AngleDegrees,t.Polarization);
                        checks.Add(new{Layer=i,PhaseStep=h,AnalyticPeakT=pointSensitivity.Layers[i].Transmittance*units[j],FixedWaveFiniteT=(p.Transmittance-m.Transmittance)/(2*h),MovingPeakFiniteT=jac[0,j]});
                    }
                    gradients.Add(jac);
                }
                var directions=new List<object>();var gradient=gradients[^1];var n=indices.Length;
                foreach(var radius in new[]{.001,.01,.05})
                {
                    var objective=new double[1,n];for(var j=0;j<n;j++)objective[0,j]=gradient[0,j];
                    var constraints=new double[1+2+(anchor.Length-3)+2*n,n];var upper=new double[constraints.GetLength(0)];
                    var widthSign=Math.Sign(anchor[1]-t.FwhmNm);var row=0;
                    for(var j=0;j<n;j++)constraints[row,j]=widthSign*gradient[1,j];row++;
                    foreach(var sign in new[]{-1,1})
                    {for(var j=0;j<n;j++)constraints[row,j]=sign*gradient[2,j];upper[row]=Math.Max(0,t.CenterToleranceNm-sign*(anchor[2]-t.CenterNm));row++;}
                    for(var band=3;band<anchor.Length;band++,row++)for(var j=0;j<n;j++)constraints[row,j]=-gradient[band,j];
                    for(var j=0;j<n;j++)
                    {constraints[row,j]=1;upper[row++]=Math.Min(radius,(d.Settings.MaximumThicknessNm-d.Layers[indices[j]].ThicknessNm)/units[j]);constraints[row,j]=-1;upper[row++]=Math.Min(radius,(d.Layers[indices[j]].ThicknessNm-d.Settings.MinimumThicknessNm)/units[j]);}
                    var step=InequalityLeastSquaresStep.Solve(objective,[anchor[0]-t.PeakTransmittance],constraints,upper,.001);
                    var trials=new List<object>();
                    foreach(var fraction in new[]{1.0,.5,.25,.125,.0625,.03125,.015625,.0078125})
                    {
                        var layers=d.Layers.ToArray();for(var j=0;j<n;j++)layers[indices[j]]=layers[indices[j]] with{ThicknessNm=layers[indices[j]].ThicknessNm+fraction*step.Step[j]*units[j]};
                        var actual=service.Evaluate(d with{Layers=layers},token:token);var metrics=Metrics(actual);
                        var joint=metrics[0]>=anchor[0]&&Math.Abs(metrics[1]-t.FwhmNm)<=Math.Abs(anchor[1]-t.FwhmNm)&&metrics.Skip(3).Zip(anchor.Skip(3)).All(p=>p.First>=p.Second)
                            &&Math.Abs(metrics[2]-t.CenterNm)<=t.CenterToleranceNm&&actual.Result!.SamplingConverged;
                        trials.Add(new{Fraction=fraction,Metrics=metrics,Joint=joint,actual.Result!.Passed,Layers=layers});
                    }
                    directions.Add(new{Radius=radius,LinearPredictedTGain=Enumerable.Range(0,n).Sum(j=>gradient[0,j]*step.Step[j]),step.Converged,step.MaximumViolation,Step=step.Step,Trials=trials});
                }
                var discrepancy=Enumerable.Range(0,anchor.Length).Select(k=>Enumerable.Range(0,n).Max(j=>Math.Abs(gradients[0][k,j]-gradients[1][k,j]))).ToArray();
                rows.Add(new{File=Path.GetFileName(file),d.Name,Anchor=anchor,DerivativeChecks=checks,FiniteDifferenceStepDiscrepancy=discrepancy,Directions=directions,
                    Scope="Local finite-difference feasible-cone audit at original actual width/OD bounds; no global infeasibility certificate"});
            }
            else if(mode=="periodic")
            {
                if(t.Kind!=DesignKind.HighReflector)continue;
                var family=new List<object>();
                foreach(var highFirst in new[]{true,false})for(var pairs=1;2*pairs<=t.MaximumLayers;pairs++)
                {
                    var layers=Enumerable.Range(0,2*pairs).Select(i=>
                    {var id=(i%2==0)==highFirst?d.HighId:d.LowId;return new FilmLayer(id,CoherentThinFilmSolver.QuarterWaveThickness(materials[id],materials[d.IncidentId],t.CenterNm,t.AngleDegrees));}).ToArray();
                    if(layers.Any(l=>l.ThicknessNm<d.Settings.MinimumThicknessNm||l.ThicknessNm>d.Settings.MaximumThicknessNm))continue;
                    var actual=service.Evaluate(d with{Layers=layers},token:token);var point=actual.Result!.Spectrum.MinBy(s=>s.Power.Reflectance)!;
                    family.Add(new{HighFirst=highFirst,Pairs=pairs,Layers=layers.Length,Point=point,actual.Result.Passed,actual.Result.SamplingConverged});
                }
                rows.Add(new{d.Name,Family=family,Scope="Periodic reference family inside original layer/thickness gates; not a global bound on all topologies"});
            }
            else throw new ArgumentException("Unknown mode");
            await File.WriteAllTextAsync(args[2],JsonSerializer.Serialize(rows,Json));
            if(mode=="profiles")await File.WriteAllTextAsync(args[2]+".points.json",JsonSerializer.Serialize(pointRows,Json));
            Console.WriteLine(mode+" "+d.Name+": done");
        }
    }
}
