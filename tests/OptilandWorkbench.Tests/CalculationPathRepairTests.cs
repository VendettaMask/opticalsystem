using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class CalculationPathRepairTests
{
    [Theory]
    [InlineData(ZernikeAnalysisKind.Fringe, false)]
    [InlineData(ZernikeAnalysisKind.Fringe, true)]
    [InlineData(ZernikeAnalysisKind.Standard, false)]
    [InlineData(ZernikeAnalysisKind.Standard, true)]
    [InlineData(ZernikeAnalysisKind.Annular, false)]
    [InlineData(ZernikeAnalysisKind.Annular, true)]
    public void HexZernikeUsesTheSystemAiming(ZernikeAnalysisKind kind, bool aiming)
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = aiming;
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var field = SpotAnalysisEngine.DefinedFields(optic).Last();
        var source = WavefrontEngine.GenerateChiefRay(optic, field, wave, 4, aimAtStop: aiming);
        var basis = kind == ZernikeAnalysisKind.Standard ? ZernikeBasisKind.Standard
            : kind == ZernikeAnalysisKind.Annular ? ZernikeBasisKind.Annular : ZernikeBasisKind.Fringe;
        var expected = ZernikeMetrics.Fit(source.Samples, basis, 12, .5, requireFullRank: false);
        var actual = new ZernikeAnalysis(optic, kind, numRings: 4, numTerms: 12, mapSize: 17).GenerateData();
        Assert.Equal(expected.Coefficients.Select(c => c.Value), (double[])actual.Values["CoefficientsWaves"]);
        Assert.Equal(aiming, actual.Values["UseRayAiming"]);
        Assert.Equal("Hexapolar rings", actual.Values["SamplingMode"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZernikeFieldSweepUsesTheSameAimedEdgePupil(bool aiming)
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = aiming;
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var edge = SpotAnalysisEngine.DefinedFields(optic).Last();
        var expected = ZernikeFitEngine.FitFringe(WavefrontEngine.GenerateChiefRay(optic, edge, wave, 4, aiming).Samples, 12);
        var actual = new ZernikeVsFieldAnalysis(optic, fieldDensity: 2, numRings: 4, numTerms: 12).GenerateData();
        Assert.Equal(expected.Select(c => c.Value), actual.PlotSeries.Select(s => s.Points[^1].Y));
        Assert.Equal(aiming, actual.Values["UseRayAiming"]);
    }

    [Theory]
    [InlineData(false, "Zernike Standard")]
    [InlineData(true, "Zernike Standard")]
    [InlineData(false, "Zernike Annular")]
    [InlineData(true, "Zernike Annular")]
    public void DesktopZernikeExplicitlyUsesUniformPupilSampling(bool aiming, string name)
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = aiming;
        var actual = new WorkbenchRuntime(optic).BuildAnalysisData(name, new Dictionary<string, string>
        { ["PupilSampling"] = "32 x 32", ["ZernikeTerms"] = "37", ["FieldNumber"] = "1", ["ObscurationRatio"] = ".5" });
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var source = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32, aimAtStop: aiming, zemaxCentered: true);
        var basis = name == "Zernike Standard" ? ZernikeBasisKind.Standard : ZernikeBasisKind.Annular;
        var expected = ZernikeMetrics.Fit(source.Samples, basis, 37, .5, requireFullRank: false);
        Assert.Equal(expected.Coefficients.Select(c => c.Value), (double[])actual.Values["CoefficientsWaves"]);
        Assert.Equal("32 x 32", actual.Values["Sampling"]);
        Assert.Equal("Zemax uniform pupil grid", actual.Values["SamplingMode"]);
    }

    [Theory]
    [InlineData(ReferenceSphereStrategy.CentroidSphere)]
    [InlineData(ReferenceSphereStrategy.BestFitSphere)]
    public void ReferenceSphereAimingIsNotSilentlyIgnored(ReferenceSphereStrategy strategy)
    {
        var optic = Optic.CreateCookeTriplet();
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        optic.RayAimingEnabled = false;
        var off = ReferenceSphereWavefrontEngine.Generate(optic, (0, 1), wave, 4, strategy);
        optic.RayAimingEnabled = true;
        var on = ReferenceSphereWavefrontEngine.Generate(optic, (0, 1), wave, 4, strategy);
        Assert.False(off.UseRayAiming); Assert.True(on.UseRayAiming);
        Assert.True(Math.Abs(off.Rms - on.Rms) > 1e-8);
        Assert.Equal(off.Samples.Select(s => (s.NormalizedPupilX, s.NormalizedPupilY)),
            on.Samples.Select(s => (s.NormalizedPupilX, s.NormalizedPupilY)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AfocalReferenceFallbackUsesSystemAiming(bool aiming)
    {
        var optic = Optic.CreateCookeTriplet(); optic.ImageSpaceAfocal = true; optic.RayAimingEnabled = aiming;
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var expected = WavefrontEngine.GenerateChiefRay(optic, (0, 1), wave, 4, aiming);
        var actual = new ReferenceSphereWavefrontAnalysis(optic, ReferenceSphereStrategy.CentroidSphere, 4, 17,
            fieldNumber: optic.Fields.Count).GenerateData();
        Assert.Equal(expected.Rms, Convert.ToDouble(actual.Values["RmsWaves"]), 12);
        Assert.Equal(aiming, actual.Values["UseRayAiming"]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FftPreparedPupilAndScaleHaveOneAimingContract(bool aiming, bool polarized)
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = aiming;
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var pupil = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 1), wave, 32, aimAtStop: aiming, zemaxCentered: true);
        var jones = JonesPupilEngine.Generate(optic, (0, 1), wave, 32, aimAtStop: aiming, zemaxCentered: true);
        var automatic = DiffractionEngine.ComputeFftPsf(optic, (0, 1), wave, 32, 64,
            usePolarization: polarized, cellCenteredPupil: true, zemaxFftSampling: true);
        var prepared = DiffractionEngine.ComputeFftPsf(optic, (0, 1), wave, 32, 64,
            usePolarization: polarized, cellCenteredPupil: true, zemaxFftSampling: true,
            preparedWavefront: pupil, preparedPolarization: jones);
        Assert.Equal(automatic.Values.Cast<double>(), prepared.Values.Cast<double>());
        Assert.Equal(aiming, automatic.UseRayAiming); Assert.False(automatic.StopAimingFallbackUsed);
        Assert.Equal(DiffractionEngine.WorkingFNumber(optic, (0, 1), wave, aimAtStop: aiming), automatic.WorkingFNumber, 12);
        var wrong = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 1), wave, 32, aimAtStop: !aiming, zemaxCentered: true);
        Assert.Throws<InvalidOperationException>(() => DiffractionEngine.ComputeFftPsf(optic, (0, 1), wave, 32, 64,
            cellCenteredPupil: true, zemaxFftSampling: true, preparedWavefront: wrong));
    }

    [Theory]
    [InlineData("grid")]
    [InlineData("field")]
    [InlineData("wave")]
    [InlineData("unknown")]
    [InlineData("nodes")]
    [InlineData("reference")]
    [InlineData("jones_nodes")]
    [InlineData("jones")]
    public void MismatchedPreparedPupilsAreRejected(string mismatch)
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = false;
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var pupil = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32, zemaxCentered: true);
        pupil = mismatch switch
        {
            "grid" => pupil with { PupilGrid = pupil.PupilGrid! with { Stretch = 2 } },
            "field" => pupil with { SourceField = (0, 1) },
            "wave" => pupil with { SourceWavelengthNanometers = 123 },
            "unknown" => pupil with { UseRayAiming = null },
            "nodes" => pupil with { Samples = pupil.Samples.Select(s => s with { NormalizedPupilX = s.NormalizedPupilX + .001 }).ToArray() },
            "reference" => pupil with { SourceReferenceWavelengthNanometers = 123 },
            _ => pupil
        };
        var jones = JonesPupilEngine.Generate(optic, (0, 0), wave, 32, zemaxCentered: true);
        if (mismatch == "jones") jones = jones with { UseRayAiming = true };
        if (mismatch == "jones_nodes") jones = jones with { Samples = jones.Samples.Select(s => s with { Px = s.Px + .001 }).ToArray() };
        Assert.Throws<InvalidOperationException>(() => DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64,
            usePolarization: true, cellCenteredPupil: true, zemaxFftSampling: true,
            preparedWavefront: pupil, preparedPolarization: jones));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JonesCellFlagNeverChangesAimingForIdenticalZemaxNodes(bool aiming)
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = !aiming;
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var a = JonesPupilEngine.Generate(optic, (0, 1), wave, 16, cellCentered: false, aimAtStop: aiming, zemaxCentered: true);
        var b = JonesPupilEngine.Generate(optic, (0, 1), wave, 16, cellCentered: true, aimAtStop: aiming, zemaxCentered: true);
        Assert.Equal(a.Samples, b.Samples); Assert.Equal(aiming, b.UseRayAiming);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ZeroDefocusIsExactlyTheRequestedSystemPupil(bool aiming, bool afocal)
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = aiming; optic.ImageSpaceAfocal = afocal;
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        (double X, double Y)[] points = [(0, 0), (.2, .3), (-.2, -.3)];
        var expected = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 1), wave, points, aimAtStop: aiming);
        var actual = DiffractionEngine.GenerateDefocusedWavefront(optic, (0, 1), wave, points, 0);
        Assert.Equal(expected.Samples, actual.Samples);
        Assert.Equal(expected.ReferenceOpticalPath, actual.ReferenceOpticalPath);
        Assert.Equal(aiming, actual.UseRayAiming);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefocusRestoresGeometryAndDoesNotReuseNominalCachedTrace(bool aiming)
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = aiming;
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        (double X, double Y)[] points = [(0, 0), (.2, .3), (-.2, -.3)];
        var original = optic.SurfaceGroup.Items.Select(s => (s.Thickness, s.CoordinateSystem)).ToArray();
        optic.ConfigureRayTraceCache(new RayTraceCache(), 1);
        var nominal = DiffractionEngine.GenerateDefocusedWavefront(optic, (0, 1), wave, points, 0);
        var shifted = DiffractionEngine.GenerateDefocusedWavefront(optic, (0, 1), wave, points, .1);
        var restored = DiffractionEngine.GenerateDefocusedWavefront(optic, (0, 1), wave, points, 0);
        Assert.Equal(original, optic.SurfaceGroup.Items.Select(s => (s.Thickness, s.CoordinateSystem)));
        Assert.Equal(nominal.Samples, restored.Samples); Assert.NotEqual(nominal.Samples, shifted.Samples);
        Assert.Equal(aiming, shifted.UseRayAiming);
        var color = DiffractionEngine.GenerateDefocusedPolychromaticWavefronts(optic, (0, 1), optic.Wavelengths.ToArray(), points, .1);
        Assert.All(color, result => Assert.Equal(aiming, result.UseRayAiming));
        Assert.Equal(original, optic.SurfaceGroup.Items.Select(s => (s.Thickness, s.CoordinateSystem)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisplayCroppingPreservesFullFftPhysicsAndNormalization(bool normalize)
    {
        var runtime = new WorkbenchRuntime(Optic.CreateCookeTriplet());
        var settings = new Dictionary<string, string> { ["Sampling"] = "64", ["WavelengthNumber"] = "1", ["FieldNumber"] = "1", ["Normalized"] = normalize.ToString() };
        settings["Display"] = "128"; var full = runtime.BuildAnalysisData("PSF", settings);
        var reference = full.PlotSeries.Single().Points.ToDictionary(p => (p.X, p.Y));
        foreach (var size in new[] { 32, 64 })
        {
            settings["Display"] = size.ToString(); var cropped = runtime.BuildAnalysisData("PSF", settings);
            Assert.Equal(128, cropped.Values["GridSize"]); Assert.Equal(size, cropped.Values["DisplaySize"]);
            Assert.Equal(full.Values["ImageDeltaMicrometers"], cropped.Values["ImageDeltaMicrometers"]);
            Assert.Equal(full.Values["PeakStrehlRatio"], cropped.Values["PeakStrehlRatio"]);
            Assert.Equal(size * size, cropped.PlotSeries.Single().Points.Count);
            Assert.All(cropped.PlotSeries.Single().Points, p => Assert.Equal(reference[(p.X, p.Y)].Value, p.Value));
        }
    }

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void ImplicitGridDoesNotReduceNominalPupilSamplingTwice(int sampling)
    {
        var optic = Optic.CreateCookeTriplet();
        var implicitGrid = new PsfAnalysis(optic, numRays: sampling, wavelengthNumber: 1, fieldNumber: 1, zemaxCompatible: true).GenerateData();
        var explicitGrid = new PsfAnalysis(optic, numRays: sampling, gridSize: 2 * sampling, wavelengthNumber: 1, fieldNumber: 1, zemaxCompatible: true).GenerateData();
        Assert.Equal(sampling, implicitGrid.Values["PupilSampling"]);
        Assert.Equal(explicitGrid.Values["ImageDeltaMicrometers"], implicitGrid.Values["ImageDeltaMicrometers"]);
        Assert.Equal(explicitGrid.PlotSeries.Single().Points, implicitGrid.PlotSeries.Single().Points);
    }

    [Fact]
    public void NegativeImageDeltaUsesFullUnstretchedPupil()
    {
        var optic = Optic.CreateCookeTriplet(); var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var automatic = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 64, 128, zemaxFftSampling: true);
        var negative = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 64, 128, zemaxFftSampling: true, imageDelta: -1);
        Assert.Equal(1, negative.PupilGridStretch); Assert.Equal(Math.Sqrt(2), automatic.PupilGridStretch);
        Assert.Equal(automatic.SampleSpacingMicrometers * Math.Sqrt(2), negative.SampleSpacingMicrometers, 12);
        Assert.NotEqual(automatic.Values.Cast<double>().ToArray(), negative.Values.Cast<double>().ToArray());
        var another = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 64, 128, zemaxFftSampling: true, imageDelta: -10);
        Assert.Equal(negative.Values.Cast<double>(), another.Values.Cast<double>());
        Assert.Throws<ArgumentOutOfRangeException>(() => new PsfAnalysis(optic, imageDeltaMicrometers: double.NaN));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 10)]
    [InlineData(0, 100)]
    [InlineData(1, 1)]
    [InlineData(1, 10)]
    [InlineData(1, 100)]
    public void HuygensPsfIsInvariantUnderGlobalAxialTranslation(int edge, double shift)
    {
        var optic = FrozenCooke(); var moved = Optic.FromSnapshot(optic.ToSnapshot());
        moved.InvalidateRayTraceCache();
        foreach (var surface in moved.SurfaceGroup.Items)
            surface.CoordinateSystem = surface.CoordinateSystem with { Origin = surface.CoordinateSystem.Origin + new Vector3D(0, 0, shift) };
        var wave = optic.Wavelengths[edge == 0 ? 1 : 0];
        var baseline = DiffractionEngine.ComputeHuygensPsf(optic, (0, edge), wave, 32, 32, .00025, aimAtStop: optic.RayAimingEnabled);
        var translated = DiffractionEngine.ComputeHuygensPsf(moved, (0, edge), wave, 32, 32, .00025, aimAtStop: optic.RayAimingEnabled);
        // Internal geometric invariant, not a relaxed native numerical tolerance.
        Assert.InRange(baseline.Values.Cast<double>().Zip(translated.Values.Cast<double>(), (a, b) => Math.Abs(a - b) / 100).Max(), 0, 1e-7);
    }

    [Fact]
    public void FoucaultDoesNotPretendToSupportPolarization()
    {
        var optic = Optic.CreateCookeTriplet();
        Assert.Throws<NotSupportedException>(() => new FoucaultAnalysis(optic, usePolarization: true).GenerateData());
        var data = new FoucaultAnalysis(optic, sampling: 16).GenerateData();
        Assert.Contains("Qualitative", data.Values["ComputationModel"].ToString());
        Assert.False((bool)data.Values["UsePolarization"]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FastMtfUsesTheRequestedWavefrontAndDirectionalScale(bool aiming, bool polarized)
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = aiming;
        var waveNumber = optic.Wavelengths.ToList().FindIndex(w => w.IsPrimary) + 1;
        var wave = optic.Wavelengths[waveNumber - 1];
        var pupil = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 1), wave, 16,
            cellCentered: true, aimAtStop: aiming, referenceWavelength: wave);
        var jones = polarized ? JonesPupilEngine.Generate(optic, (0, 1), wave, 16,
            cellCentered: true, aimAtStop: aiming) : null;
        var expected = DiffractionEngine.ComputeFastFftMtfAtFrequency(optic, (0, 1), wave, 16, 20, 0,
            polarized, pupil, jones, wave);
        var actual = new MtfThroughFocusAnalysis(optic, MtfComputationMethod.Fourier,
            spatialFrequency: 20, focusPlaneCount: 1, wavelengthNumber: waveNumber, fieldNumber: optic.Fields.Count,
            settings: new(PupilSampling: 16, ImageSize: 32, UsePolarization: polarized, ZemaxCompatible: true)).GenerateData();
        Assert.Equal(expected.Tangential.Magnitude, actual.PlotSeries[0].Points[0].Y, 12);
        Assert.Equal(expected.Sagittal.Magnitude, actual.PlotSeries[1].Points[0].Y, 12);
        Assert.Equal(aiming, actual.Values["UseRayAiming"]);
        optic.RayAimingEnabled = !aiming;
        var samePrepared = DiffractionEngine.ComputeFastFftMtfAtFrequency(optic, (0, 1), wave, 16, 20, .01,
            polarized, pupil, jones, wave);
        optic.RayAimingEnabled = aiming;
        var consistent = DiffractionEngine.ComputeFastFftMtfAtFrequency(optic, (0, 1), wave, 16, 20, .01,
            polarized, pupil, jones, wave);
        Assert.Equal(consistent, samePrepared);
    }

    private static Optic FrozenCooke() => Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Validation", "Zemax", "StandardSampleOpd", "cooke-40-degree-field", "snapshot.json")), new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals })!);

    public static IEnumerable<object[]> NativeZeroDefocusCases()
    {
        foreach (var lens in new[] { "cooke-40-degree-field", "double-gauss-28-degree-field", "relay-lens" })
        foreach (var setting in new[] { "primary", "edge-shortwave" }) yield return [lens, setting];
    }

    [Theory]
    [MemberData(nameof(NativeZeroDefocusCases))]
    public void ZeroDefocusMatchesAllFrozenNativeFanKnotsAtCapturedSettings(string lens, string setting)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Validation", "Zemax", "StandardSampleOpd");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        var fixture = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(row =>
            row.GetProperty("lens").GetString() == lens && row.GetProperty("setting").GetString() == setting);
        var snapshotPath = Path.Combine(root, lens, "snapshot.json");
        var nativePath = Path.Combine(root, lens, setting + "-native.json");
        string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        Assert.Equal(fixture.GetProperty("snapshotSha256").GetString(), Hash(snapshotPath));
        Assert.Equal(fixture.GetProperty("nativeSha256").GetString(), Hash(nativePath));
        var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(File.ReadAllText(snapshotPath), new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals })!);
        var request = fixture.GetProperty("request");
        Assert.Equal(request.GetProperty("useRayAiming").GetBoolean(), optic.RayAimingEnabled);
        var wave = optic.Wavelengths[request.GetProperty("wavelength").GetInt32() - 1];
        var field = SpotAnalysisEngine.DefinedFields(optic)[request.GetProperty("field").GetInt32() - 1];
        using var native = JsonDocument.Parse(File.ReadAllText(nativePath));
        Assert.Empty(native.RootElement.GetProperty("messages").EnumerateArray());
        var fans = native.RootElement.GetProperty("dataSeries").EnumerateArray().ToArray();
        var coordinates = fans.SelectMany((fan, axis) => fan.GetProperty("x").EnumerateArray()
            .Select(x => axis == 0 ? (X: 0d, Y: x.GetDouble()) : (X: x.GetDouble(), Y: 0d))).ToArray();
        var reference = fans.SelectMany(f => f.GetProperty("y").EnumerateArray()
            .Select(y => y[0].ValueKind == JsonValueKind.Null ? (double?)null : y[0].GetDouble())).ToArray();
        var actual = DiffractionEngine.GenerateDefocusedWavefront(optic, field, wave, coordinates, 0);
        Assert.Equal(reference.Length, actual.Samples.Count);
        var tolerance = fixture.GetProperty("tolerances").GetProperty("WavefrontError");
        var absolute = tolerance.GetProperty("absolute").GetDouble(); var relative = tolerance.GetProperty("relative").GetDouble();
        var errors = new List<double>(); var peak = 0d;
        for (var i = 0; i < reference.Length; i++)
        {
            Assert.Equal(reference[i].HasValue, actual.Samples[i].Intensity > 0);
            if (!reference[i].HasValue) continue;
            var value = reference[i]!.Value; var error = actual.Samples[i].OpdWaves - value;
            Assert.InRange(Math.Abs(error), 0, absolute + relative * Math.Abs(value));
            errors.Add(error); peak = Math.Max(peak, Math.Abs(value));
        }
        Assert.NotEmpty(errors);
        Assert.InRange(Math.Sqrt(errors.Average(e => e * e)) / peak, 0, tolerance.GetProperty("nrmse").GetDouble());
    }
}
