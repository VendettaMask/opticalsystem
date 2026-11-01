using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class CommercialBsdfCalibrationTests
{
    internal static XyzCalibrationProvenance Synthetic(bool known = true) => new(XyzCalibrationDataKind.SyntheticControl, "Synthetic independent calibration control, not measured", new string('1', 64), new string('2', 64), known);
    internal static XyzRadiometricObservation[] Observations(bool corruptValidation = false)
    {
        var xyz = new[] { ("a", 1d, 0d, 0d, false), ("b", 0d, 1d, 0d, false), ("c", 0d, 0d, 1d, false), ("d", .6, .4, .7, false), ("e", .25, .5, .75, true), ("f", .7, .2, .4, true) };
        return new[] { 400d, 700 }.SelectMany(w => xyz.Select(p => new XyzRadiometricObservation(p.Item1, w, p.Item2, p.Item3, p.Item4,
            w == 400 ? .2 * p.Item2 + .3 * p.Item3 + .1 * p.Item4 + (corruptValidation && p.Item5 ? .1 : 0) : .1 * p.Item2 + .2 * p.Item3 + .5 * p.Item4 + (corruptValidation && p.Item5 ? .1 : 0), p.Item5))).ToArray();
    }
    internal static XyzRadiometricFitResult Fit(bool corrupt = false, bool known = true) => XyzRadiometricCalibrator.Fit(Observations(corrupt), Synthetic(known), new(1e-10, 1e-10, 1e-9));
    internal static string CalibrationJson() => JsonSerializer.Serialize(Fit().CreateCalibration("Synthetic fitted calibration; not measured", "Synthetic observer", "Synthetic illuminant"));

    [Fact]
    public void NonnegativeFitRecoversKnownCoefficientsAndFreezesValidationProvenance()
    {
        var result = Fit(); Assert.True(result.ModelValidationPassed); Assert.Equal(.2, result.Bands[0].X, 12); Assert.Equal(.3, result.Bands[0].Y, 12); Assert.Equal(.1, result.Bands[0].Z, 12);
        Assert.All(result.Evidence.Bands, b => { Assert.Equal(3, b.Rank); Assert.Equal(4, b.TrainingCount); Assert.Equal(2, b.ValidationCount); Assert.InRange(b.ValidationNormalizedRms, 0, 1e-12); });
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("fitted.json"); File.WriteAllText(path, CalibrationJson()); var calibration = XyzRadiometricCalibration.Read(path);
        Assert.Equal(result.Evidence.CalibrationBandsSha256, calibration.FitEvidence!.CalibrationBandsSha256); Assert.Equal(XyzCalibrationDataKind.SyntheticControl, calibration.FitEvidence.Provenance.Kind);
        Assert.Throws<NotSupportedException>(() => ((IList<XyzCalibrationBandValidation>)calibration.FitEvidence.Bands)[0] = calibration.FitEvidence.Bands[0]);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), calibration.SourceSha256);
        var black = XyzRadiometricCalibrator.Fit(Observations().Select(p => p with { RadiometricBsdfPerSteradian = 0 }).ToArray(), Synthetic(), new(1e-10, 1e-10, 1e-9));
        Assert.True(black.ModelValidationPassed); Assert.All(black.Bands, b => Assert.True(b.X == 0 && b.Y == 0 && b.Z == 0));
    }
    [Fact]
    public void HoldoutFailureNeverChangesTrainingOrExportsACalibration()
    {
        var original = Fit(); var failed = Fit(true); Assert.Equal(original.Bands, failed.Bands); Assert.False(failed.ModelValidationPassed);
        Assert.All(failed.Evidence.Bands, b => { Assert.InRange(b.TrainingNormalizedRms, 0, 1e-12); Assert.True(b.ValidationMaximumAbsoluteError > .09); });
        Assert.Throws<InvalidDataException>(() => failed.CreateCalibration("bad", "observer", "illuminant"));
    }
    [Fact]
    public void MissingPhysicalConditionsAndRankDeficiencyCannotBecomeValidatedCalibration()
    {
        var unknown = Fit(known: false); Assert.True(unknown.ModelValidationPassed); Assert.Throws<InvalidDataException>(() => unknown.CreateCalibration("missing", "unknown", "unknown"));
        var rows = Enumerable.Range(0, 6).Select(i => new XyzRadiometricObservation("s" + i, 550, 1, 1, 1, .3, i >= 4)).ToArray();
        var degenerate = XyzRadiometricCalibrator.Fit(rows, Synthetic(), new(1e-10, 1e-10, 1e-9)); Assert.False(degenerate.ModelValidationPassed); Assert.Equal(1, degenerate.Evidence.Bands[0].Rank);
    }
    [Fact]
    public void ActiveSetFitSatisfiesIndependentNonnegativeKktConditions()
    {
        var v = new[] { new[] { 1d, .6, .3 }, new[] { .2, 1d, .4 }, new[] { .3, .3, 1d }, new[] { .6, .8, .7 }, new[] { .4, .7, .8 }, new[] { .5, .9, .2 } };
        var rows = v.Select((p, i) => new XyzRadiometricObservation("q" + i, 550, p[0], p[1], p[2], -.2 * p[0] + .5 * p[1] + .4 * p[2], i >= 4)).ToArray();
        var fit = XyzRadiometricCalibrator.Fit(rows, Synthetic(), new(0, 0, 1e-9)); var b = fit.Bands[0]; Assert.Equal(0, b.X, 12); Assert.True(b.Y > 0 && b.Z > 0);
        var gradient = new double[3]; for (var i = 0; i < 4; i++) { var residual = b.X * v[i][0] + b.Y * v[i][1] + b.Z * v[i][2] - rows[i].RadiometricBsdfPerSteradian; for (var j = 0; j < 3; j++) gradient[j] += v[i][j] * residual; }
        Assert.True(gradient[0] >= -1e-12); Assert.InRange(Math.Abs(gradient[1]), 0, 1e-12); Assert.InRange(Math.Abs(gradient[2]), 0, 1e-12);
    }
    [Fact]
    public void DuplicatesLeakageNegativeMeasurementsAndCancellationAreRejected()
    {
        var good = Observations(); Assert.Throws<InvalidDataException>(() => XyzRadiometricCalibrator.Fit(good.Append(good[0]).ToArray(), Synthetic(), new(1, 1, 1e-9)));
        var leakage = good.ToArray(); leakage[^1] = leakage[^1] with { Validation = !leakage[^1].Validation }; Assert.Throws<InvalidDataException>(() => XyzRadiometricCalibrator.Fit(leakage, Synthetic(), new(1, 1, 1e-9)));
        var negative = good.ToArray(); negative[0] = negative[0] with { RadiometricBsdfPerSteradian = -1e-6 }; Assert.Throws<InvalidDataException>(() => XyzRadiometricCalibrator.Fit(negative, Synthetic(), new(1, 1, 1e-9)));
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); Assert.ThrowsAny<OperationCanceledException>(() => XyzRadiometricCalibrator.Fit(good, Synthetic(), new(1, 1, 1e-9), cancel.Token));
    }
    [Fact]
    public void ModifiedCoefficientsCannotKeepTheOriginalFitEvidence()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var original = Fit().CreateCalibration("test", "observer", "illuminant");
        var bad = original with { Bands = original.Bands.Select(b => b with { X = b.X + .01 }).ToArray() }; var path = files.Path("tampered.json"); File.WriteAllText(path, JsonSerializer.Serialize(bad));
        Assert.Throws<InvalidDataException>(() => XyzRadiometricCalibration.Read(path));
    }
    [Fact]
    public void ReferenceComparerRequiresFrozenIdentityAndDoesNotCallSyntheticResultsCommercial()
    {
        var modelData = MeasuredBsdfTests.Constant(.2, .5);
        var identity = new BsdfReferenceIdentity(BsdfReferenceOrigin.SyntheticControl, "Independent analytic Lambertian control", "1", "", new string('1', 64), modelData.SourceSha256, modelData.DataSha256);
        var rows = new[] { new BsdfReferenceValue(BsdfSide.Front, 550, 30, 0, 45, 90, 1, 1, .2 / Math.PI, .2, .5), new(BsdfSide.Front, 550, 30, 0, 120, 90, 1, 1, .5 / Math.PI, .2, .5) };
        var report = BsdfReferenceComparer.Compare(modelData, identity, rows, new(1e-12, 1e-12, 1e-12, 1e-12)); Assert.True(report.NumericalComparisonPassed); Assert.False(report.CommercialComparisonPassed);
        var wrong = rows.ToArray(); wrong[0] = wrong[0] with { ValuePerSteradian = rows[0].ValuePerSteradian * 1.1 }; var failure = BsdfReferenceComparer.Compare(modelData, identity, wrong, new(1e-12, 1e-12, 1e-12, 1e-12)); Assert.False(failure.NumericalComparisonPassed); Assert.False(failure.Points[0].Passed);
        Assert.Throws<InvalidDataException>(() => BsdfReferenceComparer.Compare(modelData, identity with { ModelDataSha256 = new string('f', 64) }, rows, new(1, 1, 1, 1)));
        Assert.Throws<InvalidDataException>(() => BsdfReferenceComparer.Compare(modelData, identity with { Origin = BsdfReferenceOrigin.CommercialNativeExport }, rows, new(1, 1, 1, 1)));
        Assert.Throws<InvalidDataException>(() => BsdfReferenceComparer.Compare(modelData, identity, Array.Empty<BsdfReferenceValue>(), new(1, 1, 1, 1)));
        Assert.Throws<InvalidDataException>(() => BsdfReferenceComparer.Compare(modelData, identity, rows.Append(rows[0]).ToArray(), new(1, 1, 1, 1)));
    }
}
