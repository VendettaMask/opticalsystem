using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Scattering;

public static partial class XyzRadiometricCalibrator
{
    /// <summary>Fits positive, intensity-homogeneous empirical colour transfer. Neighbour
    /// count and the input domain are explicitly declared; held-out radiometric values
    /// never enter the stored model. No arbitrary XYZ spectrum recovery is claimed.</summary>
    public static XyzRadiometricFitResult FitPositiveColorTransfer(IReadOnlyList<XyzRadiometricObservation> observations,
        XyzCalibrationProvenance provenance, XyzCalibrationFitPolicy policy, XyzCalibrationGamut gamut,
        XyzColorimetricConditions conditions, int neighbourCount, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); using var scope = ComputationCancellation.Push(cancellationToken);
        // Reuse the original strict observation/partition/rank validation and its shared
        // SVD profile. Its linear coefficients are not used by this empirical model.
        var check = Fit(observations, provenance, policy, cancellationToken);
        gamut = gamut?.Freeze() ?? throw new ArgumentNullException(nameof(gamut)); conditions = conditions?.Freeze() ?? throw new ArgumentNullException(nameof(conditions));
        if (!gamut.SourceSha256.Equals(provenance.XyzSha256, StringComparison.OrdinalIgnoreCase) || observations.Any(p => !gamut.Contains(p.X, p.Y, p.Z))
            || observations.Any(p => p.WavelengthNanometers < conditions.MinimumWavelengthNanometers || p.WavelengthNanometers > conditions.MaximumWavelengthNanometers)) throw new InvalidDataException("XYZ 正值拟合的来源、颜色或波长不在声明条件内。");
        var waves = check.Bands.Select(b => b.WavelengthNanometers).ToArray();
        var nodes = new List<XyzPositiveColorNode>();
        foreach (var group in observations.Where(p => !p.Validation).GroupBy(p => p.SampleId).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            ComputationCancellation.ThrowIfCancellationRequested(); var p = group.First(); var sum = p.X + p.Y + p.Z;
            if (sum == 0) { if (group.Any(q => q.RadiometricBsdfPerSteradian > 0)) throw new InvalidDataException("训练中零XYZ对应非零辐射度，无法识别正值颜色映射。"); continue; }
            var ratios = group.OrderBy(q => q.WavelengthNanometers).Select(q => q.RadiometricBsdfPerSteradian / sum).ToArray();
            nodes.Add(new(group.Key, p.X / sum, p.Y / sum, p.Z / sum, ratios));
        }
        var model = new XyzPositiveColorModel(neighbourCount, nodes).Freeze(waves.Length);
        var predictions = observations.GroupBy(p => p.SampleId).ToDictionary(g => g.Key, g => { var p = g.First(); return model.Evaluate(p.X, p.Y, p.Z); }, StringComparer.Ordinal);
        var stats = new List<XyzCalibrationBandValidation>();
        for (var i = 0; i < waves.Length; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested(); var index = i; var reference = check.Evidence.Bands[i];
            (double Rms, double Max, double Relative) Metrics(bool validation)
            {
                var rows = observations.Where(p => p.WavelengthNanometers == waves[index] && p.Validation == validation).ToArray();
                var errors = rows.Select(p => predictions[p.SampleId][index] - p.RadiometricBsdfPerSteradian).ToArray();
                return (Math.Sqrt(errors.Sum(e => e * e) / Math.Max(rows.Sum(p => p.RadiometricBsdfPerSteradian * p.RadiometricBsdfPerSteradian), rows.Length * policy.RelativeSignalFloor * policy.RelativeSignalFloor)),
                    errors.Max(Math.Abs), errors.Select((e, k) => Math.Abs(e) / Math.Max(policy.RelativeSignalFloor, rows[k].RadiometricBsdfPerSteradian)).Max());
            }
            var training = Metrics(false); var validation = Metrics(true);
            if (new[] { training.Rms, validation.Rms, validation.Max, validation.Relative }.Any(v => !double.IsFinite(v))) throw new InvalidDataException("XYZ 正值拟合误差溢出。");
            stats.Add(reference with { TrainingNormalizedRms = training.Rms, ValidationNormalizedRms = validation.Rms, ValidationMaximumAbsoluteError = validation.Max,
                ValidationMaximumRelativeError = validation.Relative, Passed = reference.Rank == 3 && validation.Rms <= policy.MaximumValidationNormalizedRms && validation.Max <= policy.MaximumValidationAbsoluteError });
        }
        // For this explicitly marked model Bands carry wavelength axes; its nonnegative
        // ratios, not the unused zero linear coefficients, define the transfer function.
        var bands = Array.AsReadOnly(waves.Select(w => new XyzRadiometricBand(w, 0, 0, 0)).ToArray());
        return new(bands, new(provenance, policy, Array.AsReadOnly(stats.ToArray()), BandsDigest(bands, gamut, model, conditions), gamut, conditions, model.Digest()), model);
    }
}
