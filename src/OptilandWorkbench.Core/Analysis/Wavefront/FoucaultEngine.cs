using System.Numerics;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public enum FoucaultKnifeEdge { HorizontalAbove, HorizontalBelow, VerticalLeft, VerticalRight }

public sealed record FoucaultResult(double[,] Intensity, int FourierGridSize,
    double ImageSpacingXMicrometers, double ImageSpacingYMicrometers,
    double InputPower, double OutputPower, double DisplayedPower, int IlluminatedSamples,
    int VignettedSamples, int CoherentChannels)
{
    public double KnifeThroughput => OutputPower / InputPower;
}

/// <summary>Focal FFT knife test followed by ideal pupil reimaging. Homogeneous-media
/// Jones transport comes from the formal tracer; this is not general vector POP.</summary>
public static class FoucaultEngine
{
    public static FoucaultResult Compute(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int sampling, FoucaultKnifeEdge knife, double positionMicrometers,
        bool usePolarization = false)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(wavelength);
        if (sampling is < 8 or > 512) throw new ArgumentOutOfRangeException(nameof(sampling));
        if (!double.IsFinite(positionMicrometers)) throw new ArgumentOutOfRangeException(nameof(positionMicrometers));
        if (!Enum.IsDefined(knife)) throw new ArgumentOutOfRangeException(nameof(knife));
        if (optic.ImageSpaceAfocal)
            throw new NotSupportedException("Foucault 的 µm 刀口位于有焦像面；无焦系统须先定义聚焦/再成像光学系统。");
        var wavefront = WavefrontEngine.GenerateChiefRayUniform(optic, field, wavelength,
            sampling, cellCentered: true, aimAtStop: optic.RayAimingEnabled);
        var axes = DiffractionEngine.WorkingFNumbers(optic, field, wavelength, optic.RayAimingEnabled);
        var gridSize = AnalysisResourceLimits.RoundUpPowerOfTwo(checked(4 * sampling), nameof(sampling));
        // Pupil pitch is 2/N; padding changes image pitch, not pupil density.
        var spacingX = wavelength.Micrometers * axes.Sagittal * sampling / gridSize;
        var spacingY = wavelength.Micrometers * axes.Tangential * sampling / gridSize;
        var states = !usePolarization ? Array.Empty<JonesInputState>()
            : optic.Polarization.Unpolarized
                ? new[] { new JonesInputState(1, 0, ReferenceAxis: optic.Polarization.ReferenceAxis),
                    new JonesInputState(0, 1, ReferenceAxis: optic.Polarization.ReferenceAxis) }
                : new[] { optic.Polarization.Input };
        var channels = Enumerable.Range(0, usePolarization ? states.Length * 3 : 1)
            .Select(_ => new Complex[sampling, sampling]).ToArray();
        var illuminated = 0;
        foreach (var sample in wavefront.Samples)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (!double.IsFinite(sample.Intensity) || sample.Intensity < 0 || !double.IsFinite(sample.OpdWaves))
                throw new ArithmeticException("Foucault 光瞳强度和 OPD 必须为有限值。");
            if (sample.Intensity <= 0) continue;
            var column = (int)Math.Round(((sample.NormalizedPupilX + 1) * sampling - 1) / 2);
            var row = (int)Math.Round(((sample.NormalizedPupilY + 1) * sampling - 1) / 2);
            // Use the formal FFT's conjugate-field convention: -2π OPD with a
            // negative-exponent transform. The physical positive-time pupil is its
            // conjugate, so image coordinates and knife directions retain their sign.
            var phase = Complex.FromPolarCoordinates(1, -2 * Math.PI * Math.IEEERemainder(sample.OpdWaves, 1));
            if (!usePolarization)
                channels[0][row, column] = Math.Sqrt(sample.Intensity) * phase;
            else
            {
                var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(field.Hx, field.Hy,
                    sample.NormalizedPupilX, sample.NormalizedPupilY, wavelength.Micrometers,
                    optic.RayAimingEnabled).Rays.Single();
                for (var state = 0; state < states.Length; state++)
                {
                    // Coating phase and power occur once; referenced OPD supplies OPL phase.
                    var transported = optic.SequentialRayTracer.TracePolarized(ray, states[state],
                        includePropagationPhase: false).LocalField;
                    var scale = phase / Math.Sqrt(states.Length);
                    channels[3 * state][row, column] = Complex.Conjugate(transported.X) * scale;
                    channels[3 * state + 1][row, column] = Complex.Conjugate(transported.Y) * scale;
                    channels[3 * state + 2][row, column] = Complex.Conjugate(transported.Z) * scale;
                }
            }
            illuminated++;
        }
        var inputPower = channels.Sum(channel => channel.Cast<Complex>().Sum(Power));
        if (!(inputPower > 0) || !double.IsFinite(inputPower))
            throw new AnalysisDataUnavailableException("Foucault Analysis", "no illuminated pupil reached the focal surface");
        var intensity = new double[sampling, sampling];
        var outputPower = 0d;
        var offset = (gridSize - sampling) / 2;
        foreach (var channel in channels)
        {
            var nearField = FilterPupil(channel, gridSize, spacingX, spacingY, knife, positionMicrometers);
            outputPower += nearField.Cast<Complex>().Sum(Power);
            for (var row = 0; row < sampling; row++)
            for (var column = 0; column < sampling; column++)
                intensity[row, column] += Power(nearField[row + offset, column + offset]);
        }
        // Do not remask diffraction outside the input circle. Full padded-grid energy
        // and the displayed central square are different quantities.
        return new(intensity, gridSize, spacingX, spacingY, inputPower, outputPower,
            intensity.Cast<double>().Sum(), illuminated, wavefront.VignettedRayCount, channels.Length);
    }

    internal static Complex[,] FilterPupil(Complex[,] pupil, int gridSize, double spacingX, double spacingY,
        FoucaultKnifeEdge knife, double position)
    {
        var sampling = pupil.GetLength(0);
        if (sampling != pupil.GetLength(1)) throw new ArgumentException("Pupil must be square.", nameof(pupil));
        AnalysisResourceLimits.ValidateFftGrid(sampling, gridSize);
        if (!double.IsFinite(spacingX) || !double.IsFinite(spacingY) || spacingX <= 0 || spacingY <= 0
            || !double.IsFinite(position) || !Enum.IsDefined(knife))
            throw new ArgumentException("Invalid focal-grid or knife coordinates.");
        if (pupil.Cast<Complex>().Any(value => !double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary)))
            throw new ArgumentException("Pupil field must be finite.", nameof(pupil));
        var padded = new Complex[gridSize, gridSize];
        var offset = (gridSize - sampling) / 2;
        for (var row = 0; row < sampling; row++)
        for (var column = 0; column < sampling; column++)
            padded[row + offset, column + offset] = pupil[row, column];
        DiffractionEngine.FourierTransform2D(padded);
        var horizontal = knife is FoucaultKnifeEdge.HorizontalAbove or FoucaultKnifeEdge.HorizontalBelow;
        for (var row = 0; row < gridSize; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var column = 0; column < gridSize; column++)
            {
                var index = horizontal ? row : column;
                var frequency = index < gridSize / 2 ? index : index - gridSize;
                var spacing = horizontal ? spacingY : spacingX;
                // Average the physical half-plane amplitude over the frequency cell.
                var leftFraction = Math.Clamp(.5 + (position - frequency * spacing) / spacing, 0, 1);
                var transmission = knife is FoucaultKnifeEdge.HorizontalAbove or FoucaultKnifeEdge.VerticalRight
                    ? leftFraction : 1 - leftFraction;
                padded[row, column] *= transmission;
            }
        }
        DiffractionEngine.FourierTransform2D(padded, inverse: true);
        return padded;
    }

    private static double Power(Complex value) => value.Real * value.Real + value.Imaginary * value.Imaginary;
}
