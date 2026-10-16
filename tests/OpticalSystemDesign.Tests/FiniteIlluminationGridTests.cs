using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class FiniteIlluminationGridTests
{
    [Theory]
    [InlineData(.3, 5)]
    [InlineData(.3, 10)]
    [InlineData(.3, 18)]
    [InlineData(.3, 20)]
    [InlineData(.3, 40)]
    [InlineData(.3, 100)]
    [InlineData(.5, 5)]
    [InlineData(.5, 10)]
    [InlineData(.5, 17)]
    [InlineData(.5, 18)]
    [InlineData(.5, 19)]
    [InlineData(.5, 20)]
    [InlineData(.5, 21)]
    [InlineData(.5, 40)]
    [InlineData(.5, 100)]
    [InlineData(.5, 101)]
    [InlineData(.5, 128)]
    public void IndependentNativeSquareControlsResolveCoarseEnvelopeAndSamplingCap(double halfWidth, int density)
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var folder = Path.Combine(root, "validation/zemax/2026-r1/illumination-finite-grid-2026-10-16");
        using var control = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "controls.json")));
        var source = control.RootElement.GetProperty("source");
        Assert.Equal(source.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(
            File.ReadAllBytes(Path.Combine(folder, source.GetProperty("file").GetString()!)))));
        var native = control.RootElement.GetProperty("rows").EnumerateArray().Single(row =>
            row.GetProperty("halfWidth").GetDouble() == halfWidth && row.GetProperty("density").GetInt32() == density);
        var optic = Lens(1000);
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(halfWidth, halfWidth);
        var result = IlluminationMetrics.Evaluate(optic, (0, 0), .55, density,
            sampling: IlluminationSamplingKind.FiniteImageCosine, includeScalarTransmission: false);
        // These square controls have constant interior weights, independently
        // isolating grid placement. This numerical identity is not an EFNO gate
        // for arbitrary native lens files or the original four-file matrix.
        Assert.InRange(Math.Abs(result.EffectiveFNumber / native.GetProperty("value").GetDouble() - 1), 0, 1e-12);
        Assert.Equal(IlluminationSamplingKind.FiniteImageCosine, result.Sampling);
        Assert.Equal((2 * Math.Min(density, 100) + 1) * (2 * Math.Min(density, 100) + 1), result.IntegrationSamples);
        var operand = MeritFunctionCatalog.Evaluate(optic, Row(density));
        Assert.Empty(operand.Error);
        Assert.Equal(result.EffectiveFNumber, operand.Value, 12);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void CircularConeMatchesIndependentNativeFiniteMappingAndEdgeWeights(int density)
    {
        const double focalLength = 1000;
        var optic = Lens(focalLength);
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "validation/zemax/2026-r1/illumination-inverse-2026-10-16/controls.json")));
        var expected = native.RootElement.GetProperty("circularGrid").EnumerateArray()
            .Single(row => row.GetProperty("density").GetInt32() == density).GetProperty("value").GetDouble();
        var actual = IlluminationMetrics.Evaluate(optic, (0, 0), .55, density,
            sampling: IlluminationSamplingKind.FiniteImageCosine);
        // The captured finite profile approximates the inverse with a truncated
        // SVD map. Tight inverse geometry remains tested by the generic mode;
        // it cannot be substituted for the native finite-node convention.
        Assert.InRange(Math.Abs(actual.EffectiveFNumber / expected - 1), 0, 1e-12);
    }

    [Fact]
    public void GenericUniformPhysicalGridRetainsItsCellCenterContract()
    {
        var optic = Lens(1000);
        var result = IlluminationMetrics.Evaluate(optic, (0, 0), .55, 10,
            sampling: IlluminationSamplingKind.UniformImageCosine);
        Assert.Equal(100, result.IntegrationSamples);
        Assert.Equal(80, result.ValidRays);
        Assert.Equal(80 * .04 / 1_000_001, result.ProjectedCosineArea, 15);
    }

    [Fact]
    public void EntirelyBlockedEnvelopeKeepsSamplingMetadataAndFailsTheOperand()
    {
        var optic = Lens(1000);
        optic.SurfaceGroup.Items[1].PhysicalAperture = new OffsetRadialAperture(.1, offsetX: 5);
        var result = IlluminationMetrics.Evaluate(optic, (0, 0), .55, 10,
            sampling: IlluminationSamplingKind.FiniteImageCosine);
        Assert.Equal(0, result.ProjectedCosineArea);
        Assert.Equal(IlluminationSamplingKind.FiniteImageCosine, result.Sampling);
        Assert.Equal(441, result.IntegrationSamples);
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, Row(10)).Error);
    }

    [Fact]
    public void FiniteReferenceChangesWithDetectorDistanceWhilePhysicalConeIsUnchanged()
    {
        var optic = Lens(1000);
        optic.SurfaceGroup.Items[1].InteractionModel = new RefractiveReflectiveInteractionModel();
        optic.SurfaceGroup.Items[0].Thickness = 10;
        optic.SurfaceGroup.Items[1].Thickness = 10;
        optic.SurfaceGroup.Renumber();
        var physical = IlluminationMetrics.Evaluate(optic, (0, 0), .55).EffectiveFNumber;
        var referenced = MeritFunctionCatalog.Evaluate(optic, Row(10));
        Assert.Empty(referenced.Error);
        optic.SurfaceGroup.Items[1].Thickness = 12;
        optic.SurfaceGroup.Renumber();
        Assert.Equal(physical, IlluminationMetrics.Evaluate(optic, (0, 0), .55).EffectiveFNumber, 12);
        var moved = MeritFunctionCatalog.Evaluate(optic, Row(10));
        Assert.Empty(moved.Error);
        Assert.True(moved.Value > referenced.Value * 1.1);
    }

    private static Optic Lens(double focalLength)
    {
        var optic = new Optic(); optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint()); optic.Wavelengths.Add(new Wavelength { Nanometers = 550 });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { IsStop = true, SemiDiameter = 1, Thickness = focalLength,
                InteractionModel = new ThinLensInteractionModel(focalLength) },
            new OpticalSurface()
        ]);
        return optic;
    }

    private static MeritOperandDefinition Row(int density) => new()
    {
        Type = "EFNO",
        ZemaxIntegerParameters = [density, 1],
        ZemaxDataParameters = [1, 0, 0, 0]
    };
}
