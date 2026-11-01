using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Scattering;

public enum XyzCalibrationDataKind { SyntheticControl, PublishedPairedMeasurements, SpectralMeasurementsWithDerivedXyz }
public sealed record XyzCalibrationProvenance(XyzCalibrationDataKind Kind, string Source, string XyzSha256, string RadiometricSha256, bool ObserverAndIlluminantVerified);
public sealed record XyzRadiometricObservation(string SampleId, double WavelengthNanometers, double X, double Y, double Z, double RadiometricBsdfPerSteradian, bool Validation);
/// <summary>Caller-declared model-validation policy, not a commercial software accuracy specification.</summary>
public sealed record XyzCalibrationFitPolicy(double MaximumValidationNormalizedRms, double MaximumValidationAbsoluteError, double RelativeSignalFloor);
public sealed record XyzCalibrationBandValidation(double WavelengthNanometers, int TrainingCount, int ValidationCount, int Rank,
    double ConditionNumber, double TrainingNormalizedRms, double ValidationNormalizedRms, double ValidationMaximumAbsoluteError, double ValidationMaximumRelativeError, bool Passed);
public sealed record XyzCalibrationFitEvidence(XyzCalibrationProvenance Provenance, XyzCalibrationFitPolicy Policy, IReadOnlyList<XyzCalibrationBandValidation> Bands, string CalibrationBandsSha256,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] XyzCalibrationGamut? Gamut = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] XyzColorimetricConditions? Conditions = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? PositiveModelSha256 = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? AbsoluteRbfModelSha256 = null)
{
    internal XyzCalibrationFitEvidence Freeze()
    {
        XyzRadiometricCalibrator.Validate(Provenance, Policy);
        if (CalibrationBandsSha256 is not { Length: 64 } || CalibrationBandsSha256.Any(c => !Uri.IsHexDigit(c)) || Bands is null || Bands.Count is < 1 or > 32 || Bands.Any(b => b is null) || Bands.Select(b => b.WavelengthNanometers).Distinct().Count() != Bands.Count || Bands.Any(b => b.TrainingCount < 3 || b.ValidationCount < 1 || b.Rank is < 0 or > 3
            || !double.IsFinite(b.WavelengthNanometers) || b.WavelengthNanometers is < 200 or > 20000 || !double.IsFinite(b.ConditionNumber) || b.ConditionNumber < 1
            || new[] { b.TrainingNormalizedRms, b.ValidationNormalizedRms, b.ValidationMaximumAbsoluteError, b.ValidationMaximumRelativeError }.Any(v => !double.IsFinite(v) || v < 0)
            || b.Passed != (b.Rank == 3 && b.ValidationNormalizedRms <= Policy.MaximumValidationNormalizedRms && b.ValidationMaximumAbsoluteError <= Policy.MaximumValidationAbsoluteError))) throw new InvalidDataException("XYZ 校准验证证据无效。");
        var gamut = Gamut?.Freeze();
        if (gamut is not null && !gamut.SourceSha256.Equals(Provenance.XyzSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("XYZ 适用域与校准输入来源不一致。");
        if (PositiveModelSha256 is not null && (PositiveModelSha256.Length != 64 || PositiveModelSha256.Any(c => !Uri.IsHexDigit(c)) || gamut is null || Conditions is null)) throw new InvalidDataException("XYZ 正值颜色模型缺少域、条件或模型摘要。");
        if (AbsoluteRbfModelSha256 is not null && (AbsoluteRbfModelSha256.Length!=64||AbsoluteRbfModelSha256.Any(c=>!Uri.IsHexDigit(c))||PositiveModelSha256 is not null||gamut is null||Conditions is null))throw new InvalidDataException("绝对XYZ模型缺少独立域/条件摘要或模型冲突。");
        if (PositiveModelSha256 is null && AbsoluteRbfModelSha256 is null && Conditions is not null) throw new InvalidDataException("显式光谱条件需要与正值模型摘要绑定。");
        return this with { Bands = Array.AsReadOnly(Bands.OrderBy(b => b.WavelengthNanometers).ToArray()), Gamut = gamut, Conditions = Conditions?.Freeze() };
    }
}
public sealed record XyzRadiometricFitResult(IReadOnlyList<XyzRadiometricBand> Bands, XyzCalibrationFitEvidence Evidence,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] XyzPositiveColorModel? PositiveModel = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] XyzAbsoluteRbfModel? AbsoluteRbfModel = null)
{
    public bool ModelValidationPassed => Evidence.Bands.Count > 0 && Evidence.Bands.All(b => b.Passed);
    public XyzRadiometricCalibration CreateCalibration(string name, string observer, string illuminant)
    {
        var evidence = Evidence.Freeze();
        if (!ModelValidationPassed || !evidence.Provenance.ObserverAndIlluminantVerified || Bands is null || Bands.Count != evidence.Bands.Count || Bands.Any(b => b is null)
            || XyzRadiometricCalibrator.BandsDigest(Bands, evidence.Gamut, PositiveModel, evidence.Conditions,AbsoluteRbfModel) != evidence.CalibrationBandsSha256
            || PositiveModel?.Digest() != evidence.PositiveModelSha256
            || AbsoluteRbfModel?.Digest()!=evidence.AbsoluteRbfModelSha256
            || AbsoluteRbfModel is not null&&(PositiveModel is not null||observer!=evidence.Conditions?.Observer||illuminant!=evidence.Conditions?.Illuminant)
            || PositiveModel is not null && (observer != evidence.Conditions?.Observer || illuminant != evidence.Conditions?.Illuminant)) throw new InvalidDataException("XYZ 校准尚未通过独立留出验证或测量条件未核实，不能导出为可计算校准。");
        return new(name, observer, illuminant, XyzRadiometricCalibration.UnitsContract, Bands, FitEvidence: evidence, PositiveModel: PositiveModel,AbsoluteRbfModel:AbsoluteRbfModel);
    }
}

/// <summary>Fits a nonnegative three-channel radiometric transfer using exhaustive active sets and
/// shared SVD. Training and validation share no sample identity. Measurements are never clipped,
/// interpolated, relabelled or replaced to make a fit pass.</summary>
public static partial class XyzRadiometricCalibrator
{
    public const int MaximumObservations = 65536;
    internal static string BandsDigest(IReadOnlyList<XyzRadiometricBand> bands, XyzCalibrationGamut? gamut = null, XyzPositiveColorModel? positive = null, XyzColorimetricConditions? conditions = null,XyzAbsoluteRbfModel? absoluteRbf=null)
    {
        using var memory = new MemoryStream(); using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true))
        { writer.Write(absoluteRbf is not null?"XyzRadiometricAbsoluteRbfBands/1":positive is not null ? "XyzRadiometricPositiveFitBands/1" : gamut is null ? "XyzRadiometricFitBands/1" : "XyzRadiometricGamutFitBands/1");
            if(absoluteRbf is not null){writer.Write(absoluteRbf.Digest());writer.Write(System.Text.Json.JsonSerializer.Serialize(conditions));}
            if (positive is not null) { writer.Write(positive.Digest()); writer.Write(System.Text.Json.JsonSerializer.Serialize(conditions)); }
            if (gamut is not null) { writer.Write(gamut.Source); writer.Write(gamut.SourceSha256); writer.Write(gamut.Vertices.Count); foreach (var p in gamut.Vertices) { writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); } }
            foreach (var b in bands.OrderBy(b => b.WavelengthNanometers)) { writer.Write(b.WavelengthNanometers); writer.Write(b.X); writer.Write(b.Y); writer.Write(b.Z); } }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(memory.ToArray())).ToLowerInvariant();
    }
    internal static void Validate(XyzCalibrationProvenance p, XyzCalibrationFitPolicy policy)
    {
        static bool Sha(string s) => s is { Length: 64 } && s.All(Uri.IsHexDigit);
        if (p is null || !Enum.IsDefined(p.Kind) || string.IsNullOrWhiteSpace(p.Source) || p.Source.Length > 2048 || !Sha(p.XyzSha256) || !Sha(p.RadiometricSha256)
            || policy is null || !double.IsFinite(policy.MaximumValidationNormalizedRms) || policy.MaximumValidationNormalizedRms < 0
            || !double.IsFinite(policy.MaximumValidationAbsoluteError) || policy.MaximumValidationAbsoluteError < 0 || !double.IsFinite(policy.RelativeSignalFloor) || policy.RelativeSignalFloor <= 0)
            throw new InvalidDataException("XYZ 校准来源或显式验证策略无效。");
    }
    public static XyzRadiometricFitResult Fit(IReadOnlyList<XyzRadiometricObservation> observations, XyzCalibrationProvenance provenance,
        XyzCalibrationFitPolicy policy, CancellationToken cancellationToken = default)
        => FitCore(observations, provenance, policy, null, cancellationToken);
    public static XyzRadiometricFitResult FitWithinGamut(IReadOnlyList<XyzRadiometricObservation> observations, XyzCalibrationProvenance provenance,
        XyzCalibrationFitPolicy policy, XyzCalibrationGamut gamut, CancellationToken cancellationToken = default)
        => FitCore(observations, provenance, policy, gamut?.Freeze() ?? throw new ArgumentNullException(nameof(gamut)), cancellationToken);
    private static XyzRadiometricFitResult FitCore(IReadOnlyList<XyzRadiometricObservation> observations, XyzCalibrationProvenance provenance,
        XyzCalibrationFitPolicy policy, XyzCalibrationGamut? gamut, CancellationToken cancellationToken)
    {
        Validate(provenance, policy); cancellationToken.ThrowIfCancellationRequested(); using var scope = ComputationCancellation.Push(cancellationToken);
        if (observations is null || observations.Count is < 4 or > MaximumObservations || observations.Any(p => p is null || string.IsNullOrWhiteSpace(p.SampleId) || p.SampleId.Length > 256
            || !double.IsFinite(p.WavelengthNanometers) || p.WavelengthNanometers is < 200 or > 20000 || new[] { p.X, p.Y, p.Z, p.RadiometricBsdfPerSteradian }.Any(v => !double.IsFinite(v) || v is < 0 or > 1e12))) throw new InvalidDataException("XYZ 配对观测须为有限非负的归一化通道 BSDF 与辐射度 BSDF。");
        var rows = observations.ToArray();
        if (gamut is not null && (!gamut.SourceSha256.Equals(provenance.XyzSha256, StringComparison.OrdinalIgnoreCase) || rows.Any(p => !gamut.Contains(p.X, p.Y, p.Z))))
            throw new InvalidDataException("XYZ 校准样本在声明颜色域外，或颜色域来源不匹配；不能外推。");
        if (rows.Select(p => (p.SampleId, p.WavelengthNanometers)).Distinct().Count() != rows.Length) throw new InvalidDataException("XYZ 校准样本/波长重复。");
        foreach (var sample in rows.GroupBy(p => p.SampleId))
        {
            var p = sample.First();
            if (sample.Any(q => q.Validation != p.Validation || q.X != p.X || q.Y != p.Y || q.Z != p.Z)) throw new InvalidDataException("同一方向样本的 XYZ/训练角色跨波长不一致。");
        }
        var groups = rows.GroupBy(p => p.WavelengthNanometers).OrderBy(g => g.Key).ToArray();
        if (groups.Length > 32) throw new InvalidDataException("XYZ 校准波长超过 32。");
        var sampleSet = rows.Select(p => p.SampleId).ToHashSet();
        if (groups.Any(g => !sampleSet.SetEquals(g.Select(p => p.SampleId)))) throw new InvalidDataException("XYZ 校准各波长缺少配对观测。");
        var bands = new List<XyzRadiometricBand>(); var evidence = new List<XyzCalibrationBandValidation>();
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested(); var train = group.Where(p => !p.Validation).ToArray(); var validation = group.Where(p => p.Validation).ToArray();
            if (train.Length < 3 || validation.Length < 1) throw new InvalidDataException("XYZ 校准需要至少三个训练方向和一个独立验证方向。");
            var scales = new[] { train.Max(p => p.X), train.Max(p => p.Y), train.Max(p => p.Z) };
            for (var j = 0; j < 3; j++) if (scales[j] == 0) scales[j] = 1;
            double V(XyzRadiometricObservation p, int j) => (j == 0 ? p.X : j == 1 ? p.Y : p.Z) / scales[j];
            var matrix = new double[train.Length, 3]; for (var r = 0; r < train.Length; r++) for (var c = 0; c < 3; c++) matrix[r, c] = V(train[r], c);
            var factor = JacobiSvd.Factor(matrix); var nonzero = factor.SingularSquared.Where(v => v > factor.Threshold).ToArray(); var rank = nonzero.Length;
            var condition = nonzero.Length > 0 ? Math.Sqrt(nonzero.Max() / nonzero.Min()) : 1;
            var targetScale = Math.Max(train.Max(p => p.RadiometricBsdfPerSteradian), policy.RelativeSignalFloor);
            var target = train.Select(p => p.RadiometricBsdfPerSteradian / targetScale).ToArray();
            var best = new double[3]; var bestError = target.Sum(v => v * v);
            if (gamut is not null) best = XyzGamutLeastSquares.Solve(matrix, target, scales, gamut);
            for (var mask = 1; gamut is null && mask < 8; mask++)
            {
                cancellationToken.ThrowIfCancellationRequested(); var active = Enumerable.Range(0, 3).Where(c => (mask & (1 << c)) != 0).ToArray();
                var subset = new double[train.Length, active.Length]; for (var r = 0; r < train.Length; r++) for (var c = 0; c < active.Length; c++) subset[r, c] = matrix[r, active[c]];
                var candidate = JacobiSvd.Solve(JacobiSvd.Factor(subset), target);
                if (candidate.Any(v => v < 0 || !double.IsFinite(v))) continue;
                var error = Enumerable.Range(0, train.Length).Sum(r => Math.Pow(active.Select((c, k) => matrix[r, c] * candidate[k]).Sum() - target[r], 2));
                if (error < bestError) { bestError = error; best = new double[3]; for (var c = 0; c < active.Length; c++) best[active[c]] = candidate[c]; }
            }
            var coefficients = best.Select((v, j) => v * targetScale / scales[j]).ToArray();
            if (coefficients.Any(c => !double.IsFinite(c))) throw new InvalidDataException("XYZ 校准系数溢出。");
            double Predict(XyzRadiometricObservation p) => coefficients[0] * p.X + coefficients[1] * p.Y + coefficients[2] * p.Z;
            (double NormalizedRms, double MaximumAbsolute, double MaximumRelative) Metrics(XyzRadiometricObservation[] values)
            {
                var errors = values.Select(p => Predict(p) - p.RadiometricBsdfPerSteradian).ToArray();
                return (Math.Sqrt(errors.Sum(v => v * v) / Math.Max(values.Sum(p => p.RadiometricBsdfPerSteradian * p.RadiometricBsdfPerSteradian), values.Length * policy.RelativeSignalFloor * policy.RelativeSignalFloor)),
                    errors.Max(Math.Abs), errors.Select((e, i) => Math.Abs(e) / Math.Max(policy.RelativeSignalFloor, values[i].RadiometricBsdfPerSteradian)).Max());
            }
            var fit = Metrics(train); var holdout = Metrics(validation); var passed = rank == 3 && holdout.NormalizedRms <= policy.MaximumValidationNormalizedRms && holdout.MaximumAbsolute <= policy.MaximumValidationAbsoluteError;
            if (new[] { fit.NormalizedRms, holdout.NormalizedRms, holdout.MaximumAbsolute, holdout.MaximumRelative }.Any(v => !double.IsFinite(v))) throw new InvalidDataException("XYZ 校准验证误差溢出；不导出无效校准。");
            bands.Add(new(group.Key, coefficients[0], coefficients[1], coefficients[2]));
            evidence.Add(new(group.Key, train.Length, validation.Length, rank, condition, fit.NormalizedRms, holdout.NormalizedRms, holdout.MaximumAbsolute, holdout.MaximumRelative, passed));
        }
        return new(Array.AsReadOnly(bands.ToArray()), new(provenance, policy, Array.AsReadOnly(evidence.ToArray()), BandsDigest(bands, gamut), gamut));
    }
}
