using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Core.Propagation;

/// <summary>Finite-probe Gaussian convention for Grid Gradient focal power.</summary>
internal static class GridGradientFocalTransport
{
    // The forward GGD Gaussian basis uses two meridional probes, with heights
    // ±0.002 lens units and the same dy/dz of +0.002. Both rays can steer in 3D.
    private const double Height = 0.002;
    private const double Slope = 0.002;

    internal static bool Supports(GridGradientIndexProfile grid) =>
        grid.Origin.Y <= -Height && grid.Origin.Y + (grid.Ny - 1) * grid.Spacing.Y >= Height;

    internal static GradientIndexParaxialMatrix Between(GradientIndexMaterial material, GridGradientIndexProfile grid,
        double start, double end, double wavelength, CancellationToken token)
    {
        if (!double.IsFinite(end - start) || end - start > material.MaximumPathLength)
            throw new InvalidOperationException("GGD 一阶传播超过材料的有限路径预算。");
        token.ThrowIfCancellationRequested();
        grid.ValidateAxialInterval(start, end, wavelength);
        var options = material.IntegrationOptions;
        var positionTolerance = Math.Min(options.PositionTolerance, 1e-10);
        var slopeTolerance = Math.Min(options.DirectionTolerance, 5e-13);
        var basis = new ProbePair(Launch(Height), Launch(-Height));
        var step = options.MaximumStep;
        var z = start;
        for (var attempt = 0; z < end; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (attempt >= options.MaximumAttempts)
                throw new InvalidOperationException("GGD 一阶传播超过最大积分尝试次数。");
            step = Math.Min(step, end - z);
            if (step <= 0 || z + step == z)
                throw new InvalidOperationException("GGD 一阶积分无法推进到出口面。");

            ProbePair fine;
            double error;
            try
            {
                var coarse = Step(basis, z, step);
                var middle = Step(basis, z, step / 2);
                fine = Step(middle, z + step / 2, step / 2);
                error = Error(fine, coarse) / 15; // RK4 step-doubling error of the fine pair.
            }
            catch (SpatialIndexDomainException)
            {
                step = Reduce(step, .25);
                continue;
            }
            if (!double.IsFinite(error) || error > 1)
            {
                step = Reduce(step, double.IsFinite(error) ? Math.Clamp(.9 * Math.Pow(error, -.2), .1, .5) : .1);
                continue;
            }
            basis = fine;
            z += step;
            step = Math.Min(options.MaximumStep, step * Math.Clamp(error == 0 ? 4 : .9 * Math.Pow(error, -.2), .2, 4));
        }

        var result = basis.Matrix;
        return Math.Abs(result.C) <= slopeTolerance ? result with { C = 0 } : result;

        Probe Launch(double y)
        {
            var index = grid.InterfaceRefractiveIndex(new(0, y, start), wavelength);
            return new(new(0, y, 0), new Vector3D(0, Slope, 1) * (index / Math.Sqrt(1 + Slope * Slope)));
        }

        ProbePair Step(ProbePair value, double axial, double h)
        {
            var k1 = Derivative(value, axial);
            var k2 = Derivative(value + k1 * (h / 2), axial + h / 2);
            var k3 = Derivative(value + k2 * (h / 2), axial + h / 2);
            var k4 = Derivative(value + k3 * h, axial + h);
            return value + (k1 + k2 * 2 + k3 * 2 + k4) * (h / 6);
        }

        ProbePair Derivative(ProbePair pair, double axial) =>
            new(Trace(pair.Positive, axial), Trace(pair.Negative, axial));

        Probe Trace(Probe probe, double axial)
        {
            if (!(probe.Momentum.Z > 0))
                throw new SpatialIndexDomainException("GGD 一阶探针失去正向轴向动量。");
            var sample = grid.PropagationSample(probe.Position with { Z = axial }, wavelength);
            var inverseAxialMomentum = 1 / probe.Momentum.Z;
            return new(new(probe.Momentum.X * inverseAxialMomentum, probe.SlopeY, 0),
                sample.Gradient * (sample.Index * inverseAxialMomentum));
        }

        double Error(ProbePair fine, ProbePair coarse)
        {
            var f = fine.Matrix;
            var c = coarse.Matrix;
            // Test the recovered columns as well as the probe positions. An
            // acceptable error in a single slope can swamp their small difference.
            var columns = Math.Max(Math.Max(Math.Abs(f.A - c.A), Math.Abs(f.B - c.B)) / positionTolerance,
                Math.Max(Math.Abs(f.C - c.C), Math.Abs(f.D - c.D)) / slopeTolerance);
            return Math.Max(columns, Math.Max(OrthogonalError(fine.Positive, coarse.Positive),
                OrthogonalError(fine.Negative, coarse.Negative)));
        }

        double OrthogonalError(Probe fine, Probe coarse) => Math.Max(
            Math.Abs(fine.Position.X - coarse.Position.X) / positionTolerance,
            Math.Abs(fine.SlopeX - coarse.SlopeX) / slopeTolerance);

        double Reduce(double value, double factor)
        {
            var reduced = value * factor;
            if (reduced < options.MinimumStep)
                throw new InvalidOperationException("GGD 一阶探针无法在最小步长内满足误差及网格范围约束。");
            return reduced;
        }
    }

    private readonly record struct Probe(Vector3D Position, Vector3D Momentum)
    {
        internal double SlopeX => Momentum.X / Momentum.Z;
        internal double SlopeY => Momentum.Y / Momentum.Z;
        public static Probe operator +(Probe a, Probe b) => new(a.Position + b.Position, a.Momentum + b.Momentum);
        public static Probe operator *(Probe a, double f) => new(a.Position * f, a.Momentum * f);
    }

    private readonly record struct ProbePair(Probe Positive, Probe Negative)
    {
        internal GradientIndexParaxialMatrix Matrix => new(
            (Positive.Position.Y - Negative.Position.Y) / (2 * Height),
            (Positive.Position.Y + Negative.Position.Y) / (2 * Slope),
            (Positive.SlopeY - Negative.SlopeY) / (2 * Height),
            (Positive.SlopeY + Negative.SlopeY) / (2 * Slope));

        public static ProbePair operator +(ProbePair a, ProbePair b) => new(a.Positive + b.Positive, a.Negative + b.Negative);
        public static ProbePair operator *(ProbePair a, double f) => new(a.Positive * f, a.Negative * f);
    }
}
