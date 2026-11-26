using System.Numerics;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class FftMtfProjectionTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void ComplexAxesMatchAnIndependentTwoDimensionalDft(int size)
    {
        var values = new double[size, size];
        for (var row = 0; row < size; row++)
            for (var column = 0; column < size; column++)
                values[row, column] = .1 + Math.Pow(Math.Sin(.31 * row + .47 * column), 2)
                    + (row + 2d * column) / (size * size);
        var optic = Optic.CreateCookeTriplet();
        var psf = new PsfResult(values, size, size, 4, 1);
        var mtf = DiffractionEngine.ComputeFftMtf(psf, optic, optic.Wavelengths[0]);
        var power = values.Cast<double>().Sum();
        var center = size / 2;
        for (var frequency = 0; frequency < size - center; frequency++)
        {
            var tangential = Complex.Zero;
            var sagittal = Complex.Zero;
            for (var row = 0; row < size; row++)
                for (var column = 0; column < size; column++)
                {
                    tangential += values[row, column] * Complex.FromPolarCoordinates(1,
                        -2 * Math.PI * frequency * (row - center) / size);
                    sagittal += values[row, column] * Complex.FromPolarCoordinates(1,
                        -2 * Math.PI * frequency * (column - center) / size);
                }
            Assert.InRange((mtf.TangentialOtf![frequency] - tangential / power).Magnitude, 0, 2e-12);
            Assert.InRange((mtf.SagittalOtf![frequency] - sagittal / power).Magnitude, 0, 2e-12);
            Assert.Equal(mtf.TangentialOtf[frequency].Magnitude, mtf.Tangential[frequency]);
            Assert.Equal(mtf.SagittalOtf[frequency].Magnitude, mtf.Sagittal[frequency]);
        }
    }

    [Fact]
    public void ShiftedPointPreservesComplexPhaseAndUnitModulation()
    {
        const int size = 32;
        var values = new double[size, size];
        values[size / 2 + 3, size / 2 - 5] = 7;
        var optic = Optic.CreateCookeTriplet();
        var result = DiffractionEngine.ComputeFftMtf(new(values, size, size, 4, 1), optic, optic.Wavelengths[0]);
        for (var index = 0; index < size / 2; index++)
        {
            Assert.InRange((result.TangentialOtf![index] - Complex.FromPolarCoordinates(1,
                -2 * Math.PI * index * 3 / size)).Magnitude, 0, 2e-12);
            Assert.InRange((result.SagittalOtf![index] - Complex.FromPolarCoordinates(1,
                2 * Math.PI * index * 5 / size)).Magnitude, 0, 2e-12);
            Assert.InRange(Math.Abs(result.Tangential[index] - 1), 0, 2e-12);
            Assert.InRange(Math.Abs(result.Sagittal[index] - 1), 0, 2e-12);
        }
    }

    [Fact]
    public void EmptyPsfKeepsZeroOtf()
    {
        var optic = Optic.CreateCookeTriplet();
        var result = DiffractionEngine.ComputeFftMtf(new(new double[32, 32], 32, 32, 4, 1), optic, optic.Wavelengths[0]);
        Assert.All(result.TangentialOtf!, value => Assert.Equal(Complex.Zero, value));
        Assert.All(result.SagittalOtf!, value => Assert.Equal(Complex.Zero, value));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(10)]
    public void NonPowerGridRetainsTheExistingRejection(int size)
    {
        var optic = Optic.CreateCookeTriplet();
        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.ComputeFftMtf(
            new(new double[size, size], size, size, 4, 1), optic, optic.Wavelengths[0]));
    }

    [Fact]
    public void ProjectionCancellationRetainsItsRequestToken()
    {
        var optic = Optic.CreateCookeTriplet();
        var psf = new PsfResult(new double[32, 32], 32, 32, 4, 1);
        using var cancellation = new CancellationTokenSource();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        var checkpoints = 0;
        using var observer = ComputationCancellation.ObserveCheckpoints(() =>
        {
            if (++checkpoints == 20) cancellation.Cancel();
        });
        var error = Assert.Throws<OperationCanceledException>(() =>
            DiffractionEngine.ComputeFftMtf(psf, optic, optic.Wavelengths[0]));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }
}
