using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Tests;

public sealed class MeritFunctionRmsSpotTests
{
    [Fact]
    public void DefaultRmsSpotUsesZemaxSignedRayAberrationOperands()
    {
        var optic = Optic.CreateCookeTriplet();

        var operands = MeritFunctionCatalog.CreateDefaultRmsSpot(optic);
        var spotOperands = operands.Where(operand => operand.Enabled).ToArray();

        Assert.NotEmpty(spotOperands);
        Assert.DoesNotContain(spotOperands, operand => operand.Type.StartsWith("RS", StringComparison.Ordinal));
        Assert.All(spotOperands, operand =>
        {
            Assert.Contains(operand.Type, new[] { "TRCX", "TRCY" });
            Assert.InRange(operand.Field, 1, optic.Fields.Count);
            Assert.InRange(operand.Wavelength, 1, optic.Wavelengths.Count);
            Assert.Equal("gaussian_quad", operand.PupilSampling);
        });
    }

    [Fact]
    public void RectangularWizardUsesRectangularSignedOperands()
    {
        var optic = Optic.CreateCookeTriplet();

        var operands = MeritFunctionCatalog.CreateFromWizard(optic, new MeritFunctionWizardSettings(
            MeritImageQuality.RmsSpot,
            MeritPupilSampling.RectangularArray,
            PupilRings: 4,
            PupilArms: 8,
            PupilObscuration: 0,
            WeightScale: 1,
            UseAllWavelengths: true,
            IncludeCommonOperands: false));

        var spotOperands = operands.Where(operand => operand.Enabled).ToArray();
        Assert.NotEmpty(spotOperands);
        Assert.All(spotOperands, operand =>
        {
            Assert.Contains(operand.Type, new[] { "TRCX", "TRCY" });
            Assert.Equal("uniform", operand.PupilSampling);
        });
    }

    [Theory]
    [InlineData(MeritPupilSampling.GaussianQuadrature, MeritSpotReference.Centroid, "TRCX", "TRCY", "gaussian_quad")]
    [InlineData(MeritPupilSampling.GaussianQuadrature, MeritSpotReference.ChiefRay, "TRAX", "TRAY", "gaussian_quad")]
    [InlineData(MeritPupilSampling.RectangularArray, MeritSpotReference.Centroid, "TRCX", "TRCY", "uniform")]
    [InlineData(MeritPupilSampling.RectangularArray, MeritSpotReference.ChiefRay, "TRAX", "TRAY", "uniform")]
    public void WizardMapsSamplingAndReferenceToZemaxOperand(
        MeritPupilSampling sampling,
        MeritSpotReference reference,
        string expectedXType,
        string expectedYType,
        string expectedSampling)
    {
        var optic = Optic.CreateCookeTriplet();

        var operands = MeritFunctionCatalog.CreateFromWizard(optic, new MeritFunctionWizardSettings(
            MeritImageQuality.RmsSpot,
            sampling,
            PupilRings: 3,
            PupilArms: 6,
            PupilObscuration: 0,
            WeightScale: 1,
            UseAllWavelengths: true,
            IncludeCommonOperands: false,
            Reference: reference));

        var active = operands.Where(operand => operand.Enabled).ToArray();
        Assert.Contains(active, operand => operand.Type == expectedXType);
        Assert.Contains(active, operand => operand.Type == expectedYType);
        Assert.All(active, operand =>
        {
            Assert.Contains(operand.Type, new[] { expectedXType, expectedYType });
            Assert.Equal(expectedSampling, operand.PupilSampling);
        });
    }

    [Theory]
    [InlineData(MeritSpotReference.Centroid, "TRAC")]
    [InlineData(MeritSpotReference.ChiefRay, "TRAR")]
    public void ZeroAxisWeightsUseRadialSpotOperand(MeritSpotReference reference, string expectedType)
    {
        var optic = Optic.CreateCookeTriplet();

        var operands = MeritFunctionCatalog.CreateFromWizard(optic, new MeritFunctionWizardSettings(
            MeritImageQuality.RmsSpot,
            MeritPupilSampling.GaussianQuadrature,
            PupilRings: 3,
            PupilArms: 6,
            PupilObscuration: 0,
            WeightScale: 1,
            UseAllWavelengths: false,
            IncludeCommonOperands: false,
            Reference: reference,
            XWeight: 0,
            YWeight: 0));

        Assert.All(operands.Where(operand => operand.Enabled), operand => Assert.Equal(expectedType, operand.Type));
    }

    [Fact]
    public void AngularWizardCreatesAndEvaluatesAngularAberrationOperands()
    {
        var optic = Optic.CreateCookeTriplet();
        var operands = MeritFunctionCatalog.CreateFromWizard(optic, new MeritFunctionWizardSettings(
            MeritImageQuality.Angular,
            MeritPupilSampling.GaussianQuadrature,
            PupilRings: 2,
            PupilArms: 6,
            PupilObscuration: 0,
            WeightScale: 1,
            UseAllWavelengths: false,
            IncludeCommonOperands: false));
        var active = operands.Where(operand => operand.Enabled).ToArray();

        Assert.Contains(active, operand => operand.Type == "ANCX");
        Assert.Contains(active, operand => operand.Type == "ANCY");
        Assert.All(active.Take(6), operand =>
        {
            var evaluation = MeritFunctionCatalog.Evaluate(optic, operand);
            Assert.Empty(evaluation.Error);
            Assert.True(double.IsFinite(evaluation.Value));
        });
    }

    [Fact]
    public void ContrastWizardCreatesMooreElliottPairsThatEvaluate()
    {
        var optic = Optic.CreateCookeTriplet();
        var operands = MeritFunctionCatalog.CreateFromWizard(optic, new MeritFunctionWizardSettings(
            MeritImageQuality.Contrast,
            MeritPupilSampling.GaussianQuadrature,
            PupilRings: 3,
            PupilArms: 6,
            PupilObscuration: 0,
            WeightScale: 1,
            UseAllWavelengths: false,
            IncludeCommonOperands: false,
            SpatialFrequency: 30));
        var active = operands.Where(operand => operand.Enabled).ToArray();

        Assert.Contains(active, operand => operand.Type == "MECS");
        Assert.Contains(active, operand => operand.Type == "MECT");
        Assert.All(active.Take(6), operand =>
        {
            var evaluation = MeritFunctionCatalog.Evaluate(optic, operand);
            Assert.Empty(evaluation.Error);
            Assert.True(double.IsFinite(evaluation.Value));
        });
    }

    [Fact]
    public void BatchedEvaluationMatchesIndependentOperandEvaluation()
    {
        var optic = Optic.CreateCookeTriplet();
        var operands = MeritFunctionCatalog.CreateDefaultRmsSpot(optic)
            .Where(operand => operand.Enabled)
            .Take(18)
            .ToArray();
        var independent = operands
            .Select(operand => MeritFunctionCatalog.Evaluate(optic, operand).Value)
            .ToArray();

        double[] batched;
        using (MeritFunctionCatalog.BeginEvaluationBatch())
        {
            batched = operands
                .Select(operand => MeritFunctionCatalog.Evaluate(optic, operand).Value)
                .ToArray();
        }

        Assert.Equal(independent.Length, batched.Length);
        for (var index = 0; index < independent.Length; index++)
        {
            Assert.Equal(independent[index], batched[index], precision: 12);
        }
    }

    [Fact]
    public void PolychromaticRmsUsesPrimaryWavelengthCentroid()
    {
        var optic = Optic.CreateCookeTriplet();
        var definition = new MeritOperandDefinition
        {
            Type = "RSCE",
            Field = 2,
            Wavelength = 0,
            PupilRings = 2,
            PupilArms = 6,
            PupilSampling = "hexapolar"
        };

        var actual = MeritFunctionCatalog.Evaluate(optic, definition);
        var expected = CalculateOptilandPolychromaticRms(optic, definition.Field);

        Assert.Empty(actual.Error);
        Assert.Equal(expected, actual.Value, precision: 12);
    }

    [Fact]
    public void GaussianRmsUsesQuadratureRayWeights()
    {
        var optic = Optic.CreateCookeTriplet();
        var primaryIndex = optic.Wavelengths
            .Select((wavelength, index) => (wavelength, index))
            .First(item => item.wavelength.IsPrimary).index;
        var definition = new MeritOperandDefinition
        {
            Type = "RSCE",
            Field = 2,
            Wavelength = primaryIndex + 1,
            PupilRings = 3,
            PupilArms = 6,
            PupilSampling = "gaussian_quad"
        };

        var actual = MeritFunctionCatalog.Evaluate(optic, definition);
        var expected = CalculateGaussianRms(optic, definition.Field, primaryIndex);

        Assert.Empty(actual.Error);
        Assert.Equal(expected, actual.Value, precision: 12);
    }

    private static double CalculateOptilandPolychromaticRms(Optic optic, int oneBasedField)
    {
        var field = optic.Fields[oneBasedField - 1];
        var normalized = FieldCoordinates.Normalize(optic.Fields, field.X, field.Y);
        var pupilSamples = new List<PupilSample> { new(0, 0, 1) };
        const int rings = 2;
        const int arms = 6;
        for (var ring = 1; ring <= rings; ring++)
        {
            var radius = ring / (double)rings;
            var points = ring * arms;
            for (var index = 0; index < points; index++)
            {
                var angle = 2 * Math.PI * index / points;
                pupilSamples.Add(new PupilSample(
                    radius * Math.Cos(angle),
                    radius * Math.Sin(angle),
                    1));
            }
        }

        var samplesByWavelength = optic.Wavelengths
            .Select(wavelength => TraceFinalSamples(optic, normalized, wavelength.Micrometers, pupilSamples))
            .ToArray();
        var primaryIndex = optic.Wavelengths
            .Select((wavelength, index) => (wavelength, index))
            .First(item => item.wavelength.IsPrimary).index;
        var referenceSamples = samplesByWavelength[primaryIndex];
        var referenceWeight = referenceSamples.Sum(sample => sample.Intensity);
        var centroidX = referenceSamples.Sum(sample => sample.Position.X * sample.Intensity) / referenceWeight;
        var centroidY = referenceSamples.Sum(sample => sample.Position.Y * sample.Intensity) / referenceWeight;
        var allSamples = samplesByWavelength.SelectMany(samples => samples).ToArray();
        var totalWeight = allSamples.Sum(sample => sample.Intensity);
        return Math.Sqrt(allSamples.Sum(sample =>
            (((sample.Position.X - centroidX) * (sample.Position.X - centroidX))
             + ((sample.Position.Y - centroidY) * (sample.Position.Y - centroidY))) * sample.Intensity) / totalWeight);
    }

    private static double CalculateGaussianRms(Optic optic, int oneBasedField, int wavelengthIndex)
    {
        var field = optic.Fields[oneBasedField - 1];
        var normalized = FieldCoordinates.Normalize(optic.Fields, field.X, field.Y);
        var radialSamples = new[]
        {
            (Radius: Math.Sqrt((1 - Math.Sqrt(3.0 / 5.0)) / 2), Weight: 5.0 / 18.0),
            (Radius: Math.Sqrt(0.5), Weight: 4.0 / 9.0),
            (Radius: Math.Sqrt((1 + Math.Sqrt(3.0 / 5.0)) / 2), Weight: 5.0 / 18.0)
        };
        const int arms = 6;
        var pupilSamples = radialSamples
            .SelectMany(radialSample => Enumerable.Range(0, arms).Select(index =>
            {
                var angle = 2 * Math.PI * (index + 1) / arms;
                return new PupilSample(
                    radialSample.Radius * Math.Cos(angle),
                    radialSample.Radius * Math.Sin(angle),
                    radialSample.Weight / arms);
            }))
            .ToArray();
        var samples = TraceFinalSamples(
            optic,
            normalized,
            optic.Wavelengths[wavelengthIndex].Micrometers,
            pupilSamples);
        var totalWeight = samples.Sum(sample => sample.Intensity);
        var centroidX = samples.Sum(sample => sample.Position.X * sample.Intensity) / totalWeight;
        var centroidY = samples.Sum(sample => sample.Position.Y * sample.Intensity) / totalWeight;
        return Math.Sqrt(samples.Sum(sample =>
            (((sample.Position.X - centroidX) * (sample.Position.X - centroidX))
             + ((sample.Position.Y - centroidY) * (sample.Position.Y - centroidY))) * sample.Intensity) / totalWeight);
    }

    private static RayTraceSample[] TraceFinalSamples(
        Optic optic,
        (double X, double Y) normalized,
        double wavelengthMicrometers,
        IReadOnlyList<PupilSample> pupilSamples)
    {
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            normalized.X,
            normalized.Y,
            wavelengthMicrometers,
            pupilSamples);
        return optic.SequentialRayTracer.Trace(bundle).RayHistories
            .Where(history => history.Count > 0)
            .Select(history => history[^1])
            .Where(sample => !sample.Vignetted && sample.Intensity > 0)
            .ToArray();
    }
}
