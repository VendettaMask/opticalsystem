using OpticalSystemDesign.Validation;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class AnalysisSurfaceAndReferenceTests
{
    private static readonly JsonSerializerOptions Json = new()
    { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private static string Snapshot(Optic optic) => JsonSerializer.Serialize(optic.ToSnapshot(), Json);

    [Theory]
    [InlineData("Optical Path Difference")] [InlineData("Wavefront")] [InlineData("PSF")] [InlineData("MTF")]
    public void InvalidSurfaceIsRejectedWithoutChangingTheSource(string key)
    {
        var optic = Example(); var frozen = Snapshot(optic);
        Assert.Throws<ArgumentOutOfRangeException>(() => Evaluate(optic, key, 999));
        Assert.Equal(frozen, Snapshot(optic));
    }

    [Theory]
    [InlineData("Optical Path Difference")] [InlineData("Wavefront")] [InlineData("PSF")] [InlineData("MTF")]
    public void SelectingARealIntermediateImageChangesTheActualResult(string key)
    {
        var optic = Lens(); var frozen = Snapshot(optic);
        var final = Evaluate(optic, key, -1);
        var selected = Evaluate(optic, key, 1);
        Assert.Equal(AnalysisOutcome.Success, final.Outcome);
        Assert.Equal(AnalysisOutcome.Success, selected.Outcome);
        Assert.NotEqual(Numbers(final), Numbers(selected));
        Assert.Equal(frozen, Snapshot(optic));
    }

    [Theory]
    [InlineData(50d)] [InlineData(-50d)]
    public void TheSelectedLensRetainsItsPowerAndHasItsAnalyticalRealOrVirtualFocus(double focalLength)
    {
        var optic = Lens(focalLength); var frozen = Snapshot(optic);
        var prepared = AnalysisSurfacePreparation.Prepare(optic, 1);
        Assert.NotSame(optic, prepared);
        Assert.IsType<ThinLensInteractionModel>(prepared.SurfaceGroup.Items[1].InteractionModel);
        Assert.Equal(focalLength, prepared.SurfaceGroup.Items[1].Thickness, 10);
        Assert.Equal(focalLength, prepared.SurfaceGroup.Items[^1].CoordinateSystem.Origin.Z, 10);
        Assert.IsType<PlaneGeometry>(prepared.SurfaceGroup.Items[^1].Geometry);
        Assert.IsType<CoordinateReferenceInteractionModel>(prepared.SurfaceGroup.Items[^1].InteractionModel);
        var marginal = prepared.Paraxial.MarginalRay(prepared.Wavelengths.First(w => w.IsPrimary).Micrometers);
        Assert.InRange(Math.Abs(marginal.Heights[^1][0]), 0, 1e-12);
        Assert.Equal(frozen, Snapshot(optic));
    }

    [Theory]
    [InlineData(ApertureKind.FloatByStopSize)] [InlineData(ApertureKind.NumericalAperture)]
    public void BeforeStopUsesTheOriginalPupilAndAnIndependentDummyStop(ApertureKind aperture)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].IsStop = false;
        optic.SurfaceGroup.Items[2].IsStop = true;
        optic.SurfaceGroup.Items[0].Thickness = 200; optic.SurfaceGroup.Renumber();
        optic.Aperture.Kind = aperture; optic.Aperture.Value = aperture == ApertureKind.NumericalAperture ? .01 : 5;
        var diameter = optic.Paraxial.EstimateEntrancePupilDiameter(); var frozen = Snapshot(optic);
        var prepared = AnalysisSurfacePreparation.Prepare(optic, 1);
        Assert.True(prepared.SurfaceGroup.Items[1].IsStop);
        Assert.Equal(diameter / 2, prepared.SurfaceGroup.Items[1].SemiDiameter, 10);
        Assert.Equal(aperture == ApertureKind.NumericalAperture ? aperture : ApertureKind.EntrancePupilDiameter,
            prepared.Aperture.Kind);
        Assert.IsType<ThinLensInteractionModel>(prepared.SurfaceGroup.Items[2].InteractionModel);
        Assert.Equal(1 / (1d / 50 - 1d / 200), prepared.SurfaceGroup.Items[2].Thickness, 10);
        Assert.Equal(frozen, Snapshot(optic));
    }

    [Fact]
    public void AimingBeforeTheOriginalStopIsRejectedWithoutMutatingTheLens()
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].IsStop = false;
        optic.SurfaceGroup.Items[2].IsStop = true; optic.RayAimingEnabled = true;
        var frozen = Snapshot(optic);
        Assert.Throws<NotSupportedException>(() => AnalysisSurfacePreparation.Prepare(optic, 1));
        Assert.Equal(frozen, Snapshot(optic));
    }

    [Theory]
    [InlineData(FieldDefinitionKind.ParaxialImageHeight, true)]
    [InlineData(FieldDefinitionKind.ParaxialImageHeight, false)]
    [InlineData(FieldDefinitionKind.RealImageHeight, true)]
    [InlineData(FieldDefinitionKind.RealImageHeight, false)]
    public void IntermediateImageConvertsFieldsUsingTheOriginalPrimaryChief(FieldDefinitionKind definition, bool infinite)
    {
        var optic = Lens(); optic.FieldDefinition = definition;
        optic.SurfaceGroup.Items[0].Thickness = infinite ? double.PositiveInfinity : 200;
        optic.SurfaceGroup.Renumber();
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = .1 });
        var primary = optic.Wavelengths.First(w => w.IsPrimary);
        var original = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 1, 0, 0, primary.Micrometers).Rays[0];
        var objectFrame = optic.SurfaceGroup.Items[0].CoordinateSystem;
        var point = objectFrame.ToLocalPoint(original.Origin); var direction = objectFrame.ToLocalDirection(original.Direction);
        var frozen = Snapshot(optic); var prepared = AnalysisSurfacePreparation.Prepare(optic, 2);
        Assert.Equal(infinite ? FieldDefinitionKind.Angle : FieldDefinitionKind.ObjectHeight, prepared.FieldDefinition);
        Assert.Equal(infinite ? Math.Atan2(direction.Y, direction.Z) * 180 / Math.PI : point.Y, prepared.Fields[0].Y, 10);
        Assert.Equal(frozen, Snapshot(optic));
    }

    [Fact]
    public void AfocalSelectionDoesNotMoveToAParaxialFocus()
    {
        var optic = Lens(); optic.ImageSpaceAfocal = true;
        var prepared = AnalysisSurfacePreparation.Prepare(optic, 2);
        Assert.Equal(0, prepared.SurfaceGroup.Items[^2].Thickness);
        Assert.Equal(prepared.SurfaceGroup.Items[^2].CoordinateSystem.Origin, prepared.SurfaceGroup.Items[^1].CoordinateSystem.Origin);
        Assert.True(prepared.ImageSpaceAfocal);
    }

    [Theory]
    [InlineData(ReferenceOpdMode.Absolute)] [InlineData(ReferenceOpdMode.Absolute2)]
    public void FocalAbsoluteUsesPhaseInclusiveImageOpticalPathWithoutSphereCorrection(ReferenceOpdMode mode)
    {
        var optic = Example(); optic.ReferenceOpd = mode; var frozen = Snapshot(optic);
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        (double X, double Y)[] pupils = [(0, 0), (0, .3), (0, .8), (-.7, 0)];
        var result = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 1), wave, pupils, optic.RayAimingEnabled);
        double Path(double x, double y)
        {
            var ray = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator
                .GenerateGeneric(0, 1, x, y, wave.Micrometers, optic.RayAimingEnabled)).Single()!;
            return ray.PhaseInclusiveOpticalPathLength ?? ray.CumulativeOpticalPathLength;
        }
        var chief = Path(0, 0);
        var traced = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator
            .GenerateNormalizedPupilSamples(0, 1, wave.Micrometers,
                pupils.Select(p => new PupilSample(p.X, p.Y, 1)).ToArray(), optic.RayAimingEnabled));
        for (var i = 0; i < pupils.Length; i++)
        {
            var ray = traced[i]!;
            var expected = (chief - (ray.PhaseInclusiveOpticalPathLength ?? ray.CumulativeOpticalPathLength)) / (wave.Micrometers * .001);
            Assert.Equal(expected, result.Samples[i].OpdWaves, 12);
        }
        Assert.Null(result.ReferenceSphere); Assert.Equal(mode, result.ReferenceOpd);
        var data = Evaluate(optic, "Optical Path Difference", -1);
        Assert.Equal("image plane", data.Values["ReferenceGeometry"]);
        Assert.Equal(frozen, Snapshot(optic));
    }

    [Theory]
    [InlineData(ReferenceOpdMode.Absolute)] [InlineData(ReferenceOpdMode.Absolute2)] [InlineData(ReferenceOpdMode.Infinity)]
    public void DisplayReferenceDoesNotReplaceTheDiffractionPupilPhase(ReferenceOpdMode mode)
    {
        var optic = Lens(); var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var fft = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 16, 32);
        var huygens = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 16, 8, .001);
        var mtf = DiffractionEngine.ComputeFftMtf(fft, optic, wave);
        optic.ReferenceOpd = mode;
        var actual = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 16, 32);
        Assert.Equal(fft.Values.Cast<double>(), actual.Values.Cast<double>());
        Assert.Equal(mtf.Tangential, DiffractionEngine.ComputeFftMtf(actual, optic, wave).Tangential);
        Assert.Equal(huygens.Values.Cast<double>(), DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 16, 8, .001).Values.Cast<double>());
    }

    [Fact]
    public void UnsupportedFocalInfinityCannotSilentlyReturnExitPupilOpd()
    {
        var optic = Lens(); optic.ReferenceOpd = ReferenceOpdMode.Infinity;
        Assert.Throws<NotSupportedException>(() => Evaluate(optic, "Optical Path Difference", -1));
    }

    [Theory]
    [InlineData(ReferenceOpdMode.Absolute)] [InlineData(ReferenceOpdMode.Absolute2)]
    public void AnAbsoluteDisplayCannotBeSuppliedAsPreparedDiffractionPhase(ReferenceOpdMode mode)
    {
        var optic = Lens(); optic.ReferenceOpd = mode;
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var display = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 16);
        Assert.Throws<ArgumentException>(() => DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 16, 32,
            preparedWavefront: display));
    }

    [Fact]
    public void WavefrontPrimaryReferenceIsARealCalculationOptionAndPreservesTheSource()
    {
        var optic = Example(); var frozen = Snapshot(optic); var runtime = new WorkbenchRuntime(optic);
        var primary = optic.Wavelengths.Select((w, i) => (w, i)).First(p => p.w.IsPrimary).i + 1;
        var other = primary == 1 ? 2 : 1;
        AnalysisData Map(int wave, bool usePrimary) => runtime.BuildAnalysisData("Wavefront", new Dictionary<string, string>
        { ["Sampling"] = "32", ["FieldNumber"] = "3", ["WavelengthNumber"] = wave.ToString(),
            ["UseExitPupilShape"] = "False", ["ReferenceToPrimary"] = usePrimary.ToString() });
        Assert.Equal(Numbers(Map(primary, false)), Numbers(Map(primary, true)));
        Assert.NotEqual(Numbers(Map(other, false)), Numbers(Map(other, true)));
        var referred = Map(other, true);
        Assert.Equal(true, referred.Values["ReferenceToPrimary"]);
        Assert.Equal(optic.Wavelengths[primary - 1].Micrometers, referred.Values["ReferenceWavelengthMicrometers"]);
        Assert.Contains(runtime.GetAnalysisParameters("Wavefront"), p => p.Key == "ReferenceToPrimary");
        Assert.Equal(frozen, Snapshot(optic));
    }

    private static string Numbers(AnalysisData data) => JsonSerializer.Serialize(
        (data.PlotPanes is { Count: > 0 } ? data.PlotPanes.SelectMany(p => p.Series) : data.PlotSeries)
        .Select(s => new { s.XQuantity, s.YQuantity, s.XUnit, s.YUnit, s.Points }), Json);

    private static AnalysisData Evaluate(Optic optic, string key, int surface) => key switch
    {
        "Optical Path Difference" => new OpticalPathDifferenceAnalysis(optic, wavelengthNumber: 2, fieldNumber: 1, surfaceNumber: surface).GenerateData(),
        "Wavefront" => new WavefrontAnalysis(optic, pupilSampling: 16, wavelengthNumber: 2, fieldNumber: 1,
            useExitPupilShape: false, surfaceNumber: surface).GenerateData(),
        "PSF" => new PsfAnalysis(optic, numRays: 16, gridSize: 32, wavelengthNumber: 2, fieldNumber: 1, surfaceNumber: surface).GenerateData(),
        "MTF" => new MtfAnalysis(optic, numRays: 16, gridSize: 32, wavelengthNumber: 2, fieldNumber: 1, surfaceNumber: surface).GenerateData(),
        _ => throw new ArgumentException(key)
    };

    private static Optic Example() => OpticalFormatCatalog.Import(EvidenceFiles.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "definition-settings", "example2.zmx")), ".zmx");

    private static Optic Lens(double focalLength = 50)
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Geometry = new PlaneGeometry(), InteractionModel = new ThinLensInteractionModel(focalLength),
                IsStop = true, SemiDiameter = 5, Thickness = 20 },
            new OpticalSurface { Geometry = new PlaneGeometry(), SemiDiameter = 5, Thickness = 40 },
            new OpticalSurface { Geometry = new PlaneGeometry(), SemiDiameter = 100 }
        ]);
        optic.Aperture.Value = 2; optic.Fields.Clear(); optic.Fields.Add(new FieldPoint());
        return optic;
    }
}
