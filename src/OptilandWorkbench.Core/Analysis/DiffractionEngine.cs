using System.Numerics;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed record PsfResult(
    double[,] Values,
    int PupilSampling,
    int GridSize,
    double WorkingFNumber,
    double SampleSpacingMicrometers,
    double TangentialWorkingFNumber = 0,
    double SagittalWorkingFNumber = 0,
    double FrequencySampleCount = 0,
    AnalysisAxisUnit SampleSpacingUnit = AnalysisAxisUnit.Micrometer)
{
    public double StrehlRatio => Values[GridSize / 2, GridSize / 2] / 100;

    public double PeakStrehlRatio => Values.Cast<double>().DefaultIfEmpty(0).Max() / 100;
}

public sealed record MtfResult(
    IReadOnlyList<double> Frequency,
    IReadOnlyList<double> Tangential,
    IReadOnlyList<double> Sagittal,
    double CutoffFrequency,
    IReadOnlyList<Complex>? TangentialOtf = null,
    IReadOnlyList<Complex>? SagittalOtf = null,
    IReadOnlyList<double>? TangentialFrequency = null,
    IReadOnlyList<double>? SagittalFrequency = null);

public enum FftMtfDataType
{
    Modulation,
    Real,
    Imaginary,
    Phase,
    SquareWave
}

public static class DiffractionEngine
{
    public static MtfResult LimitFrequency(MtfResult result, double? maximumFrequency)
    {
        if (!maximumFrequency.HasValue
            || !double.IsFinite(maximumFrequency.Value)
            || maximumFrequency.Value <= 0
            || result.Frequency.Count == 0
            || maximumFrequency.Value >= result.Frequency[^1])
        {
            return result;
        }

        var limit = maximumFrequency.Value;
        var frequency = new List<double>();
        var tangential = new List<double>();
        var sagittal = new List<double>();
        var tangentialOtf = result.TangentialOtf is null ? null : new List<Complex>();
        var sagittalOtf = result.SagittalOtf is null ? null : new List<Complex>();
        var index = 0;
        while (index < result.Frequency.Count && result.Frequency[index] <= limit)
        {
            frequency.Add(result.Frequency[index]);
            tangential.Add(result.Tangential[index]);
            sagittal.Add(result.Sagittal[index]);
            tangentialOtf?.Add(result.TangentialOtf![index]);
            sagittalOtf?.Add(result.SagittalOtf![index]);
            index++;
        }

        if (frequency.Count > 0
            && frequency[^1] < limit
            && index < result.Frequency.Count)
        {
            var leftFrequency = result.Frequency[index - 1];
            var rightFrequency = result.Frequency[index];
            var fraction = (limit - leftFrequency) / (rightFrequency - leftFrequency);
            frequency.Add(limit);
            tangential.Add(result.Tangential[index - 1]
                + ((result.Tangential[index] - result.Tangential[index - 1]) * fraction));
            sagittal.Add(result.Sagittal[index - 1]
                + ((result.Sagittal[index] - result.Sagittal[index - 1]) * fraction));
            tangentialOtf?.Add(result.TangentialOtf![index - 1]
                + ((result.TangentialOtf[index] - result.TangentialOtf[index - 1]) * fraction));
            sagittalOtf?.Add(result.SagittalOtf![index - 1]
                + ((result.SagittalOtf[index] - result.SagittalOtf[index - 1]) * fraction));
        }

        return new MtfResult(
            frequency,
            tangential,
            sagittal,
            result.CutoffFrequency,
            tangentialOtf,
            sagittalOtf);
    }

    public static PsfResult ComputeFftPsf(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int pupilSampling,
        int gridSize,
        bool usePolarization = false,
        bool cellCenteredPupil = false,
        double defocusMillimeters = 0,
        WavefrontResult? preparedWavefront = null,
        JonesPupilResult? preparedPolarization = null,
        bool zemaxFftSampling = false,
        bool ignoreOpd = false)
    {
        AnalysisResourceLimits.ValidateFftGrid(pupilSampling, gridSize);

        var pupilGridStretch = zemaxFftSampling
            ? Math.Sqrt(pupilSampling / 32.0)
            : 1;
        var wavefront = preparedWavefront ?? WavefrontEngine.GenerateChiefRayUniform(
            optic,
            field,
            wavelength,
            pupilSampling,
            cellCenteredPupil,
            aimAtStop: cellCenteredPupil,
            pupilGridStretch: pupilGridStretch);
        var polarization = usePolarization
            ? preparedPolarization ?? JonesPupilEngine.Generate(
                    optic,
                    field,
                    wavelength,
                    pupilSampling,
                    useFresnelCoatings: true,
                    cellCentered: cellCenteredPupil)
            : null;
        var fNumber = WorkingFNumber(optic, field, wavelength);
        var (tangentialFNumber, sagittalFNumber) = cellCenteredPupil
            ? WorkingFNumbers(
                optic,
                field,
                wavelength,
                aimAtStop: true,
                zemaxDirectionalAverage: true)
            : (fNumber, fNumber);
        var pupil = BuildComplexPupilCore(
            wavefront,
            polarization,
            pupilSampling,
            wavelength,
            tangentialFNumber,
            sagittalFNumber,
            cellCenteredPupil,
            defocusMillimeters,
            pupilGridStretch,
            ignoreOpd);

        var nonzeroCount = pupil.Cast<Complex>().Count(value => value.Magnitude > 0);
        var normalization = Math.Max(1, nonzeroCount * nonzeroCount);
        var padded = new Complex[gridSize, gridSize];
        var offset = (gridSize - pupilSampling) / 2;
        for (var row = 0; row < pupilSampling; row++)
        {
            for (var column = 0; column < pupilSampling; column++)
            {
                padded[row + offset, column + offset] = pupil[row, column];
            }
        }

        Fft2D(padded);
        var shifted = FftShift(padded);
        var psf = new double[gridSize, gridSize];
        for (var row = 0; row < gridSize; row++)
        {
            for (var column = 0; column < gridSize; column++)
            {
                psf[row, column] = shifted[row, column].Magnitude * shifted[row, column].Magnitude / normalization * 100;
            }
        }

        var sampleSpacing = optic.ImageSpaceAfocal
            ? ImageSpaceAnalysisSupport.FftSampleSpacingMilliradians(
                optic,
                wavelength,
                pupilSampling,
                gridSize,
                zemaxFftSampling,
                pupilGridStretch)
            : zemaxFftSampling
                ? wavelength.Micrometers * fNumber * (pupilSampling - 2)
                    / (gridSize * pupilGridStretch)
                : wavelength.Micrometers * fNumber * (pupilSampling - 1) / gridSize;
        sampleSpacing = Math.Max(1e-12, sampleSpacing);
        return new PsfResult(
            psf,
            pupilSampling,
            gridSize,
            fNumber,
            sampleSpacing,
            cellCenteredPupil ? tangentialFNumber : 0,
            cellCenteredPupil ? sagittalFNumber : 0,
            cellCenteredPupil ? pupilSampling : pupilSampling - 1,
            optic.ImageSpaceAfocal ? AnalysisAxisUnit.Milliradian : AnalysisAxisUnit.Micrometer);
    }

    internal static (Complex Tangential, Complex Sagittal) ComputeFastFftMtfAtFrequency(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int pupilSampling,
        double spatialFrequency,
        double defocusMillimeters,
        bool usePolarization,
        WavefrontResult preparedWavefront,
        JonesPupilResult? preparedPolarization = null)
    {
        var (tangentialFNumber, sagittalFNumber) = WorkingFNumbers(
            optic,
            field,
            wavelength,
            aimAtStop: true,
            zemaxDirectionalAverage: true);
        var frequencyTangentialFNumber = tangentialFNumber;
        var frequencySagittalFNumber = sagittalFNumber;
        var pupil = BuildComplexPupil(
            preparedWavefront,
            usePolarization ? preparedPolarization : null,
            pupilSampling,
            wavelength,
            tangentialFNumber,
            sagittalFNumber,
            cellCenteredPupil: true,
            defocusMillimeters);
        var tangentialCutoff = optic.ImageSpaceAfocal
            ? ImageSpaceAnalysisSupport.AfocalCutoffFrequencyCyclesPerMilliradian(optic, wavelength)
            : frequencyTangentialFNumber <= 1e-30
                ? 0
                : 1 / (wavelength.Micrometers * 1e-3 * frequencyTangentialFNumber);
        var sagittalCutoff = optic.ImageSpaceAfocal
            ? ImageSpaceAnalysisSupport.AfocalCutoffFrequencyCyclesPerMilliradian(optic, wavelength)
            : frequencySagittalFNumber <= 1e-30
                ? 0
                : 1 / (wavelength.Micrometers * 1e-3 * frequencySagittalFNumber);
        var tangentialShift = tangentialCutoff <= 1e-30
            ? 2
            : 2 * spatialFrequency / tangentialCutoff;
        var sagittalShift = sagittalCutoff <= 1e-30
            ? 2
            : 2 * spatialFrequency / sagittalCutoff;
        if (!usePolarization)
        {
            return (
                SparsePupilAutocorrelation(
                    optic,
                    field,
                    wavelength,
                    pupilSampling,
                    0,
                    tangentialShift,
                    defocusMillimeters,
                    tangentialFNumber,
                    sagittalFNumber,
                    preparedWavefront),
                SparsePupilAutocorrelation(
                    optic,
                    field,
                    wavelength,
                    pupilSampling,
                    sagittalShift,
                    0,
                    defocusMillimeters,
                    tangentialFNumber,
                    sagittalFNumber,
                    preparedWavefront));
        }

        return (
            PupilAutocorrelation(pupil, 0, tangentialShift),
            PupilAutocorrelation(pupil, sagittalShift, 0));
    }

    private static Complex SparsePupilAutocorrelation(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int pupilSampling,
        double shiftX,
        double shiftY,
        double defocusMillimeters,
        double tangentialFNumber,
        double sagittalFNumber,
        WavefrontResult normalizationWavefront)
    {
        if (Math.Abs(shiftX) >= 2 || Math.Abs(shiftY) >= 2)
        {
            return Complex.Zero;
        }

        var coordinates = new List<(double X, double Y)>();
        var integrationWeights = new List<double>();
        const int boundarySubdivisions = 8;
        for (var row = 0; row < pupilSampling; row++)
        {
            for (var column = 0; column < pupilSampling; column++)
            {
                var accepted = 0;
                var centroidX = 0.0;
                var centroidY = 0.0;
                for (var subRow = 0; subRow < boundarySubdivisions; subRow++)
                {
                    var subcellY = -1
                        + (2.0 * (row + ((subRow + 0.5) / boundarySubdivisions))
                            / pupilSampling);
                    for (var subColumn = 0; subColumn < boundarySubdivisions; subColumn++)
                    {
                        var subcellX = -1
                            + (2.0 * (column + ((subColumn + 0.5) / boundarySubdivisions))
                                / pupilSampling);
                        var leftX = subcellX - (shiftX / 2);
                        var leftY = subcellY - (shiftY / 2);
                        var rightX = subcellX + (shiftX / 2);
                        var rightY = subcellY + (shiftY / 2);
                        if ((leftX * leftX) + (leftY * leftY) > 1
                            || (rightX * rightX) + (rightY * rightY) > 1)
                        {
                            continue;
                        }

                        accepted++;
                        centroidX += subcellX;
                        centroidY += subcellY;
                    }
                }

                if (accepted == 0)
                {
                    continue;
                }

                var x = centroidX / accepted;
                var y = centroidY / accepted;
                coordinates.Add((x - (shiftX / 2), y - (shiftY / 2)));
                coordinates.Add((x + (shiftX / 2), y + (shiftY / 2)));
                integrationWeights.Add(accepted / (double)(boundarySubdivisions * boundarySubdivisions));
            }
        }

        if (coordinates.Count == 0)
        {
            return Complex.Zero;
        }

        var pairWavefront = GenerateDefocusedWavefront(
            optic,
            field,
            wavelength,
            coordinates,
            defocusMillimeters);
        var normalizationSamples = normalizationWavefront.Samples
            .Where(sample => sample.Intensity > 0)
            .ToArray();
        var meanNormalizationIntensity = normalizationSamples
            .Select(sample => sample.Intensity)
            .DefaultIfEmpty(0)
            .Average();
        var normalization = meanNormalizationIntensity * Math.PI * pupilSampling * pupilSampling / 4;
        if (normalization <= 1e-30)
        {
            return Complex.Zero;
        }

        var sum = Complex.Zero;
        for (var index = 0; index < pairWavefront.Samples.Count; index += 2)
        {
            var left = pairWavefront.Samples[index];
            var right = pairWavefront.Samples[index + 1];
            var leftValue = PupilValue(left, wavelength, 0, pairWavefront);
            var rightValue = PupilValue(right, wavelength, 0, pairWavefront);
            sum += integrationWeights[index / 2]
                * rightValue
                * Complex.Conjugate(leftValue);
        }

        return sum / normalization;
    }

    public static WavefrontResult GenerateDefocusedWavefront(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        IReadOnlyList<(double X, double Y)> coordinates,
        double defocusMillimeters)
    {
        if (optic.ImageSpaceAfocal)
        {
            var wavefront = WavefrontEngine.GenerateChiefRaySamples(
                optic,
                field,
                wavelength,
                coordinates,
                aimAtStop: true);
            return ApplyAfocalDefocus(wavefront, wavelength, defocusMillimeters);
        }

        if (Math.Abs(defocusMillimeters) <= 1e-30)
        {
            return WavefrontEngine.GenerateChiefRaySamples(
                optic,
                field,
                wavelength,
                coordinates,
                aimAtStop: true);
        }

        lock (optic)
        {
            var surfaces = optic.SurfaceGroup.Items;
            if (surfaces.Count < 2)
            {
                return WavefrontEngine.GenerateChiefRaySamples(
                    optic,
                    field,
                    wavelength,
                    coordinates,
                    aimAtStop: true);
            }

            var previous = surfaces[^2];
            var image = surfaces[^1];
            (double X, double Y)? nominalRealImageLaunch = null;
            if (optic.FieldDefinition == FieldDefinitionKind.RealImageHeight)
            {
                var target = FieldCoordinates.Denormalize(
                    optic.Fields,
                    field.Hx,
                    field.Hy);
                nominalRealImageLaunch = optic.SequentialRayTracer.RayGenerator
                    .ResolveRealImageFieldCoordinates(
                        target.X,
                        target.Y,
                        aimAtStop: true);
            }

            var originalThickness = previous.Thickness;
            var originalCoordinate = image.CoordinateSystem;
            var shift = previous.CoordinateSystem.ToGlobalDirection(
                new Vector3D(0, 0, defocusMillimeters));
            previous.Thickness = originalThickness + defocusMillimeters;
            image.CoordinateSystem = new CoordinateSystem(
                originalCoordinate.Origin + shift,
                originalCoordinate.RotationXDegrees,
                originalCoordinate.RotationYDegrees,
                originalCoordinate.RotationZDegrees);
            try
            {
                return WavefrontEngine.GenerateChiefRaySamples(
                    optic,
                    field,
                    wavelength,
                    coordinates,
                    aimAtStop: true,
                    nominalRealImageLaunch);
            }
            finally
            {
                previous.Thickness = originalThickness;
                image.CoordinateSystem = originalCoordinate;
            }
        }
    }

    private static WavefrontResult ApplyAfocalDefocus(
        WavefrontResult wavefront,
        Wavelength wavelength,
        double defocusDiopters)
    {
        if (!wavefront.ImageSpaceAfocal || Math.Abs(defocusDiopters) <= 1e-30)
        {
            return wavefront;
        }

        var samples = wavefront.Samples
            .Select(sample => sample with
            {
                OpdWaves = sample.OpdWaves
                    + ImageSpaceAnalysisSupport.AfocalDefocusOpdWaves(
                        sample,
                        wavelength,
                        defocusDiopters,
                        wavefront.AfocalPupilDiameterMillimeters)
            })
            .ToArray();
        return wavefront with { Samples = samples };
    }

    private static Complex PupilValue(
        WavefrontSample sample,
        Wavelength wavelength,
        double defocusMillimeters,
        WavefrontResult wavefront)
    {
        if (sample.Intensity <= 0)
        {
            return Complex.Zero;
        }

        var defocusOpdWaves = DefocusOpdWaves(sample, wavelength, defocusMillimeters, wavefront);
        return Complex.FromPolarCoordinates(
            FieldAmplitudeFromPower(sample.Intensity),
            -2 * Math.PI * (sample.OpdWaves + defocusOpdWaves));
    }

    private static double DefocusOpdWaves(
        WavefrontSample sample,
        Wavelength wavelength,
        double defocus,
        WavefrontResult wavefront)
    {
        if (Math.Abs(defocus) <= 1e-30)
        {
            return 0;
        }

        return wavefront.ImageSpaceAfocal
            ? ImageSpaceAnalysisSupport.AfocalDefocusOpdWaves(
                sample,
                wavelength,
                defocus,
                wavefront.AfocalPupilDiameterMillimeters)
            : wavefront.ImageRefractiveIndex * defocus
                * (wavefront.ChiefImageDirectionZ - sample.ImageDirectionZ)
                / (wavelength.Micrometers * 1e-3);
    }

    internal static double FieldAmplitudeFromPower(double power) =>
        double.IsFinite(power) && power > 0 ? Math.Sqrt(power) : 0;

    private static Complex[,] BuildComplexPupil(
        WavefrontResult wavefront,
        JonesPupilResult? polarization,
        int pupilSampling,
        Wavelength wavelength,
        double tangentialFNumber,
        double sagittalFNumber,
        bool cellCenteredPupil,
        double defocusMillimeters)
    {
        return BuildComplexPupilCore(
            wavefront,
            polarization,
            pupilSampling,
            wavelength,
            tangentialFNumber,
            sagittalFNumber,
            cellCenteredPupil,
            defocusMillimeters,
            pupilGridStretch: 1,
            ignoreOpd: false);
    }

    public static IReadOnlyList<WavefrontResult> GenerateDefocusedPolychromaticWavefronts(
        Optic optic,
        (double Hx, double Hy) field,
        IReadOnlyList<Wavelength> wavelengths,
        IReadOnlyList<(double X, double Y)> coordinates,
        double defocusMillimeters,
        bool usePolarization = false)
    {
        if (wavelengths.Count == 0)
        {
            return Array.Empty<WavefrontResult>();
        }

        if (optic.ImageSpaceAfocal)
        {
            return wavelengths.Select(wavelength =>
            {
                var wavefront = WavefrontEngine.GenerateChiefRaySamples(
                    optic,
                    field,
                    wavelength,
                    coordinates,
                    aimAtStop: true,
                    usePolarization: usePolarization);
                return ApplyAfocalDefocus(wavefront, wavelength, defocusMillimeters);
            }).ToArray();
        }

        lock (optic)
        {
            var surfaces = optic.SurfaceGroup.Items;
            var previous = surfaces.Count >= 2 ? surfaces[^2] : null;
            var image = surfaces.Count >= 1 ? surfaces[^1] : null;
            var originalThickness = previous?.Thickness ?? 0;
            var originalCoordinate = image?.CoordinateSystem;
            (double X, double Y)? nominalRealImageLaunch = null;
            if (optic.FieldDefinition == FieldDefinitionKind.RealImageHeight)
            {
                var target = FieldCoordinates.Denormalize(optic.Fields, field.Hx, field.Hy);
                nominalRealImageLaunch = optic.SequentialRayTracer.RayGenerator
                    .ResolveRealImageFieldCoordinates(target.X, target.Y, aimAtStop: true);
            }

            if (previous is not null && image is not null && Math.Abs(defocusMillimeters) > 1e-30)
            {
                var shift = previous.CoordinateSystem.ToGlobalDirection(
                    new Vector3D(0, 0, defocusMillimeters));
                previous.Thickness = originalThickness + defocusMillimeters;
                image.CoordinateSystem = new CoordinateSystem(
                    originalCoordinate!.Origin + shift,
                    originalCoordinate.RotationXDegrees,
                    originalCoordinate.RotationYDegrees,
                    originalCoordinate.RotationZDegrees);
            }

            try
            {
                var spheres = wavelengths.Select(wavelength =>
                    WavefrontEngine.CreateChiefRayReferenceSphere(
                        optic,
                        field,
                        wavelength,
                        aimAtStop: true,
                        nominalRealImageLaunch,
                        usePolarization)).ToArray();
                var primaryIndex = Enumerable.Range(0, wavelengths.Count)
                    .FirstOrDefault(index => wavelengths[index].IsPrimary);
                var primary = spheres[primaryIndex];
                return wavelengths.Select((wavelength, index) =>
                    WavefrontEngine.GenerateChiefRaySamples(
                        optic,
                        field,
                        wavelength,
                        coordinates,
                        aimAtStop: true,
                        resolvedRealImageLaunch: nominalRealImageLaunch,
                        referenceSphere: new WavefrontReferenceSphere(
                            primary.CenterX,
                            primary.CenterY,
                            primary.CenterZ,
                            spheres[index].Radius),
                        usePolarization: usePolarization)).ToArray();
            }
            finally
            {
                if (previous is not null && image is not null && originalCoordinate is not null)
                {
                    previous.Thickness = originalThickness;
                    image.CoordinateSystem = originalCoordinate;
                }
            }
        }
    }

    private static Complex[,] BuildComplexPupilCore(
        WavefrontResult wavefront,
        JonesPupilResult? polarization,
        int pupilSampling,
        Wavelength wavelength,
        double tangentialFNumber,
        double sagittalFNumber,
        bool cellCenteredPupil,
        double defocusMillimeters,
        double pupilGridStretch = 1,
        bool ignoreOpd = false)
    {
        var pupil = new Complex[pupilSampling, pupilSampling];
        var valid = wavefront.Samples.Where(sample => sample.Intensity > 0).ToArray();
        var meanIntensity = valid.Select(sample => sample.Intensity).DefaultIfEmpty(0).Average();
        foreach (var sample in wavefront.Samples)
        {
            var column = (int)Math.Round(
                ((sample.NormalizedPupilX / pupilGridStretch) + 1) / 2 * (pupilSampling - 1));
            var row = (int)Math.Round(
                ((sample.NormalizedPupilY / pupilGridStretch) + 1) / 2 * (pupilSampling - 1));
            var relativeIntensity = meanIntensity <= 1e-30 ? 0 : sample.Intensity / meanIntensity;
            if (polarization is not null)
            {
                var jones = polarization.Samples[(row * pupilSampling) + column];
                relativeIntensity *= jones.IsValid
                    ? (jones.Jxx.Magnitude * jones.Jxx.Magnitude
                        + (jones.Jxy.Magnitude * jones.Jxy.Magnitude)
                        + (jones.Jyx.Magnitude * jones.Jyx.Magnitude)
                        + (jones.Jyy.Magnitude * jones.Jyy.Magnitude)) / 2
                    : 0;
            }

            var amplitude = FieldAmplitudeFromPower(relativeIntensity);
            var defocusOpdWaves = DefocusOpdWaves(
                sample,
                wavelength,
                defocusMillimeters,
                wavefront);
            var phase = -2 * Math.PI * ((ignoreOpd ? 0 : sample.OpdWaves) + defocusOpdWaves);
            pupil[row, column] = Complex.FromPolarCoordinates(amplitude, phase);
        }

        return pupil;
    }

    private static Complex PupilAutocorrelation(
        Complex[,] pupil,
        double shiftX,
        double shiftY)
    {
        var size = pupil.GetLength(0);
        var normalization = pupil.Cast<Complex>()
            .Sum(value => value.Magnitude * value.Magnitude);
        if (normalization <= 1e-30 || Math.Abs(shiftX) >= 2 || Math.Abs(shiftY) >= 2)
        {
            return Complex.Zero;
        }

        var sum = Complex.Zero;
        for (var row = 0; row < size; row++)
        {
            var y = -1 + ((2.0 * row + 1) / size);
            for (var column = 0; column < size; column++)
            {
                var x = -1 + ((2.0 * column + 1) / size);
                var left = BilinearPupilSample(
                    pupil,
                    x - (shiftX / 2),
                    y - (shiftY / 2));
                var right = BilinearPupilSample(
                    pupil,
                    x + (shiftX / 2),
                    y + (shiftY / 2));
                sum += right * Complex.Conjugate(left);
            }
        }

        return sum / normalization;
    }

    private static Complex BilinearPupilSample(Complex[,] pupil, double x, double y)
    {
        var size = pupil.GetLength(0);
        var column = (((x + 1) * size) - 1) / 2;
        var row = (((y + 1) * size) - 1) / 2;
        if (column < 0 || row < 0 || column > size - 1 || row > size - 1)
        {
            return Complex.Zero;
        }

        var left = (int)Math.Floor(column);
        var top = (int)Math.Floor(row);
        var right = Math.Min(size - 1, left + 1);
        var bottom = Math.Min(size - 1, top + 1);
        var fx = column - left;
        var fy = row - top;
        var upper = (pupil[top, left] * (1 - fx)) + (pupil[top, right] * fx);
        var lower = (pupil[bottom, left] * (1 - fx)) + (pupil[bottom, right] * fx);
        return (upper * (1 - fy)) + (lower * fy);
    }

    public static MtfResult ComputeFftMtf(
        PsfResult psf,
        Optic optic,
        Wavelength wavelength)
    {
        var complex = new Complex[psf.GridSize, psf.GridSize];
        for (var row = 0; row < psf.GridSize; row++)
        {
            for (var column = 0; column < psf.GridSize; column++)
            {
                complex[row, column] = new Complex(psf.Values[row, column], 0);
            }
        }

        Fft2D(complex);
        var shifted = FftShift(complex);
        var center = psf.GridSize / 2;
        var dc = shifted[center, center];
        var normalization = dc.Magnitude <= 1e-30 ? Complex.One : dc;
        var tangentialOtf = Enumerable.Range(center, psf.GridSize - center)
            .Select(row => shifted[row, center] / normalization)
            .ToArray();
        var sagittalOtf = Enumerable.Range(center, psf.GridSize - center)
            .Select(column => shifted[center, column] / normalization)
            .ToArray();
        var tangential = tangentialOtf.Select(value => value.Magnitude).ToArray();
        var sagittal = sagittalOtf.Select(value => value.Magnitude).ToArray();
        var sampleCount = psf.FrequencySampleCount > 0
            ? psf.FrequencySampleCount
            : psf.PupilSampling - 1;
        var legacyFNumber = WorkingFNumber(optic, (0, 0), wavelength);
        var tangentialFNumber = psf.TangentialWorkingFNumber > 0
            ? psf.TangentialWorkingFNumber
            : legacyFNumber;
        var sagittalFNumber = psf.SagittalWorkingFNumber > 0
            ? psf.SagittalWorkingFNumber
            : legacyFNumber;
        var afocalCutoff = optic.ImageSpaceAfocal
            ? ImageSpaceAnalysisSupport.AfocalCutoffFrequencyCyclesPerMilliradian(optic, wavelength)
            : 0;
        var tangentialStep = optic.ImageSpaceAfocal
            ? afocalCutoff / sampleCount
            : 1 / (sampleCount * wavelength.Micrometers * 1e-3 * tangentialFNumber);
        var sagittalStep = optic.ImageSpaceAfocal
            ? afocalCutoff / sampleCount
            : 1 / (sampleCount * wavelength.Micrometers * 1e-3 * sagittalFNumber);
        var tangentialFrequency = Enumerable.Range(0, tangential.Length)
            .Select(index => index * tangentialStep)
            .ToArray();
        var sagittalFrequency = Enumerable.Range(0, sagittal.Length)
            .Select(index => index * sagittalStep)
            .ToArray();
        var cutoff = optic.ImageSpaceAfocal
            ? afocalCutoff
            : Math.Min(
                1 / (wavelength.Micrometers * 1e-3 * tangentialFNumber),
                1 / (wavelength.Micrometers * 1e-3 * sagittalFNumber));
        return new MtfResult(
            tangentialFrequency,
            tangential,
            sagittal,
            cutoff,
            tangentialOtf,
            sagittalOtf,
            tangentialFrequency,
            sagittalFrequency);
    }

    public static PsfResult ComputeMmdftPsf(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int numRays,
        int imageSize,
        double? pixelPitchMicrometers = null)
    {
        if (numRays < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(numRays), "MMDFT requires at least two pupil samples.");
        }

        if (imageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(imageSize), "Image size must be positive.");
        }

        AnalysisResourceLimits.ValidateDirectPsfWork(numRays, imageSize);

        var wavefront = WavefrontEngine.GenerateChiefRayUniform(optic, field, wavelength, numRays);
        var pupil = CreateUniformPupil(wavefront, numRays);
        var fNumber = WorkingFNumber(optic, field, wavelength);
        var clearSize = numRays - 1;
        var afocalImageSpace = optic.ImageSpaceAfocal;
        var pupilDiameter = ImageSpaceAnalysisSupport.AfocalDiffractionPupilDiameterMillimeters(optic);
        var defaultSampleSpacing = afocalImageSpace && pupilDiameter > 1e-30
            ? wavelength.Micrometers * clearSize / (pupilDiameter * imageSize)
            : wavelength.Micrometers * fNumber * clearSize / imageSize;
        var sampleSpacing = pixelPitchMicrometers ?? defaultSampleSpacing;
        var padSize = afocalImageSpace && pupilDiameter > 1e-30
            ? wavelength.Micrometers * clearSize / (pupilDiameter * sampleSpacing)
            : wavelength.Micrometers * fNumber * clearSize / sampleSpacing;
        if (imageSize > padSize)
        {
            throw new ArgumentException("Image size exceeds the MMDFT pad size for the requested pixel pitch.");
        }

        var imagePlane = new Complex[imageSize, imageSize];
        for (var imageRow = 0; imageRow < imageSize; imageRow++)
        {
            var imageY = imageRow - (imageSize / 2);
            for (var imageColumn = 0; imageColumn < imageSize; imageColumn++)
            {
                var imageX = imageColumn - (imageSize / 2);
                var sum = Complex.Zero;
                for (var pupilRow = 0; pupilRow < numRays; pupilRow++)
                {
                    var pupilY = pupilRow - (numRays / 2);
                    var left = Complex.FromPolarCoordinates(1, -2 * Math.PI * imageY * pupilY / padSize);
                    for (var pupilColumn = 0; pupilColumn < numRays; pupilColumn++)
                    {
                        var pupilX = pupilColumn - (numRays / 2);
                        var right = Complex.FromPolarCoordinates(1, -2 * Math.PI * pupilX * imageX / padSize);
                        sum += left * pupil[pupilRow, pupilColumn] * right;
                    }
                }

                imagePlane[imageRow, imageColumn] = sum;
            }
        }

        var normalization = Math.Max(1, pupil.Cast<Complex>().Count(value => value.Magnitude > 0));
        normalization *= normalization;
        var psf = new double[imageSize, imageSize];
        for (var row = 0; row < imageSize; row++)
        {
            for (var column = 0; column < imageSize; column++)
            {
                psf[row, column] = imagePlane[row, column].Magnitude * imagePlane[row, column].Magnitude * 100 / normalization;
            }
        }

        return new PsfResult(
            psf,
            numRays,
            imageSize,
            fNumber,
            sampleSpacing,
            SampleSpacingUnit: afocalImageSpace ? AnalysisAxisUnit.Milliradian : AnalysisAxisUnit.Micrometer);
    }

    public static PsfResult ComputeHuygensPsf(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int numRays,
        int imageSize,
        double pixelPitchMillimeters,
        bool usePolarization = false,
        bool aimAtStop = false,
        double defocus = 0)
    {
        if (numRays < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(numRays), "Huygens PSF requires at least two pupil samples.");
        }

        if (imageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(imageSize), "Image size must be positive.");
        }


        AnalysisResourceLimits.ValidateDirectPsfWork(numRays, imageSize);

        if (!double.IsFinite(pixelPitchMillimeters) || pixelPitchMillimeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelPitchMillimeters), "Pixel pitch must be positive.");
        }

        var wavefront = WavefrontEngine.GenerateChiefRayUniform(
            optic,
            field,
            wavelength,
            numRays,
            aimAtStop: aimAtStop);
        var polarization = usePolarization
            ? JonesPupilEngine.Generate(optic, field, wavelength, numRays, useFresnelCoatings: true)
            : null;
        if (optic.ImageSpaceAfocal)
        {
            var afocalRaw = HuygensFarFieldSummation(
                wavefront,
                wavelength,
                imageSize,
                pixelPitchMillimeters,
                idealOpd: false,
                polarization,
                defocus);
            var afocalNormalizationWavefront = field.Hx == 0 && field.Hy == 0
                ? wavefront
                : WavefrontEngine.GenerateChiefRayUniform(
                    optic,
                    (0, 0),
                    wavelength,
                    numRays,
                    aimAtStop: aimAtStop);
            var afocalNormalizationPolarization = !usePolarization
                ? null
                : field.Hx == 0 && field.Hy == 0
                    ? polarization
                    : JonesPupilEngine.Generate(
                        optic,
                        (0, 0),
                        wavelength,
                        numRays,
                        useFresnelCoatings: true);
            var afocalNormalization = HuygensFarFieldSummation(
                afocalNormalizationWavefront,
                wavelength,
                1,
                pixelPitchMillimeters,
                idealOpd: true,
                afocalNormalizationPolarization,
                defocus: 0)[0, 0];
            afocalNormalization = Math.Max(1e-300, afocalNormalization);

            var afocalPsf = new double[imageSize, imageSize];
            for (var row = 0; row < imageSize; row++)
            {
                for (var column = 0; column < imageSize; column++)
                {
                    afocalPsf[row, column] = afocalRaw[row, column] / afocalNormalization * 100;
                }
            }

            return new PsfResult(
                afocalPsf,
                numRays,
                imageSize,
                WorkingFNumber(optic, field, wavelength, aimAtStop),
                pixelPitchMillimeters,
                SampleSpacingUnit: AnalysisAxisUnit.Milliradian);
        }

        var imageCoordinates = CreateHuygensImageCoordinates(optic, field, wavelength, imageSize, pixelPitchMillimeters);
        var raw = HuygensSummation(
            imageCoordinates,
            wavefront,
            wavelength,
            idealOpd: false,
            polarization);
        var normalizationWavefront = field.Hx == 0 && field.Hy == 0
            ? wavefront
            : WavefrontEngine.GenerateChiefRayUniform(
                optic,
                (0, 0),
                wavelength,
                numRays,
                aimAtStop: aimAtStop);
        var normalizationPolarization = !usePolarization
            ? null
            : field.Hx == 0 && field.Hy == 0
                ? polarization
                : JonesPupilEngine.Generate(
                    optic,
                    (0, 0),
                    wavelength,
                    numRays,
                    useFresnelCoatings: true);
        var normalizationPoint = CreateHuygensImageCoordinates(optic, (0, 0), wavelength, 1, pixelPitchMillimeters);
        var normalization = HuygensSummation(
            normalizationPoint,
            normalizationWavefront,
            wavelength,
            idealOpd: true,
            normalizationPolarization)[0, 0];
        normalization = Math.Max(1e-300, normalization);

        var psf = new double[imageSize, imageSize];
        for (var row = 0; row < imageSize; row++)
        {
            for (var column = 0; column < imageSize; column++)
            {
                psf[row, column] = raw[row, column] / normalization * 100;
            }
        }

        return new PsfResult(
            psf,
            numRays,
            imageSize,
            WorkingFNumber(optic, field, wavelength, aimAtStop),
            pixelPitchMillimeters * 1000.0);
    }

    public static double DefaultHuygensImageDeltaMillimeters(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int pupilSampling)
    {
        if (optic.ImageSpaceAfocal)
        {
            var airy = ImageSpaceAnalysisSupport.AfocalAiryRadiusMilliradians(optic, wavelength);
            return Math.Max(1e-12, airy / Math.Sqrt(Math.Max(2, pupilSampling)));
        }

        var workingFNumber = WorkingFNumber(
            optic,
            field,
            wavelength,
            aimAtStop: optic.RayAimingEnabled);
        var deltaMicrometers = wavelength.Micrometers
            * workingFNumber
            / Math.Sqrt(Math.Max(2, pupilSampling));
        return Math.Max(1e-12, deltaMicrometers / 1000.0);
    }

    public static MtfResult ComputePsfMtf(PsfResult psf)
    {
        var complex = new Complex[psf.GridSize, psf.GridSize];
        for (var row = 0; row < psf.GridSize; row++)
        {
            for (var column = 0; column < psf.GridSize; column++)
            {
                complex[row, column] = new Complex(psf.Values[row, column], 0);
            }
        }

        Transform2D(complex);
        var shifted = FftShift(complex);
        var center = psf.GridSize / 2;
        var count = psf.GridSize / 2;
        var dc = Math.Max(1e-300, shifted[center, center].Magnitude);
        var tangential = Enumerable.Range(0, count)
            .Select(index => Math.Clamp(shifted[center + index, center].Magnitude / dc, 0, 1))
            .ToArray();
        var sagittal = Enumerable.Range(0, count)
            .Select(index => Math.Clamp(shifted[center, center + index].Magnitude / dc, 0, 1))
            .ToArray();
        var frequencyStep = psf.SampleSpacingUnit == AnalysisAxisUnit.Milliradian
            ? 1.0 / (psf.GridSize * psf.SampleSpacingMicrometers)
            : 1000.0 / (psf.GridSize * psf.SampleSpacingMicrometers);
        var frequency = Enumerable.Range(0, count).Select(index => index * frequencyStep).ToArray();
        var cutoff = count == 0 ? 0 : frequency[^1];
        return new MtfResult(frequency, tangential, sagittal, cutoff);
    }

    public static MtfResult ComputePsfMtfAtFrequencies(
        PsfResult psf,
        IReadOnlyList<double> spatialFrequencies)
    {
        ArgumentNullException.ThrowIfNull(spatialFrequencies);
        if (spatialFrequencies.Count == 0)
        {
            return new MtfResult(Array.Empty<double>(), Array.Empty<double>(), Array.Empty<double>(), 0);
        }

        var frequencies = spatialFrequencies
            .Select(frequency => Math.Max(0, frequency))
            .ToArray();
        var tangential = new double[frequencies.Length];
        var sagittal = new double[frequencies.Length];
        var dc = psf.Values.Cast<double>().Sum();
        if (!double.IsFinite(dc) || dc <= 0)
        {
            return new MtfResult(frequencies, tangential, sagittal, frequencies.Max());
        }

        var center = (psf.GridSize - 1) / 2.0;
        var spacing = psf.SampleSpacingUnit == AnalysisAxisUnit.Milliradian
            ? psf.SampleSpacingMicrometers
            : psf.SampleSpacingMicrometers / 1000.0;
        for (var frequencyIndex = 0; frequencyIndex < frequencies.Length; frequencyIndex++)
        {
            var angularFrequency = 2 * Math.PI * frequencies[frequencyIndex];
            var tangentialOtf = Complex.Zero;
            var sagittalOtf = Complex.Zero;
            for (var row = 0; row < psf.GridSize; row++)
            {
                var y = (row - center) * spacing;
                var tangentialPhase = Complex.FromPolarCoordinates(1, -angularFrequency * y);
                for (var column = 0; column < psf.GridSize; column++)
                {
                    var intensity = psf.Values[row, column];
                    tangentialOtf += intensity * tangentialPhase;
                    var x = (column - center) * spacing;
                    sagittalOtf += intensity * Complex.FromPolarCoordinates(1, -angularFrequency * x);
                }
            }

            tangential[frequencyIndex] = Math.Clamp(tangentialOtf.Magnitude / dc, 0, 1);
            sagittal[frequencyIndex] = Math.Clamp(sagittalOtf.Magnitude / dc, 0, 1);
        }

        return new MtfResult(frequencies, tangential, sagittal, frequencies.Max());
    }

    public static double WorkingFNumber(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        bool aimAtStop = false)
    {
        var axes = WorkingFNumbers(optic, field, wavelength, aimAtStop);
        var inverseSquared = (1 / (axes.Tangential * axes.Tangential)
            + (1 / (axes.Sagittal * axes.Sagittal))) / 2;
        return inverseSquared <= 0 ? 10000 : Math.Min(10000, 1 / Math.Sqrt(inverseSquared));
    }

    public static (double Tangential, double Sagittal) WorkingFNumbers(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        bool aimAtStop = false,
        bool zemaxDirectionalAverage = false)
    {
        var pupil = new[] { (0.0, 0.0), (0.0, 1.0), (0.0, -1.0), (1.0, 0.0), (-1.0, 0.0) };
        var directions = pupil.Select(item =>
        {
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
                field.Hx,
                field.Hy,
                item.Item1,
                item.Item2,
                wavelength.Micrometers,
                aimAtStop);
            var sample = optic.SequentialRayTracer.TraceFinalSamples(bundle).Single()
                ?? throw new InvalidOperationException("Working-F-number ray did not reach the image surface.");
            return sample.Direction;
        }).ToArray();
        var chief = directions[0];
        var imageIndex = optic.SurfaceGroup.Items[^1].MaterialAfter.RefractiveIndex(wavelength.Nanometers);
        double DirectionalFNumber(IEnumerable<Vector3D> marginalDirections)
        {
            var numericalApertures = marginalDirections.Select(direction =>
            {
                var dot = Math.Clamp(
                    (chief.X * direction.X) + (chief.Y * direction.Y) + (chief.Z * direction.Z),
                    -1,
                    1);
                var angle = Math.Acos(dot);
                return imageIndex * Math.Sin(angle);
            }).ToArray();
            var equivalentNumericalAperture = zemaxDirectionalAverage
                ? numericalApertures.Average()
                : Math.Sqrt(numericalApertures.Average(value => value * value));
            return equivalentNumericalAperture <= 0
                ? 10000
                : Math.Min(10000, 1 / (2 * equivalentNumericalAperture));
        }

        return (
            DirectionalFNumber(directions.Skip(1).Take(2)),
            DirectionalFNumber(directions.Skip(3).Take(2)));
    }

    private static void Fft2D(Complex[,] data)
    {
        var size = data.GetLength(0);
        var buffer = new Complex[size];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                buffer[column] = data[row, column];
            }

            Fft(buffer);
            for (var column = 0; column < size; column++)
            {
                data[row, column] = buffer[column];
            }
        }

        for (var column = 0; column < size; column++)
        {
            for (var row = 0; row < size; row++)
            {
                buffer[row] = data[row, column];
            }

            Fft(buffer);
            for (var row = 0; row < size; row++)
            {
                data[row, column] = buffer[row];
            }
        }
    }

    private static void Transform2D(Complex[,] data)
    {
        var size = data.GetLength(0);
        if (IsPowerOfTwo(size))
        {
            Fft2D(data);
            return;
        }

        var transformed = new Complex[size, size];
        for (var u = 0; u < size; u++)
        {
            for (var v = 0; v < size; v++)
            {
                var sum = Complex.Zero;
                for (var row = 0; row < size; row++)
                {
                    for (var column = 0; column < size; column++)
                    {
                        var angle = -2 * Math.PI * ((u * row / (double)size) + (v * column / (double)size));
                        sum += data[row, column] * Complex.FromPolarCoordinates(1, angle);
                    }
                }

                transformed[u, v] = sum;
            }
        }

        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                data[row, column] = transformed[row, column];
            }
        }
    }

    private static Complex[,] CreateUniformPupil(WavefrontResult wavefront, int numRays)
    {
        var pupil = new Complex[numRays, numRays];
        var valid = wavefront.Samples.Where(sample => sample.Intensity > 0).ToArray();
        var meanIntensity = valid.Select(sample => sample.Intensity).DefaultIfEmpty(0).Average();
        foreach (var sample in wavefront.Samples)
        {
            var column = (int)Math.Round((sample.NormalizedPupilX + 1) / 2 * (numRays - 1));
            var row = (int)Math.Round((sample.NormalizedPupilY + 1) / 2 * (numRays - 1));
            var relativePower = meanIntensity <= 1e-30 ? 0 : sample.Intensity / meanIntensity;
            var amplitude = FieldAmplitudeFromPower(relativePower);
            var phase = -2 * Math.PI * sample.OpdWaves;
            pupil[row, column] = Complex.FromPolarCoordinates(amplitude, phase);
        }

        return pupil;
    }

    private static Vector3D[,] CreateHuygensImageCoordinates(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int imageSize,
        double pixelPitchMillimeters)
    {
        var imageSurface = optic.SurfaceGroup.Items[^1];
        var chiefBundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
            field.Hx,
            field.Hy,
            0,
            0,
            wavelength.Micrometers);
        var chief = optic.SequentialRayTracer.TraceFinalSamples(chiefBundle).SingleOrDefault()
            ?? throw new InvalidOperationException("Chief ray did not reach the image surface.");
        var center = chief.Position;
        var localCenter = imageSurface.CoordinateSystem.ToLocalPoint(center);
        var localNormal = imageSurface.Geometry.SurfaceNormal(localCenter);
        var normal = Normalize(imageSurface.CoordinateSystem.ToGlobalDirection(localNormal));
        var imageLocalX = imageSurface.CoordinateSystem.ToGlobalDirection(new Vector3D(1, 0, 0));
        var tangentX = imageLocalX - (normal * Dot(imageLocalX, normal));
        if (tangentX.Length <= 1e-12)
        {
            tangentX = new Vector3D(1, 0, 0) - (normal * normal.X);
        }

        tangentX = Normalize(tangentX);
        var tangentY = Normalize(Cross(normal, tangentX));
        var coordinates = new Vector3D[imageSize, imageSize];
        var centerIndex = imageSize / 2;
        for (var row = 0; row < imageSize; row++)
        {
            var y = (row - centerIndex) * pixelPitchMillimeters;
            for (var column = 0; column < imageSize; column++)
            {
                var x = (column - centerIndex) * pixelPitchMillimeters;
                coordinates[row, column] = center + (tangentX * x) + (tangentY * y);
            }
        }

        return coordinates;
    }

    private static (double X, double Y) HuygensImageCenter(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength)
    {
        var imageSurface = optic.SurfaceGroup.Items[^1];
        var pupilSamples = SpotAnalysisEngine.CreatePupilSamples(6, "hexapolar");
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            pupilSamples);
        var local = optic.SequentialRayTracer.TraceFinalSamples(bundle)
            .Where(sample => sample is not null)
            .Select(sample => sample!)
            .Where(sample => sample.Intensity > 0)
            .Select(sample => imageSurface.CoordinateSystem.ToLocalPoint(sample.Position))
            .ToArray();
        return local.Length == 0
            ? (0, 0)
            : (local.Average(point => point.X), local.Average(point => point.Y));
    }

    private static double[,] HuygensFarFieldSummation(
        WavefrontResult wavefront,
        Wavelength wavelength,
        int imageSize,
        double angularPixelPitchMilliradians,
        bool idealOpd,
        JonesPupilResult? polarization = null,
        double defocus = 0)
    {
        var psf = new double[imageSize, imageSize];
        var pupilDiameter = wavefront.AfocalPupilDiameterMillimeters;
        if (pupilDiameter <= 1e-30)
        {
            return psf;
        }

        var pupilRadius = pupilDiameter / 2.0;
        var wavelengthMillimeters = wavelength.Micrometers * 1e-3;
        var k = 2 * Math.PI / wavelengthMillimeters;
        var centerIndex = imageSize / 2;
        var polarizationByPupil = polarization?.Samples.ToDictionary(
            sample => (
                X: (long)Math.Round(sample.Px * 1_000_000_000),
                Y: (long)Math.Round(sample.Py * 1_000_000_000)),
            PolarizationAmplitude);
        for (var row = 0; row < imageSize; row++)
        {
            var thetaY = (row - centerIndex) * angularPixelPitchMilliradians / 1_000.0;
            for (var column = 0; column < imageSize; column++)
            {
                var thetaX = (column - centerIndex) * angularPixelPitchMilliradians / 1_000.0;
                var field = Complex.Zero;
                foreach (var sample in wavefront.Samples)
                {
                    if (sample.Intensity <= 0)
                    {
                        continue;
                    }

                    var pupilX = sample.NormalizedPupilX * pupilRadius;
                    var pupilY = sample.NormalizedPupilY * pupilRadius;
                    var opdWaves = idealOpd
                        ? 0
                        : sample.OpdWaves
                            + ImageSpaceAnalysisSupport.AfocalDefocusOpdWaves(
                                sample,
                                wavelength,
                                defocus,
                                pupilDiameter);
                    var pupilPhase = -k * opdWaves * wavelengthMillimeters;
                    var anglePhase = -k * ((pupilX * thetaX) + (pupilY * thetaY));
                    var polarizationAmplitude = polarizationByPupil?.GetValueOrDefault((
                        (long)Math.Round(sample.NormalizedPupilX * 1_000_000_000),
                        (long)Math.Round(sample.NormalizedPupilY * 1_000_000_000))) ?? 1;
                    var sampleAmplitude = FieldAmplitudeFromPower(sample.Intensity);
                    field += sampleAmplitude
                        * polarizationAmplitude
                        * Complex.FromPolarCoordinates(1, pupilPhase + anglePhase);
                }

                psf[row, column] = field.Magnitude * field.Magnitude;
            }
        }

        return psf;

        static double PolarizationAmplitude(JonesPupilSample sample)
        {
            if (!sample.IsValid)
            {
                return 0;
            }

            var power = (
                (sample.Jxx.Magnitude * sample.Jxx.Magnitude)
                + (sample.Jxy.Magnitude * sample.Jxy.Magnitude)
                + (sample.Jyx.Magnitude * sample.Jyx.Magnitude)
                + (sample.Jyy.Magnitude * sample.Jyy.Magnitude)) / 2;
            return Math.Sqrt(Math.Max(0, power));
        }
    }

    private static double[,] HuygensSummation(
        Vector3D[,] imageCoordinates,
        WavefrontResult wavefront,
        Wavelength wavelength,
        bool idealOpd,
        JonesPupilResult? polarization = null)
    {
        var rows = imageCoordinates.GetLength(0);
        var columns = imageCoordinates.GetLength(1);
        var psf = new double[rows, columns];
        var wavelengthMillimeters = wavelength.Micrometers * 1e-3;
        var k = 2 * Math.PI / wavelengthMillimeters;
        var radius = wavefront.Radius;
        var polarizationByPupil = polarization?.Samples.ToDictionary(
            sample => (
                X: (long)Math.Round(sample.Px * 1_000_000_000),
                Y: (long)Math.Round(sample.Py * 1_000_000_000)),
            PolarizationAmplitude);
        foreach (var row in Enumerable.Range(0, rows))
        {
            for (var column = 0; column < columns; column++)
            {
                var image = imageCoordinates[row, column];
                var field = Complex.Zero;
                foreach (var sample in wavefront.Samples)
                {
                    var pupil = new Vector3D(sample.PupilX, sample.PupilY, sample.PupilZ);
                    var delta = image - pupil;
                    var distance = delta.Length;
                    if (distance <= 1e-300)
                    {
                        continue;
                    }

                    var wave = Complex.FromPolarCoordinates(1 / distance, k * distance);
                    var obliquity = 0.5 * (1 + (Dot(delta, pupil / radius) / distance));
                    var opd = idealOpd ? 0 : sample.OpdWaves * wavelengthMillimeters;
                    var pupilPhase = Complex.FromPolarCoordinates(1, -k * opd);
                    var polarizationAmplitude = polarizationByPupil?.GetValueOrDefault((
                        (long)Math.Round(sample.NormalizedPupilX * 1_000_000_000),
                        (long)Math.Round(sample.NormalizedPupilY * 1_000_000_000))) ?? 1;
                    var sampleAmplitude = FieldAmplitudeFromPower(sample.Intensity);
                    field += sampleAmplitude * polarizationAmplitude * pupilPhase * wave * obliquity;
                }

                psf[row, column] = field.Magnitude * field.Magnitude;
            }
        }

        return psf;

        static double PolarizationAmplitude(JonesPupilSample sample)
        {
            if (!sample.IsValid)
            {
                return 0;
            }

            var power = (
                (sample.Jxx.Magnitude * sample.Jxx.Magnitude)
                + (sample.Jxy.Magnitude * sample.Jxy.Magnitude)
                + (sample.Jyx.Magnitude * sample.Jyx.Magnitude)
                + (sample.Jyy.Magnitude * sample.Jyy.Magnitude)) / 2;
            return Math.Sqrt(Math.Max(0, power));
        }
    }


    private static double Dot(Vector3D left, Vector3D right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private static Vector3D Cross(Vector3D left, Vector3D right)
    {
        return new Vector3D(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    private static Vector3D Normalize(Vector3D value)
    {
        var length = value.Length;
        return length <= 1e-30 ? new Vector3D(0, 0, 1) : value / length;
    }

    private static void Fft(Complex[] values)
    {
        var count = values.Length;
        for (int index = 1, reversed = 0; index < count; index++)
        {
            var bit = count >> 1;
            for (; (reversed & bit) != 0; bit >>= 1)
            {
                reversed ^= bit;
            }

            reversed ^= bit;
            if (index < reversed)
            {
                (values[index], values[reversed]) = (values[reversed], values[index]);
            }
        }

        for (var length = 2; length <= count; length <<= 1)
        {
            var root = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
            for (var start = 0; start < count; start += length)
            {
                var factor = Complex.One;
                for (var offset = 0; offset < length / 2; offset++)
                {
                    var even = values[start + offset];
                    var odd = values[start + offset + (length / 2)] * factor;
                    values[start + offset] = even + odd;
                    values[start + offset + (length / 2)] = even - odd;
                    factor *= root;
                }
            }
        }
    }

    private static Complex[,] FftShift(Complex[,] source)
    {
        var size = source.GetLength(0);
        var shifted = new Complex[size, size];
        var half = (size + 1) / 2;
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                shifted[row, column] = source[(row + half) % size, (column + half) % size];
            }
        }

        return shifted;
    }

    private static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }
}
