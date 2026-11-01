using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Scattering;

public static partial class XyzRadiometricCalibrator
{
    /// <summary>Explicit absolute-XYZ empirical fit; held-out targets are used only for scoring.
    /// The caller chooses one general feature/regularization configuration for the run.</summary>
    public static XyzRadiometricFitResult FitAbsoluteRbf(IReadOnlyList<XyzRadiometricObservation> observations,
        XyzCalibrationProvenance provenance,XyzCalibrationFitPolicy policy,XyzCalibrationGamut gamut,XyzColorimetricConditions conditions,
        XyzAbsoluteRbfFeatures features,int neighbourCount,double smoothing,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();using var scope=ComputationCancellation.Push(cancellationToken);
        var check=Fit(observations,provenance,policy,cancellationToken);gamut=gamut.Freeze();conditions=conditions.Freeze();
        if(!gamut.SourceSha256.Equals(provenance.XyzSha256,StringComparison.OrdinalIgnoreCase)||observations.Any(p=>!gamut.Contains(p.X,p.Y,p.Z)||p.WavelengthNanometers<conditions.MinimumWavelengthNanometers||p.WavelengthNanometers>conditions.MaximumWavelengthNanometers))throw new InvalidDataException("绝对XYZ拟合来源、颜色或波长超出声明域。");
        var nodes=new List<XyzAbsoluteRbfNode>();
        foreach(var group in observations.Where(p=>!p.Validation).GroupBy(p=>p.SampleId).OrderBy(g=>g.Key,StringComparer.Ordinal))
        {
            var p=group.First();if(p.X+p.Y+p.Z==0){if(group.Any(q=>q.RadiometricBsdfPerSteradian>0))throw new InvalidDataException("零XYZ对应非零训练辐射度。");continue;}
            nodes.Add(new(p.SampleId,p.X,p.Y,p.Z,group.OrderBy(q=>q.WavelengthNanometers).Select(q=>q.RadiometricBsdfPerSteradian).ToArray()));
        }
        var raw=nodes.Select(p=>XyzAbsoluteRbfModel.RawFeatures(features,p.X,p.Y,p.Z)).ToArray();
        var scales=Enumerable.Range(0,3).Select(j=>{var mean=raw.Average(p=>p[j]);return Math.Sqrt(raw.Average(p=>(p[j]-mean)*(p[j]-mean)));}).ToArray();
        var strengths=observations.Select(p=>p.X+p.Y+p.Z).Where(s=>s>0).ToArray();
        var model=new XyzAbsoluteRbfModel(features,neighbourCount,smoothing,policy.RelativeSignalFloor,scales,strengths.Min(),strengths.Max(),nodes).Freeze(check.Bands.Count);
        var predictions=new Dictionary<string,double[]>(StringComparer.Ordinal);
        foreach(var group in observations.GroupBy(p=>p.SampleId)){ComputationCancellation.ThrowIfCancellationRequested();var p=group.First();predictions.Add(group.Key,model.Evaluate(p.X,p.Y,p.Z));}
        var stats=new List<XyzCalibrationBandValidation>();
        for(int i=0;i<check.Bands.Count;i++)
        {
            var wave=check.Bands[i].WavelengthNanometers;
            (double Rms,double Maximum,double Relative) Metrics(bool validation)
            {
                var rows=observations.Where(p=>p.WavelengthNanometers==wave&&p.Validation==validation).ToArray();var errors=rows.Select(p=>predictions[p.SampleId][i]-p.RadiometricBsdfPerSteradian).ToArray();
                return(Math.Sqrt(errors.Sum(e=>e*e)/Math.Max(rows.Sum(p=>p.RadiometricBsdfPerSteradian*p.RadiometricBsdfPerSteradian),rows.Length*policy.RelativeSignalFloor*policy.RelativeSignalFloor)),errors.Max(Math.Abs),errors.Select((e,j)=>Math.Abs(e)/Math.Max(policy.RelativeSignalFloor,rows[j].RadiometricBsdfPerSteradian)).Max());
            }
            var train=Metrics(false);var validation=Metrics(true);if(new[]{train.Rms,validation.Rms,validation.Maximum,validation.Relative}.Any(v=>!double.IsFinite(v)))throw new InvalidDataException("绝对XYZ模型误差溢出。");
            stats.Add(check.Evidence.Bands[i] with{TrainingNormalizedRms=train.Rms,ValidationNormalizedRms=validation.Rms,ValidationMaximumAbsoluteError=validation.Maximum,ValidationMaximumRelativeError=validation.Relative,Passed=check.Evidence.Bands[i].Rank==3&&validation.Rms<=policy.MaximumValidationNormalizedRms&&validation.Maximum<=policy.MaximumValidationAbsoluteError});
        }
        var bands=Array.AsReadOnly(check.Bands.Select(b=>new XyzRadiometricBand(b.WavelengthNanometers,0,0,0)).ToArray());
        return new(bands,new(provenance,policy,Array.AsReadOnly(stats.ToArray()),BandsDigest(bands,gamut,conditions:conditions,absoluteRbf:model),gamut,conditions,AbsoluteRbfModelSha256:model.Digest()),AbsoluteRbfModel:model);
    }
}
