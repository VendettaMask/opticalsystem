using System.Numerics;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    /// <summary>Same polychromatic finite Fourier sum as the dense energy grid,
    /// evaluated only on a complete physical window. Spectral intensities are
    /// combined about one power-weighted centroid; complex amplitudes never mix.</summary>
    internal static IBoundedFftEnergyImageGrid ComputeBoundedSpectralCumulativeEnergy(
        Optic optic, (double Hx, double Hy) field, IReadOnlyList<Wavelength> wavelengths,
        int sampling, string referenceKind, string type, double maximumDistance, bool ignoreOpd)
    {
        if (wavelengths.Count < (optic.ImageSpaceAfocal ? 1 : 2) || sampling < 256
            || (sampling & (sampling - 1)) != 0 || !(maximumDistance > 0) || !double.IsFinite(maximumDistance)
            || type.StartsWith("X", StringComparison.OrdinalIgnoreCase) || type.StartsWith("Y", StringComparison.OrdinalIgnoreCase))
            throw new AnalysisDataUnavailableException("Diffraction Encircled Energy",
                "bounded spectral energy requires a radial/square grid and a positive maximum distance");
        var reference = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0];
        var angular = optic.ImageSpaceAfocal;
        var cone = angular ? null : CumulativeEnergyCone(optic, field, reference);
        if (!angular && cone is null)
            throw new AnalysisDataUnavailableException("Diffraction Encircled Energy", "no finite reference marginal cone");
        var longest = wavelengths.MaxBy(w => w.Micrometers)!;
        var nativePitch = angular ? AfocalCumulativeEnergyPitch(optic, longest, sampling)
            : ExpandedCumulativeEnergyPitch(longest.Micrometers, cone!.Value.Rms, sampling);
        var sphere = angular ? null : CumulativeEnergyReferenceSphere(optic, field, reference);
        var pupils = new List<(Complex[,] Values, double Scale, bool Aiming)>();
        var maximumWeight = wavelengths.Max(w => w.Weight);
        var minimumWavelength = wavelengths.Min(w => w.Micrometers);
        long pupilCells = 0;
        foreach (var wave in wavelengths)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var source = angular ? AfocalCumulativeEnergyGrid(optic, field, wave, longest, sampling)
                : ExpandedCumulativeEnergyGrid(optic, field, wave, sampling, nativePitch,
                    cone!.Value.TangentialMean, cone.Value.SagittalMean);
            if (source.Coordinate(0) > -1 + 1e-12 || source.Coordinate(sampling - 1) < 1 - 1e-12
                || source.YCoordinate(0) > -1 + 1e-12 || source.YCoordinate(sampling - 1) < 1 - 1e-12)
                throw new AnalysisDataUnavailableException("Diffraction Encircled Energy", "the source Fourier grid does not cover the complete physical pupil");
            var grid = CompactFftPupilGrid(source);
            pupilCells += (long)grid.SampleCount * grid.SampleCount;
            if (pupilCells > AnalysisResourceLimits.MaximumAggregateGridCells)
                throw new ArgumentOutOfRangeException(nameof(sampling), "Spectral pupil storage exceeds the aggregate budget");
            // This small readout supplies only the shared complex pupil. Its
            // metadata pitch is discarded; nativePitch belongs to the original
            // source grid, whose complete support was checked before compaction.
            var metadata = ComputeFftPsfCore(optic, field, wave, grid.SampleCount, CompactPupilTransformSize(grid.SampleCount),
                false, true, 0, null, null, true, ignoreOpd, optic.RayAimingEnabled, reference, 0, true,
                out var pupil, sphere, grid, interpolateScalarPupilAmplitude: angular, croppedOutputSize: 2);
            metadata.PupilPhaseSampling?.EnforceForCurrentRequest("Diffraction Encircled Energy");
            var nonzero = pupil.Cast<Complex>().Count(value => value.Magnitude > 0);
            var weight = (maximumWeight > 0 ? Math.Max(0, wave.Weight) / maximumWeight : 1)
                * Math.Pow(minimumWavelength / wave.Micrometers, 2);
            pupils.Add((pupil, weight / (nonzero * (double)nonzero), metadata.UseRayAiming));
        }

        var logical = checked(2 * sampling);
        var pitch = nativePitch;
        var largestPupil = pupils.Max(p => p.Values.GetLength(0));
        while (!FitsReadout())
        {
            if (logical < 4 * largestPupil)
                throw new ArgumentOutOfRangeException(nameof(maximumDistance), "No complete spectral energy window fits the resource budget");
            logical /= 2; pitch *= 2;
        }
        var moments = pupils.Select(p => FourierImageMoments(p.Values, logical)).ToArray();
        var total = pupils.Select((p, i) => p.Scale * moments[i].Total).Sum();
        if (!(total > 0) || !double.IsFinite(total))
            throw new AnalysisDataUnavailableException("Diffraction Encircled Energy", "no finite spectral power");
        var center = (X: 0d, Y: 0d);
        if (referenceKind.Equals("centroid", StringComparison.OrdinalIgnoreCase))
            center = (pupils.Select((p, i) => p.Scale * moments[i].Total * moments[i].X).Sum() / total * pitch,
                pupils.Select((p, i) => p.Scale * moments[i].Total * moments[i].Y).Sum() / total * pitch);
        if (referenceKind.Equals("vertex", StringComparison.OrdinalIgnoreCase))
        {
            var waveNumber = optic.Wavelengths.ToList().IndexOf(reference) + 1;
            var chief = RayBundleMetrics.Trace(optic, optic.SurfaceGroup.Items[^1].Number, waveNumber,
                field.Hx, field.Hy, [new Raytrace.PupilSample(0, 0, 1)], requireUnvignetted: true,
                includeSurfaceTransmission: false, aimAtStop: pupils[0].Aiming)[0];
            var coordinate = angular
                ? ImageSpaceAnalysisSupport.DirectionAnglesMilliradians(optic.SurfaceGroup.Items[^1], chief.Direction)
                : (X: chief.Position.X * 1000, Y: chief.Position.Y * 1000);
            center = (-coordinate.X, -coordinate.Y);
        }
        var firstX = checked((int)Math.Floor((center.X - maximumDistance - nativePitch / 2) / pitch));
        var firstY = checked((int)Math.Floor((center.Y - maximumDistance - nativePitch / 2) / pitch));
        var width = checked((int)Math.Ceiling((center.X + maximumDistance + nativePitch / 2) / pitch) - firstX + 1);
        var height = checked((int)Math.Ceiling((center.Y + maximumDistance + nativePitch / 2) / pitch) - firstY + 1);
        var grids = new List<(FftCumulativeKnotGrid Grid, double Weight)>();
        for (var i = 0; i < pupils.Count; i++)
        {
            var pupil = pupils[i];
            if (pupil.Scale == 0) continue;
            var image = FourierTransformWindow(pupil.Values, logical, firstX, firstY, width, height,
                additionalButterflies: FourierImageMomentButterflies(pupil.Values));
            grids.Add((new(image, firstX, firstY, pitch, center, moments[i].Total, nativePitch,
                maximumDistance, sampling, type, sampling), pupil.Scale * moments[i].Total / total));
        }
        return new SpectralCumulativeWindow(grids, sampling, width, pitch, nativePitch, maximumDistance);

        bool FitsReadout()
        {
            var side = Math.Ceiling((2 * maximumDistance + nativePitch) / pitch) + 2;
            return logical >= 2 * largestPupil && side is >= 2 and <= AnalysisResourceLimits.MaximumFftGridSize
                && pupilCells + largestPupil * side + side * side <= AnalysisResourceLimits.MaximumAggregateGridCells;
        }
    }

    private sealed class SpectralCumulativeWindow(IReadOnlyList<(FftCumulativeKnotGrid Grid, double Weight)> grids,
        int sampling, int windowSize, double pitch, double binWidth, double maximum) : IBoundedFftEnergyImageGrid
    {
        public int FourierSampling => sampling;
        public int WindowSize => windowSize;
        public double ImagePitch => pitch;
        public double BinWidth => binWidth;
        public double Fraction(double distance) => grids.Sum(g => g.Weight * g.Grid.Fraction(distance));
        public double RadiusContaining(double target)
        {
            if (target <= 0) return 0;
            if (target > Fraction(maximum)) throw new InvalidOperationException("Energy quantile lies outside the evaluated spectral window.");
            var lower = 0d; var upper = maximum;
            for (var i = 0; i < 60; i++)
            {
                var middle = (lower + upper) / 2;
                if (Fraction(middle) < target) lower = middle; else upper = middle;
            }
            return upper;
        }
    }
}
