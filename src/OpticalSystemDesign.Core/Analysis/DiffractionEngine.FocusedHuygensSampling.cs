using System.Numerics;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

/// <summary>Agreement of two radial quadratures on the fixed physical image window.</summary>
public sealed record FocusedHuygensSamplingDiagnostics(int CoarsePanels, int FinePanels,
    int CoarseNodes, int FineNodes, long CumulativeKernelEvaluations, double MaximumPhaseStepWaves,
    double PeakRelativeIntensityDisagreement, double RelativeReferenceAmplitudeDisagreement);

public static partial class DiffractionEngine
{
    private const int MaximumFocusedRadialPanels = 16_384;
    private const double FocusedHuygensRelativeTolerance = 2e-4;
    private const double MaximumRadialBesselArgument = 18;
    private static readonly double[] RadialAzimuthCosines = Enumerable.Range(0, 12)
        .Select(index => Math.Cos(Math.PI * (index + .5) / 24)).ToArray();

    private readonly record struct RadialHuygensWavelet(double Phase, double Frequency, double Amplitude);
    private sealed record RadialHuygensTrial(double[]? Intensities, int Nodes, double Reference, double PhaseStep);

    internal static PsfResult ComputeCheckedFocusedHuygensPsf(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int pupilSampling, int imageSize, double pitch)
    {
        const string analysis = "Huygens PSF";
        var surfaces = optic.SurfaceGroup.Items;
        var image = surfaces.LastOrDefault();
        if (optic.ImageSpaceAfocal || field != (0d, 0d) || optic.RayAimingEnabled
            || optic.FieldDefinition != FieldDefinitionKind.Angle || !ObjectConjugate.IsInfinite(surfaces.FirstOrDefault())
            || optic.SequentialRayTracer.RayGenerator.GetPupilVignetting(0, 0) != PupilVignetting.Identity
            || optic.Apodization is not (null or UniformApodization or ZemaxApodization)
            || image?.Geometry is not PlaneGeometry || !surfaces.All(IsCenteredRefractingSurface))
            throw new AnalysisDataUnavailableException(analysis,
                "checked focal Huygens integration requires a scalar axial field, a centered circular pupil without aiming, and a coaxial refractive system with a plane image");
        AnalysisResourceLimits.ValidateAnalysisGrid(imageSize, imageSize, analysis);
        var pupilDistance = Math.Abs(optic.Paraxial.EstimateExitPupilLocation());
        if (!double.IsFinite(pitch) || pitch <= 0 || !double.IsFinite(pupilDistance) || pupilDistance <= 0
            || UsesSphericalFocusedHuygens(optic, wavelength, pupilDistance, imageSize, pitch, null))
            throw new AnalysisDataUnavailableException(analysis,
                "checked focal Huygens integration requires a finite planar propagation window");
        var frame = CreateHuygensImageFrame(optic, field, wavelength);
        var lambda = wavelength.Micrometers * .001;
        var k = 2 * Math.PI / lambda;
        var radiiSquared = new int[imageSize, imageSize];
        var uniqueRadii = new SortedSet<int>();
        for (var row = 0; row < imageSize; row++)
            for (var column = 0; column < imageSize; column++)
            {
                var x = column - imageSize / 2;
                var y = row - imageSize / 2;
                radiiSquared[row, column] = x * x + y * y;
                uniqueRadii.Add(radiiSquared[row, column]);
            }
        var radialIndex = uniqueRadii.Select((value, index) => (value, index)).ToDictionary(p => p.value, p => p.index);
        var radii = uniqueRadii.Select(value => pitch * Math.Sqrt(value)).ToArray();
        var endpoints = WavefrontEngine.GenerateChiefRayBatch(optic, field, wavelength,
            [new PupilSample(0, 0, 1), new PupilSample(1, 0, 1)], false);
        if (endpoints.Samples.Any(sample => !(sample.Intensity > 0)))
            throw new AnalysisDataUnavailableException(analysis, "checked focal Huygens integration requires a transmitted full circular pupil");

        long operations = 0;
        var panels = Math.Clamp(pupilSampling, 16, MaximumFocusedRadialPanels);
        while (true)
        {
            var coarse = Evaluate(panels, 8);
            var finePanels = panels == MaximumFocusedRadialPanels ? panels - 1 : panels + 1;
            if (coarse.Intensities is { } coarseValues)
            {
                var fine = Evaluate(finePanels, 12);
                if (fine.Intensities is { } fineValues)
                {
                    var peak = fineValues.Max();
                    var intensityError = peak > 0 ? coarseValues.Zip(fineValues,
                        (first, second) => Math.Abs(first - second) / peak).Max() : double.PositiveInfinity;
                    var referenceError = Math.Abs(coarse.Reference - fine.Reference) / fine.Reference;
                    if (intensityError <= FocusedHuygensRelativeTolerance && referenceError <= FocusedHuygensRelativeTolerance)
                    {
                        var values = new double[imageSize, imageSize];
                        for (var row = 0; row < imageSize; row++)
                            for (var column = 0; column < imageSize; column++)
                                values[row, column] = fineValues[radialIndex[radiiSquared[row, column]]];
                        return new PsfResult(values, pupilSampling, imageSize, WorkingFNumber(optic, field, wavelength, false), pitch * 1000)
                        {
                            PupilQuadratureNodeCount = fine.Nodes,
                            HuygensReferenceAmplitude = fine.Reference,
                            FocusedHuygensSampling = new(panels, finePanels, coarse.Nodes, fine.Nodes, operations,
                                Math.Max(coarse.PhaseStep, fine.PhaseStep) / (2 * Math.PI), intensityError, referenceError)
                        };
                    }
                }
            }
            if (panels == MaximumFocusedRadialPanels)
                throw new AnalysisDataUnavailableException(analysis, "radial pupil sampling did not stabilize within the checked integration limit");
            panels = Math.Min(2 * panels, MaximumFocusedRadialPanels);
        }

        RadialHuygensTrial Evaluate(int count, int order)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var nodes = count * order;
            var unit = ApertureSampler.GenerateHuygensGaussianQuadrature(order, 4, 0)
                .Where((_, index) => index % 4 == 0).Select(p => (T: p.X * p.X, W: 4 * p.Weight)).ToArray();
            var samples = Enumerable.Range(0, count).SelectMany(panel => unit.Select(node =>
            {
                var radius = (panel + node.T) / count;
                return (Point: new PupilSample(radius, 0, 1), Weight: 2 * radius * node.W / count);
            }));
            var wavelets = new List<RadialHuygensWavelet>(nodes);
            var reference = 0d;
            var phaseStep = 0d;
            foreach (var batch in samples.Chunk(HuygensFourierSum.PupilBatchSize))
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var wavefront = WavefrontEngine.GenerateChiefRayBatch(optic, field, wavelength, batch.Select(p => p.Point).ToArray(), false);
                for (var index = 0; index < wavefront.Samples.Count; index++)
                {
                    var sample = wavefront.Samples[index];
                    if (!(sample.Intensity > 0) || sample.ImageDirection is not { Length: > 0 } direction)
                        throw new AnalysisDataUnavailableException(analysis, "a checked radial pupil ray was clipped or did not reach the image");
                    var amplitude = FieldAmplitudeFromPower(sample.LaunchIntensity ?? sample.Intensity) * batch[index].Weight;
                    var pupil = new Vector3D(sample.PupilX, sample.PupilY, sample.PupilZ);
                    var phase = -2 * Math.PI * sample.OpdWaves + k * wavefront.ImageRefractiveIndex
                        * (Dot(frame.Center - pupil, direction) - wavefront.Radius);
                    var dx = Dot(frame.TangentX, direction);
                    var dy = Dot(frame.TangentY, direction);
                    var frequency = k * wavefront.ImageRefractiveIndex * Math.Sqrt(dx * dx + dy * dy);
                    if (!double.IsFinite(phase) || !(frequency * radii[^1] <= MaximumRadialBesselArgument))
                        throw new AnalysisDataUnavailableException(analysis, "checked focal Huygens image window exceeds the bounded angular integral");
                    if (wavelets.Count > 0) phaseStep = Math.Max(phaseStep, Math.Abs(phase - wavelets[^1].Phase));
                    reference += amplitude;
                    wavelets.Add(new(phase, frequency, amplitude));
                }
            }
            // Agreement from two aliased grids does not establish convergence.
            if (phaseStep > Math.PI) return new(null, nodes, reference, phaseStep);
            operations += (long)nodes * radii.Length;
            if (operations > AnalysisResourceLimits.MaximumDirectPsfOperations || !(reference > 0) || !double.IsFinite(reference))
                throw new AnalysisDataUnavailableException(analysis, "checked focal Huygens pupil exceeded its work or normalization limit");

            var fieldSum = new Complex[radii.Length];
            var compensation = new Complex[radii.Length];
            foreach (var wavelet in wavelets)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var value = Complex.FromPolarCoordinates(wavelet.Amplitude, wavelet.Phase);
                for (var index = 0; index < radii.Length; index++)
                {
                    var add = value * BoundedBesselJ0(wavelet.Frequency * radii[index]) - compensation[index];
                    var sum = fieldSum[index] + add;
                    compensation[index] = (sum - fieldSum[index]) - add;
                    fieldSum[index] = sum;
                }
            }
            return new(fieldSum.Select(sum => 100 * Math.Pow(sum.Magnitude / reference, 2)).ToArray(), nodes, reference, phaseStep);
        }
    }

    private static bool IsCenteredRefractingSurface(OpticalSurface surface) =>
        surface.CoordinateSystem.Origin.X == 0 && surface.CoordinateSystem.Origin.Y == 0
        && surface.CoordinateSystem.RotationXDegrees == 0 && surface.CoordinateSystem.RotationYDegrees == 0
        && surface.Geometry is PlaneGeometry { ReferenceIncidenceNormal: null } or StandardGeometry
        && surface.PhysicalAperture is null or CircularAperture
        && surface.ScatteringModel is null
        && surface.InteractionModel is RefractiveReflectiveInteractionModel { IsReflective: false }
        && surface.MaterialBefore.PropagationModel is HomogeneousPropagationModel
        && surface.MaterialAfter.PropagationModel is HomogeneousPropagationModel;

    private static double BoundedBesselJ0(double argument)
    {
        if (argument > 8)
        {
            // The 48-angle midpoint rule's first nonzero alias is J48.
            // At |argument| <= 18 the full angular truncation is below 6e-15.
            // Cosine symmetry reduces the same rule to a quarter-circle sum.
            var angularSum = 0d;
            foreach (var cosine in RadialAzimuthCosines) angularSum += Math.Cos(argument * cosine);
            return angularSum / RadialAzimuthCosines.Length;
        }
        var term = 1d;
        var result = 1d;
        var factor = -argument * argument / 4;
        for (var order = 1; order < 48; order++)
        {
            term *= factor / (order * order);
            result += term;
            if (Math.Abs(term) <= 1e-17) break;
        }
        return result;
    }
}
