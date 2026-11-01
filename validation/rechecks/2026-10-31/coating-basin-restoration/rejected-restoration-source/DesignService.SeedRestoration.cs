using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    public const string SeedRestorationProtocol = "search-target-restoration/1";

    // A generated seed is a disposable search state, not a certified user design.
    // Its incidental feasible targets must not become new hard constraints. The
    // original design supplies the retained quality floor; formal output selection
    // remains outside this unconstrained-in-target-space (box-bounded) restoration.
    private (FilmLayer[] Layers, double Merit, RunRecord Run) RefineGeneralSearchSeed(Experiment d,
        IReadOnlyDictionary<string, IMaterial> materials, double[] initialGrid, int maximumIterations,
        Experiment? incumbent, IProgress<DesignProgress>? progress, CancellationToken token, bool certifyProgress = false)
    {
        var work = new GeneralCounter(); var indices = Enumerable.Range(0, d.Layers.Length).Where(i => d.Layers[i].Variable).ToArray();
        var units = indices.Select(i => CoherentThinFilmSolver.CharacteristicThickness(materials[d.Layers[i].MaterialId], materials[d.IncidentId], d.Target.CenterNm, d.Target.AngleDegrees)).ToArray();
        var phase = indices.Select((i,j) => d.Layers[i].ThicknessNm / units[j]).ToArray(); var originalPhase = phase.ToArray();
        var grid = GeneralGrid(d, initialGrid); var opticalSnapshot = Solver(d, materials); var t = d.Target;
        var minimumT = Math.Max(t.PeakTransmittance, incumbent?.Result?.Metrics.PeakTransmittance ?? 0);
        var widthTolerance = Math.Min(t.FwhmToleranceNm, incumbent?.Result?.Metrics.FwhmNanometers is { } w ? Math.Abs(w-t.FwhmNm) : double.PositiveInfinity);
        var minimumOd = t.StopBands!.Select(b => Math.Max(t.BlockingOd, incumbent is null ? t.BlockingOd : MinimumOd(incumbent.Result!.Spectrum,b))).ToArray();
        FilmLayer[] Layers()
        {
            var layers = d.Layers.ToArray();
            for (var j=0;j<indices.Length;j++) layers[indices[j]]=layers[indices[j]] with
            { ThicknessNm=phase[j]==originalPhase[j]?d.Layers[indices[j]].ThicknessNm:Math.Clamp(phase[j]*units[j],d.Settings.MinimumThicknessNm,d.Settings.MaximumThicknessNm) };
            return layers;
        }
        var best=d.Layers; var bestPhase=phase.ToArray(); var bestMerit=double.PositiveInfinity; long evaluations=0; var iterations=0; var remaining=maximumIterations; var reason="IterationLimit";
        double Shift(GeneralConstraintKey key) => key.Group switch
        {
            0 => (minimumT-t.PeakTransmittance)/t.PeakTransmittance,
            -2 or -3 => (t.FwhmToleranceNm-widthTolerance)/t.FwhmNm,
            >=6 => (minimumOd[key.Group-6]-t.BlockingOd)/Math.Max(1,t.BlockingOd),
            _ => 0
        };
        while(remaining>0 && indices.Length>0 && d.Settings.MinimumThicknessNm<d.Settings.MaximumThicknessNm)
        {
            token.ThrowIfCancellationRequested(); work.Passes++;
            var problem=new OptimizationProblem(); problem.DisableBatching(); problem.EnableBoundActiveSet();
            for(var j=0;j<phase.Length;j++){var k=j;problem.AddVariable(new DelegateVariable("搜索起点相位 "+j,()=>phase[k],v=>phase[k]=v,d.Settings.MinimumThicknessNm/units[k],d.Settings.MaximumThicknessNm/units[k],stepHint:1));}
            var analytic=d.Settings.Optimizer=="Damped Least Squares"; GeneralSystem? cache=null; double[]? cachedPhase=null;
            GeneralSystem Current()
            {
                token.ThrowIfCancellationRequested(); if(cachedPhase is not null && cachedPhase.SequenceEqual(phase))return cache!;
                var layers=Layers(); cache=GeneralConstraintSystem(d,opticalSnapshot.WithThicknesses(layers.Select(l=>l.ThicknessNm).ToArray()),grid,analytic,work);cachedPhase=phase.ToArray();
                var merit=Enumerable.Range(0,cache.Values.Length).Sum(i=>Math.Pow(double.IsNegativeInfinity(cache.Values[i])?0:Math.Max(0,cache.Values[i]+Shift(cache.Keys[i])),2));
                if(merit<bestMerit){best=layers;bestPhase=phase.ToArray();bestMerit=merit;}return cache;
            }
            var initial=Current();
            double Residual(int i){var s=Current();return double.IsNegativeInfinity(s.Values[i])?0:Math.Max(0,s.Values[i]+Shift(s.Keys[i]));}
            for(var i=0;i<initial.Values.Length;i++){var k=i;problem.AddOperand(new Operand("原物理目标 "+i,0,1,()=>Residual(k)));}
            if(analytic)problem.SetValueJacobianEvaluator(vector=>
            {
                var saved=phase.ToArray();Array.Copy(vector.ToArray(),phase,phase.Length);
                try{var s=Current();var jacobian=new double[s.Values.Length,phase.Length];for(var i=0;i<s.Values.Length;i++)if(Residual(i)>0)for(var j=0;j<phase.Length;j++)jacobian[i,j]=s.Derivatives![i,indices[j]]*units[j];return jacobian;}
                finally{Array.Copy(saved,phase,phase.Length);}
            });
            var allowance=Math.Min(remaining,Math.Max(1,(int)Math.Ceiling(Math.Sqrt(maximumIterations))));
            var report=OptimizerCatalog.Create(d.Settings.Optimizer).Optimize(problem,allowance);
            iterations+=report.Iterations;remaining-=Math.Max(1,report.Iterations);evaluations+=report.FunctionEvaluations;work.Jacobians+=problem.AnalyticJacobianEvaluations;reason=report.StopReason;
            _=Current(); progress?.Report(new("原目标搜索起点恢复",evaluations,bestMerit));
            Array.Copy(bestPhase,phase,phase.Length);
            Experiment? certified=null;
            if(certifyProgress)
            {
                certified=Evaluate(d with{Layers=best,Result=null,Candidates=null,Tolerance=null},progress:progress,token:token);work.Dense++;
                if(certified.Result!.Passed&&certified.Result.Metrics.PeakTransmittance>=minimumT
                    &&certified.Result.Metrics.FwhmNanometers is {} width&&Math.Abs(width-t.FwhmNm)<=widthTolerance
                    &&t.StopBands!.Select((b,i)=>MinimumOd(certified.Result.Spectrum,b)>=minimumOd[i]).All(v=>v))break;
            }
            else if(bestMerit==0)break;
            if(remaining<=0)break;
            var guideSolver=opticalSnapshot.WithThicknesses(best.Select(l=>l.ThicknessNm).ToArray());
            var refined=AdaptiveThinFilmSpectrum.Sample(guideSolver,grid,t.AngleDegrees,t.Polarization,
                Math.Min(t.CenterToleranceNm,t.FwhmToleranceNm)/t.FwhmNm/16,Math.Max(grid.Length,Math.Min(2048,grid.Length*2)));
            work.Spectrum+=refined.Evaluations;work.Refinements++;work.Limit|=refined.LimitReached;
            var anchors=new List<double>();
            if(certified is not null)
            {
                var metric=certified.Result!.Metrics;anchors.Add(metric.PeakWavelengthNanometers);
                if(metric.LeftHalfMaximumNanometers is {} left)anchors.Add(left);if(metric.RightHalfMaximumNanometers is {} right)anchors.Add(right);
                foreach(var band in new[]{new SpectralBand(Math.Max(t.MinimumNm,t.CenterNm-2*t.FwhmNm),Math.Min(t.MaximumNm,t.CenterNm+2*t.FwhmNm))}.Concat(t.StopBands!))
                {
                    var local=certified.Result.Spectrum.Where(s=>s.WavelengthNanometers>=band.MinimumNm&&s.WavelengthNanometers<=band.MaximumNm).ToArray();
                    var peak=ThinFilmSpectrum.RefineTransmissionPeak(guideSolver,local,t.AngleDegrees,t.Polarization);work.Spectrum+=peak.Evaluations;anchors.Add(peak.Peak.WavelengthNanometers);
                }
            }
            grid=GeneralGrid(d,refined.Samples.Select(s=>s.WavelengthNanometers).Concat(anchors));
            bestMerit=double.PositiveInfinity;
        }
        var final=GeneralConstraintSystem(d,opticalSnapshot.WithThicknesses(best.Select(l=>l.ThicknessNm).ToArray()),grid,false,work); evaluations++;
        return(best,AssessGeneral(d,final.Samples).Merit,new(d.Settings.Optimizer,GeneralCoatingProtocol,"SearchSeed:"+reason,evaluations,iterations,d.Settings.RandomSeed)
        { DesignProtocol=GeneralCoatingProtocol,GeneralWork=work.Snapshot(),SearchSeedRestorationProtocol=SeedRestorationProtocol });
    }

    private static bool WithinSearchQuality(Experiment candidate, Experiment? incumbent)
    {
        if(incumbent is null)return true;
        var a=incumbent.Result!;var b=candidate.Result!;var t=incumbent.Target;
        return(!a.SamplingConverged||b.SamplingConverged)&&(!a.Passed||b.Passed)
            && b.Metrics.PeakTransmittance>=a.Metrics.PeakTransmittance
            && (a.Metrics.FwhmNanometers is not { } width||b.Metrics.FwhmNanometers is { } next&&Math.Abs(next-t.FwhmNm)<=Math.Abs(width-t.FwhmNm))
            && t.StopBands!.All(band=>MinimumOd(b.Spectrum,band)>=MinimumOd(a.Spectrum,band));
    }
}
