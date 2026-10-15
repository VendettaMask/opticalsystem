using System.Numerics;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class FoucaultEngine
{
    private sealed record PlaneVertex(double X, double Y, double Cosine, ComplexElectricField[] Fields);

    private static FoucaultResult ComputeSpatial(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int sampling, FoucaultKnifeEdge knife, double positionMicrometers,
        bool polarized, FoucaultPropagationMode mode, double knifeDistance, double observationDistance, double halfWidth)
    {
        if (!double.IsFinite(knifeDistance) || !double.IsFinite(observationDistance) || !double.IsFinite(halfWidth)
            || knifeDistance < 0 || observationDistance < 0 || halfWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(knifeDistance), "Forward plane distances and observation half-width must be finite and nonnegative.");
        var vector = mode == FoucaultPropagationMode.VectorAngularSpectrum;
        if (polarized && !vector) throw new ArgumentException("Polarized spatial propagation requires the Maxwell vector mode.");
        var gridSize = AnalysisResourceLimits.RoundUpPowerOfTwo(4 * sampling, nameof(sampling));
        if (vector && 12L * gridSize * gridSize > AnalysisResourceLimits.MaximumAggregateGridCells)
            throw new ArgumentOutOfRangeException(nameof(sampling), "Vector propagation exceeds the aggregate field-grid budget; use at most 128 pupil samples.");
        var wavefront = WavefrontEngine.GenerateImagePlaneUniform(optic, field, wavelength, sampling, optic.RayAimingEnabled);
        var chief = optic.SequentialRayTracer.TraceGenericFinalSample(field.Hx, field.Hy, 0, 0,
            wavelength.Micrometers, optic.RayAimingEnabled)
            ?? throw new AnalysisDataUnavailableException("Foucault Analysis", "no chief ray reached the source plane");
        var z = ExitPupilGeometryEngine.Unit(chief.Direction);
        var imageFrame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
        var imageX = imageFrame.ToGlobalDirection(new(1, 0, 0));
        var x = ExitPupilGeometryEngine.Unit(imageX - z * ExitPupilGeometryEngine.Dot(imageX, z));
        var y = ExitPupilGeometryEngine.Cross(z, x);
        var medium = optic.SurfaceGroup.Items[^1].MaterialAfter;
        var index = medium.RefractiveIndex(wavelength.Nanometers);
        if (!double.IsFinite(index) || index <= 0 || medium.ExtinctionCoefficient(wavelength.Nanometers) != 0
            || medium.PropagationModel is not HomogeneousPropagationModel)
            throw new NotSupportedException("Spatial Foucault currently requires a transparent homogeneous output medium.");
        var states = !polarized ? Array.Empty<JonesInputState>() : optic.Polarization.Unpolarized
            ? new[] { new JonesInputState(1, 0, ReferenceAxis: optic.Polarization.ReferenceAxis),
                new JonesInputState(0, 1, ReferenceAxis: optic.Polarization.ReferenceAxis) }
            : new[] { optic.Polarization.Input };
        var stateCount = Math.Max(1, states.Length);
        var vertices = new PlaneVertex?[sampling, sampling];
        var illuminated = 0;
        foreach (var sample in wavefront.Samples.Where(p => p.Intensity > 0))
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (!double.IsFinite(sample.Intensity) || !double.IsFinite(sample.OpdWaves))
                throw new ArithmeticException("Non-finite spatial Foucault input.");
            var delta = new Vector3D(sample.PupilX, sample.PupilY, sample.PupilZ) - chief.Position;
            var direction = sample.ImageDirection ?? throw new InvalidOperationException("Missing source-plane ray direction.");
            var cosine = ExitPupilGeometryEngine.Dot(ExitPupilGeometryEngine.Unit(direction), z);
            if (cosine <= 1e-8) throw new NotSupportedException("Folded/backward source-plane wavefront needs a different propagation frame.");
            var phase = Complex.FromPolarCoordinates(1, -2 * Math.PI * Math.IEEERemainder(sample.OpdWaves, 1));
            var fields = new ComplexElectricField[stateCount];
            if (!polarized) fields[0] = new(Math.Sqrt(sample.Intensity) * phase, 0, 0);
            else
            {
                var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(field.Hx, field.Hy,
                    sample.NormalizedPupilX, sample.NormalizedPupilY, wavelength.Micrometers, optic.RayAimingEnabled).Rays.Single();
                for (var state = 0; state < states.Length; state++)
                {
                    var local = optic.SequentialRayTracer.TracePolarized(ray, states[state],
                        inputCoordinates: optic.SurfaceGroup.Items[0].CoordinateSystem,
                        timeConvention: HarmonicTimeConvention.Negative, includePropagationPhase: false).LocalField;
                    Complex Component(Vector3D axis) => local.Dot(imageFrame.ToLocalDirection(axis));
                    fields[state] = new ComplexElectricField(Component(x), Component(y), Component(z)) * (phase / Math.Sqrt(stateCount));
                }
            }
            var row = (int)Math.Round(((sample.NormalizedPupilY + 1) * sampling - 1) / 2);
            var column = (int)Math.Round(((sample.NormalizedPupilX + 1) * sampling - 1) / 2);
            vertices[row, column] = new(ExitPupilGeometryEngine.Dot(delta, x), ExitPupilGeometryEngine.Dot(delta, y), cosine, fields);
            illuminated++;
        }
        var valid = vertices.Cast<PlaneVertex?>().Where(v => v is not null).Select(v => v!).ToArray();
        if (valid.Length < 3) throw new AnalysisDataUnavailableException("Foucault Analysis", "insufficient source-plane support");
        var extent = valid.Max(v => Math.Max(Math.Abs(v.X), Math.Abs(v.Y)));
        if (halfWidth == 0) halfWidth = extent * 1.05;
        if (!double.IsFinite(halfWidth) || halfWidth <= extent)
            throw new ArgumentOutOfRangeException(nameof(halfWidth), "Observation extent must cover the complete sampled source pupil with a margin.");
        var pitch = 2 * halfWidth / sampling;
        var maximumTransverse = wavefront.Samples.Where(p => p.Intensity > 0).Max(p =>
        {
            var d = ExitPupilGeometryEngine.Unit(p.ImageDirection!.Value);
            return Math.Max(Math.Abs(ExitPupilGeometryEngine.Dot(d, x)), Math.Abs(ExitPupilGeometryEngine.Dot(d, y)));
        });
        if (index * maximumTransverse / (wavelength.Micrometers * .001) > .5 / pitch)
            throw new AnalysisDataUnavailableException("Foucault Analysis", "source-plane ray slopes exceed the physical-grid Nyquist limit; refine sampling or define a supported source plane");
        var pupils = Rasterize(vertices, stateCount, gridSize, pitch, index, vector);
        var intensity = new double[sampling, sampling]; var offset = (gridSize - sampling) / 2;
        double inputPower = 0, outputPower = 0, displayedPower = 0, projectionDifference = 0;
        foreach (var pupil in pupils)
        {
            if (vector)
            {
                var initial = AngularSpectrumEngine.PropagateVector(pupil, pitch, pitch, wavelength.Micrometers * .001, index, 0);
                inputPower += initial.NormalFluxIntegral; projectionDifference += initial.ProjectionDifferenceNormIntegral;
                var blade = AngularSpectrumEngine.PropagateVector(initial.Electric, pitch, pitch, wavelength.Micrometers * .001,
                    index, knifeDistance, projectTransverse: false).Electric;
                Mask(blade, pitch, knife, positionMicrometers * .001);
                var observed = AngularSpectrumEngine.PropagateVector(blade, pitch, pitch, wavelength.Micrometers * .001, index, observationDistance);
                outputPower += observed.NormalFluxIntegral; projectionDifference += observed.ProjectionDifferenceNormIntegral;
                for (var row = 0; row < sampling; row++)
                for (var column = 0; column < sampling; column++)
                {
                    var e = observed.Electric[row + offset, column + offset];
                    var h = observed.Magnetic[row + offset, column + offset];
                    intensity[row, column] += e.SquaredNorm;
                    displayedPower += (e.X * Complex.Conjugate(h.Y) - e.Y * Complex.Conjugate(h.X)).Real * pitch * pitch;
                }
            }
            else
            {
                var scalar = new Complex[gridSize, gridSize];
                for (var row = 0; row < gridSize; row++)
                for (var column = 0; column < gridSize; column++) scalar[row, column] = pupil[row, column].X;
                inputPower += scalar.Cast<Complex>().Sum(Power) * pitch * pitch;
                var blade = AngularSpectrumEngine.PropagateScalar(scalar, pitch, pitch, wavelength.Micrometers * .001, index, knifeDistance);
                Mask(blade, pitch, knife, positionMicrometers * .001);
                var observed = AngularSpectrumEngine.PropagateScalar(blade, pitch, pitch, wavelength.Micrometers * .001, index, observationDistance);
                outputPower += observed.Cast<Complex>().Sum(Power) * pitch * pitch;
                for (var row = 0; row < sampling; row++)
                for (var column = 0; column < sampling; column++)
                {
                    var value = Power(observed[row + offset, column + offset]);
                    intensity[row, column] += value; displayedPower += value * pitch * pitch;
                }
            }
        }
        if (!double.IsFinite(inputPower) || inputPower <= 0 || !double.IsFinite(outputPower) || !double.IsFinite(displayedPower)
            || intensity.Cast<double>().Any(v => !double.IsFinite(v))) throw new ArithmeticException("Invalid spatial Foucault field/flux.");
        return new(intensity, gridSize, pitch * 1000, pitch * 1000, inputPower, outputPower,
            displayedPower, illuminated, wavefront.VignettedRayCount, stateCount * (vector ? 3 : 1))
        { PropagationMode = mode, ObservationHalfWidthMicrometers = halfWidth * 1000,
            TransverseProjectionDifferenceNormIntegral = projectionDifference };
    }

    private static ComplexElectricField[][,] Rasterize(PlaneVertex?[,] vertices, int states, int size, double pitch,
        double index, bool vector)
    {
        var sampling = vertices.GetLength(0);
        var result = Enumerable.Range(0, states).Select(_ => new ComplexElectricField[size, size]).ToArray();
        var covered = new bool[size, size]; long work = 0; int orientation = 0;
        for (var row = 0; row < sampling - 1; row++)
        for (var column = 0; column < sampling - 1; column++)
        {
            var corners = new[] { vertices[row, column], vertices[row, column + 1],
                vertices[row + 1, column + 1], vertices[row + 1, column] };
            var validCorners = corners.Where(p => p is not null).ToArray();
            if (validCorners.Length == 3) Fill(validCorners[0], validCorners[1], validCorners[2]);
            else if (validCorners.Length == 4)
            {
                Fill(corners[0], corners[1], corners[2]);
                Fill(corners[0], corners[2], corners[3]);
            }
        }
        return result;

        void Fill(PlaneVertex? a, PlaneVertex? b, PlaneVertex? c)
        {
            if (a is null || b is null || c is null) return;
            var det = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
            if (Math.Abs(det) < 1e-20) throw new NotSupportedException("Degenerate source-plane pupil cell.");
            if (orientation == 0) orientation = Math.Sign(det);
            if (orientation != Math.Sign(det)) throw new NotSupportedException("Folded source-plane pupil requires a multi-branch field reconstruction.");
            var scale = Math.Sqrt((4d / (sampling * sampling)) / Math.Abs(det));
            var firstX = Math.Max(0, (int)Math.Ceiling(Math.Min(a.X, Math.Min(b.X, c.X)) / pitch + (size - 1) / 2d - 1e-9));
            var lastX = Math.Min(size - 1, (int)Math.Floor(Math.Max(a.X, Math.Max(b.X, c.X)) / pitch + (size - 1) / 2d + 1e-9));
            var firstY = Math.Max(0, (int)Math.Ceiling(Math.Min(a.Y, Math.Min(b.Y, c.Y)) / pitch + (size - 1) / 2d - 1e-9));
            var lastY = Math.Min(size - 1, (int)Math.Floor(Math.Max(a.Y, Math.Max(b.Y, c.Y)) / pitch + (size - 1) / 2d + 1e-9));
            if ((work += (long)(lastX - firstX + 1) * (lastY - firstY + 1) * states) > AnalysisResourceLimits.MaximumWavefrontInterpolationWork)
                throw new ArgumentOutOfRangeException(nameof(vertices), "Source-plane reconstruction exceeds its interpolation budget.");
            for (var r = firstY; r <= lastY; r++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                for (var col = firstX; col <= lastX; col++)
                {
                    var dx = (col - (size - 1) / 2d) * pitch - a.X; var dy = (r - (size - 1) / 2d) * pitch - a.Y;
                    var wb = (dx * (c.Y - a.Y) - dy * (c.X - a.X)) / det;
                    var wc = ((b.X - a.X) * dy - (b.Y - a.Y) * dx) / det; var wa = 1 - wb - wc;
                    if (wa < -1e-9 || wb < -1e-9 || wc < -1e-9) continue;
                    if (covered[r, col])
                    {
                        if (Math.Min(wa, Math.Min(wb, wc)) > 1e-8)
                            throw new NotSupportedException("Overlapping source-plane pupil branches cannot be merged silently.");
                        continue;
                    }
                    for (var state = 0; state < states; state++)
                    {
                        var va = a.Fields[state] * (scale / Math.Sqrt(vector ? index * a.Cosine : 1));
                        var vb = b.Fields[state] * (scale / Math.Sqrt(vector ? index * b.Cosine : 1));
                        var vc = c.Fields[state] * (scale / Math.Sqrt(vector ? index * c.Cosine : 1));
                        result[state][r, col] = va * wa + vb * wb + vc * wc;
                    }
                    covered[r, col] = true;
                }
            }
        }
    }
    internal static double SpatialKnifeTransmission(int row, int column, int size, double pitch,
        FoucaultKnifeEdge knife, double position)
    {
        var i = knife is FoucaultKnifeEdge.HorizontalAbove or FoucaultKnifeEdge.HorizontalBelow ? row : column;
        var lower = Math.Clamp(.5 + (position - (i - (size - 1) / 2d) * pitch) / pitch, 0, 1);
        return knife is FoucaultKnifeEdge.HorizontalAbove or FoucaultKnifeEdge.VerticalRight ? lower : 1 - lower;
    }
    private static void Mask(ComplexElectricField[,] grid, double pitch, FoucaultKnifeEdge knife, double position)
    {
        for (var r = 0; r < grid.GetLength(0); r++)
        for (var c = 0; c < grid.GetLength(1); c++) grid[r, c] *= SpatialKnifeTransmission(r, c, grid.GetLength(0), pitch, knife, position);
    }
    private static void Mask(Complex[,] grid, double pitch, FoucaultKnifeEdge knife, double position)
    {
        for (var r = 0; r < grid.GetLength(0); r++)
        for (var c = 0; c < grid.GetLength(1); c++) grid[r, c] *= SpatialKnifeTransmission(r, c, grid.GetLength(0), pitch, knife, position);
    }
}
