using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    internal static (Wavelength Wavelength, PsfResult Result)[] ComputeFftCumulativeEnergySpectrum(
        Optic optic, (double Hx, double Hy) field, IReadOnlyList<Wavelength> wavelengths,
        int pupilSampling, bool ignoreOpd)
    {
        var reference = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0];
        var longest = wavelengths.MaxBy(w => w.Nanometers)!;
        if (optic.ImageSpaceAfocal)
        {
            var diameter = ImageSpaceAnalysisSupport.AfocalDiffractionPupilDiameterMillimeters(optic);
            if (!(diameter > 0)) throw new AnalysisDataUnavailableException("Diffraction Encircled Energy", "no finite exit pupil");
            var angularPitch = 2 * longest.Micrometers / (diameter * Math.Sqrt(pupilSampling));
            return wavelengths.Select(wave =>
            {
                // Form the angular Fourier interval directly. Canceling the
                // pupil diameter through a rounded default-pitch ratio can
                // move the four diagonal nodes outside the closed unit pupil.
                var step = (wave.Micrometers / longest.Micrometers) / (2 * Math.Sqrt(pupilSampling));
                var aspect = ImageSpaceAnalysisSupport.AfocalFftPupilAspectStretch(
                    optic, field, wave, optic.RayAimingEnabled);
                var grid = new PupilGridSpecification(pupilSampling, true, true,
                    step * aspect.X * (pupilSampling / 2d - 1))
                {
                    ExplicitCoordinateStep = step * aspect.X,
                    ExplicitCoordinateStepY = step * aspect.Y
                };
                // Share the scalar FFT amplitude rule with afocal PSF displays;
                // cumulative integration must receive the same apodized pupil.
                var result = ComputeFftPsfCore(optic, field, wave, pupilSampling, 2 * pupilSampling,
                    false, true, 0, null, null, true, ignoreOpd, optic.RayAimingEnabled,
                    reference, angularPitch, true, out _, explicitPupilGrid: grid,
                    interpolateScalarPupilAmplitude: true);
                return (wave, result with { SampleSpacingMicrometers = angularPitch });
            }).ToArray();
        }
        var scaleWave = wavelengths.Count == 1 ? reference : longest;
        if (pupilSampling == 32 && wavelengths.Count == 1
            && TryComputeCircularEnergy32(optic, field, wavelengths[0], reference, ignoreOpd, out var smallGrid))
            return smallGrid;
        if (wavelengths.Count > 1 && pupilSampling <= 64)
        {
            // A polychromatic grid uses the system reference cone at every
            // wavelength. Separate monochromatic cones change rim samples even
            // when their PSFs share the same nominal image pitch.
            var referenceF = WorkingFNumber(optic, field, reference, optic.RayAimingEnabled);
            var commonPitch = longest.Micrometers * referenceF / Math.Sqrt(pupilSampling);
            var automaticStretch = Math.Sqrt(pupilSampling / 32d);
            var referenceSphere = CumulativeEnergyReferenceSphere(optic, field, reference);
            return wavelengths.Select(wave =>
            {
                // Retain the default-FFT extent conversion before forming its
                // coordinate interval. Reconstructing coordinates from extent
                // independently can change the four circular boundary nodes.
                var defaultPitch = wave.Micrometers * referenceF * (pupilSampling - 2)
                    / (2 * pupilSampling * automaticStretch);
                var extent = automaticStretch * defaultPitch / commonPitch;
                var step = extent / (pupilSampling / 2d - 1);
                var grid = new PupilGridSpecification(pupilSampling, true, true, extent)
                {
                    ExplicitCoordinateStep = step,
                    UseCachedFftStopMap = true
                };
                var result = ComputeFftPsfCore(optic, field, wave, pupilSampling,
                    2 * pupilSampling, false, true, 0, null, null, true, ignoreOpd,
                    optic.RayAimingEnabled, reference, commonPitch, true, out _, referenceSphere, grid);
                // Its grid was defined by the common reference cone. The
                // monochromatic F-number must not redefine the returned pitch.
                return (wave, result with { SampleSpacingMicrometers = commonPitch });
            }).ToArray();
        }
        if (wavelengths.Count > 1 && pupilSampling >= 128
            && CumulativeEnergyCone(optic, field, reference) is { } referenceCone)
        {
            // Expanded polychromatic grids use the reference cone's RMS cutoff
            // for their common image plane and its directional means for every
            // wavelength's pupil. Per-wave cones belong to selected-wave mode.
            var cutoff = 2 * referenceCone.Rms / longest.Micrometers;
            var commonPitch = pupilSampling >= 256
                ? ExpandedCumulativeEnergyPitch(longest.Micrometers, referenceCone.Rms, pupilSampling)
                : Math.Sqrt(1d / pupilSampling) / cutoff;
            var referenceSphere = CumulativeEnergyReferenceSphere(optic, field, reference);
            return wavelengths.Select(wave =>
            {
                // The 128 grid retains its direct common-plane interval.
                // Denser grids retain the physical/default-FFT conversion below.
                // Keep the resolved intervals through tracing and indexing.
                var stepX = CoordinateStep(referenceCone.SagittalMean);
                var stepY = CoordinateStep(referenceCone.TangentialMean);
                var grid = new PupilGridSpecification(pupilSampling, true, true, stepX * (pupilSampling / 2d - 1))
                {
                    ExplicitCoordinateStep = stepX,
                    ExplicitCoordinateStepY = stepY,
                    UseCachedFftStopMap = true
                };
                if (pupilSampling >= 256)
                    grid = ExpandedCumulativeEnergyGrid(optic, field, wave, pupilSampling, commonPitch,
                        referenceCone.TangentialMean, referenceCone.SagittalMean);
                var result = ComputeFftPsfCore(optic, field, wave, pupilSampling,
                    2 * pupilSampling, false, true, 0, null, null, true, ignoreOpd,
                    optic.RayAimingEnabled, reference, commonPitch, true, out _, referenceSphere, grid);
                return (wave, result with { SampleSpacingMicrometers = commonPitch });

                double CoordinateStep(double numericalAperture) =>
                    1 / (pupilSampling * commonPitch / wave.Micrometers / (.5 / numericalAperture));
            }).ToArray();
        }
        // Monochromatic expanded grids use their own cutoff. Keep the captured
        // common-grid convention below for a true polychromatic mixture.
        if (pupilSampling > 32 && wavelengths.Count == 1)
        {
            var scaleCone = CumulativeEnergyCone(optic, field, scaleWave);
            var cones = wavelengths.Select(wave => (Wave: wave, Cone: CumulativeEnergyCone(optic, field, wave))).ToArray();
            if (scaleCone is { } scale && cones.All(item => item.Cone.HasValue))
            {
                // The inverse cutoff sets image pitch. Scale the documented
                // default FFT grid to that pitch before generating coordinates;
                // canceling its stretch algebraically changes boundary nodes.
                var cutoff = 2 * scale.Rms / longest.Micrometers;
                // Form the normalized frequency interval before converting it
                // through the cutoff. Moving the square root into a rounded
                // denominator can drop diagonal rim nodes on expanded grids.
                var imagePitch = pupilSampling >= 256
                    ? ExpandedCumulativeEnergyPitch(longest.Micrometers, scale.Rms, pupilSampling)
                    : Math.Sqrt(1d / pupilSampling) / cutoff;
                var automaticStretch = Math.Sqrt(pupilSampling / 32d);
                var referenceSphere = CumulativeEnergyReferenceSphere(optic, field, reference);
                return cones.Select(item =>
                {
                    var cone = item.Cone!.Value;
                    PupilGridSpecification pupilGrid;
                    if (cone.TangentialMean == cone.SagittalMean
                        && (cone.Rms == Math.BitIncrement(cone.TangentialMean)
                            || pupilSampling >= 256 && cone.Rms == cone.TangentialMean))
                    {
                        // Equal dense circular cones, and adjacent rounded
                        // moments of one cone, must share the same cutoff and
                        // closed pupil rim. Successive F-number/extent
                        // conversions can otherwise remove the four axial
                        // boundary samples even when the cone is identical.
                        // Use the common RMS cone's Fourier coordinate interval;
                        // retain that interval through tracing and FFT indexing.
                        var step = (scale.Rms / cone.Rms) / Math.Sqrt(pupilSampling);
                        pupilGrid = new(pupilSampling, true, true, step * (pupilSampling / 2d - 1))
                        {
                            ExplicitCoordinateStep = step
                        };
                    }
                    else if (pupilSampling >= 256)
                    {
                        pupilGrid = ExpandedCumulativeEnergyGrid(optic, field, item.Wave, pupilSampling,
                            imagePitch, cone.TangentialMean, cone.SagittalMean);
                    }
                    else
                    {
                        var stretchX = PupilStretch(cone.SagittalMean);
                        var stretchY = PupilStretch(cone.TangentialMean);
                        pupilGrid = new(pupilSampling, true, true, stretchX) { StretchY = stretchY };
                    }
                    pupilGrid = pupilGrid with { UseCachedFftStopMap = true };
                    var result = ComputeFftPsfCore(optic, field, item.Wave, pupilSampling,
                        2 * pupilSampling, false, true, 0, null, null, true, ignoreOpd,
                        optic.RayAimingEnabled, reference, imagePitch, true, out _, referenceSphere, pupilGrid);
                    return (item.Wave, result with { SampleSpacingMicrometers = imagePitch });

                    double PupilStretch(double numericalAperture)
                    {
                        var axisFNumber = .5 / numericalAperture;
                        if (pupilSampling >= 128)
                        {
                            // High sampling retains the shared FFT's default
                            // extent before changing its directional F-number.
                            // Rebuilding that extent from the directional cone
                            // directly changes diagonal boundary nodes.
                            var fftFNumber = WorkingFNumber(optic, field, item.Wave, optic.RayAimingEnabled);
                            var defaultFftPitch = item.Wave.Micrometers * fftFNumber * (pupilSampling - 2)
                                / (2 * pupilSampling * automaticStretch);
                            var extentPerFNumber = automaticStretch * defaultFftPitch / imagePitch / fftFNumber;
                            return extentPerFNumber * axisFNumber;
                        }
                        var defaultPitch = item.Wave.Micrometers * axisFNumber * (pupilSampling - 2)
                            / (2 * pupilSampling * automaticStretch);
                        return automaticStretch * defaultPitch / imagePitch;
                    }
                }).ToArray();
            }
        }
        // Work in the shared tracer's millimeters. The inverse normalized
        // frequency defines pupil coordinates directly; reconstructing them
        // through a rounded grid stretch changes circular boundary nodes.
        var referenceFNumber = WorkingFNumber(optic, field, scaleWave, optic.RayAimingEnabled);
        var pitchMillimeters = (longest.Micrometers * .001 / Math.Sqrt(pupilSampling)) * referenceFNumber;
        if (pupilSampling >= 128)
        {
            // Preserve the common-grid and unavailable-full-cone conventions.
            // Expanded single-wavelength grids with a full cone use the
            // cutoff/default-grid scaling path above.
            pitchMillimeters = longest.Micrometers * referenceFNumber * (pupilSampling - 2)
                / (2 * pupilSampling * Math.Sqrt(pupilSampling / 4d - 1)) * .001;
        }
        var pitch = pitchMillimeters * 1000;
        var sphere = CumulativeEnergyReferenceSphere(optic, field, reference);
        return wavelengths.Select(wave =>
        {
            var fNumber = WorkingFNumber(optic, field, wave, optic.RayAimingEnabled);
            // The image pitch uses the system's RMS cone, while each pupil axis
            // uses its own mean marginal NA. Combining those axes before tracing
            // changes the aperture support for asymmetric cones.
            var axes = WorkingFNumbers(optic, field, wave, optic.RayAimingEnabled, zemaxDirectionalAverage: true);
            var stepX = CoordinateStep(axes.Sagittal);
            var stepY = CoordinateStep(axes.Tangential);
            var grid = new PupilGridSpecification(pupilSampling, true, true, stepX * (pupilSampling / 2d - 1))
            {
                ExplicitCoordinateStep = stepX,
                ExplicitCoordinateStepY = stepY,
                UseCachedFftStopMap = true
            };
            var result = ComputeFftPsfCore(
                optic, field, wave, pupilSampling, 2 * pupilSampling, false, true, 0,
                null, null, true, ignoreOpd, optic.RayAimingEnabled, reference,
                pitch, true, out _, sphere, grid);
            return (wave, result with { SampleSpacingMicrometers = pitch });

            double CoordinateStep(double axisFNumber)
            {
                // Preserve a circular cone when the apparent axis difference is
                // only roundoff from taking and recombining square roots.
                if (Math.Abs(axisFNumber - fNumber) <= 1e-14 * fNumber) axisFNumber = fNumber;
                return 1 / (pupilSampling * pitchMillimeters / (wave.Micrometers * .001) / axisFNumber);
            }
        }).ToArray();
    }

    // Dense cumulative grids form the physical image interval in the shared
    // tracer's millimeters before converting to the analysis's micrometers.
    // Algebraic cancellation through the cutoff changes circular rim nodes.
    private static double ExpandedCumulativeEnergyPitch(double wavelengthMicrometers, double rmsNa, int sampling) =>
        (wavelengthMicrometers * .001) * (.5 / rmsNa) / Math.Sqrt(sampling) * 1000;

    private static PupilGridSpecification ExpandedCumulativeEnergyGrid(
        Optic optic, (double Hx, double Hy) field, Wavelength wavelength, int sampling,
        double imagePitch, double tangentialNa, double sagittalNa)
    {
        var stretch = Math.Sqrt(sampling / 32d);
        var fftFNumber = WorkingFNumber(optic, field, wavelength, optic.RayAimingEnabled);
        var defaultPitch = wavelength.Micrometers * fftFNumber * (sampling - 2)
            / (2 * sampling * stretch);
        var extentPerFNumber = stretch * defaultPitch / imagePitch / fftFNumber;
        // Keep the interval, rather than reconstructing coordinates from an
        // equivalent extent; tracing and Fourier indexing must use it together.
        var stepX = extentPerFNumber * (.5 / sagittalNa) / (sampling / 2d - 1);
        var stepY = extentPerFNumber * (.5 / tangentialNa) / (sampling / 2d - 1);
        return new(sampling, true, true, stepX * (sampling / 2d - 1))
        {
            ExplicitCoordinateStep = stepX,
            ExplicitCoordinateStepY = stepY,
            UseCachedFftStopMap = true
        };
    }

    private static (double Rms, double TangentialMean, double SagittalMean)? CumulativeEnergyCone(
        Optic optic, (double Hx, double Hy) field, Wavelength wavelength)
    {
        if (!TryWorkingPupilDirections(optic, field, wavelength, optic.RayAimingEnabled, 1, out var directions)) return null;
        var chief = directions[0];
        var index = optic.SurfaceGroup.Items[^1].MaterialAfter.RefractiveIndex(wavelength.Nanometers);
        var numericalApertures = directions.Skip(1).Select(direction =>
            index * optic.Backend.Current.Cross(chief, direction).Length / (chief.Length * direction.Length)).ToArray();
        if (numericalApertures.Any(value => !double.IsFinite(value) || value <= 0)) return null;
        var rms = CumulativeEnergyRootMeanSquare(numericalApertures);
        // Retain the existing working-F-number cap through the fallback path.
        if (!double.IsFinite(rms) || .5 / rms > 10000) return null;
        return (rms, (numericalApertures[0] + numericalApertures[1]) / 2,
            (numericalApertures[2] + numericalApertures[3]) / 2);
    }
}
