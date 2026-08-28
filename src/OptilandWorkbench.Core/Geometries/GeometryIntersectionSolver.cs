using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.Geometries;

internal static class GeometryIntersectionSolver
{
    private const int MaximumNewtonIterations = 48;
    private const int MaximumBracketIterations = 96;
    private const double MinimumDerivative = 1e-13;
    private const double TangentIncidenceCosine = 1e-4;

    public static IntersectionResult Solve(
        Vector3D origin,
        Vector3D direction,
        Func<double, double, double> sag,
        Func<Vector3D, Vector3D> normal)
    {
        ArgumentNullException.ThrowIfNull(sag);
        ArgumentNullException.ThrowIfNull(normal);
        if (!IsFinite(origin) || !IsFiniteNonZero(direction))
        {
            return IntersectionResult.Failure(IntersectionStatus.InvalidInput);
        }

        var initial = Math.Abs(direction.Z) <= 1e-14 ? 0 : Math.Max(0, -origin.Z / direction.Z);
        var iterations = 0;
        Evaluation? best = null;
        Bracket? bracket = null;
        var currentDistance = initial;
        Evaluation? previous = null;

        for (; iterations < MaximumNewtonIterations; iterations++)
        {
            if (!TryEvaluate(origin, direction, currentDistance, sag, out var current)) break;
            best = Better(best, current);
            if (IsConverged(current))
            {
                return CreateVerifiedResult(origin, direction, current.Distance, sag, normal,
                    iterations + 1, current.ConditionEstimate);
            }

            if (previous is { } prior && OppositeSigns(prior.Residual, current.Residual))
            {
                bracket = Bracket.Ordered(prior, current);
            }

            if (!double.IsFinite(current.Derivative) || Math.Abs(current.Derivative) <= MinimumDerivative) break;
            var proposed = current.Distance - (current.Residual / current.Derivative);
            if (bracket is { } bounds
                && (proposed <= bounds.Lower.Distance || proposed >= bounds.Upper.Distance))
            {
                proposed = Midpoint(bounds.Lower.Distance, bounds.Upper.Distance);
            }

            if (!double.IsFinite(proposed) || proposed < 0) proposed = current.Distance / 2;
            var accepted = false;
            for (var backtrack = 0; backtrack < 16; backtrack++)
            {
                if (TryEvaluate(origin, direction, proposed, sag, out var candidate)
                    && (Math.Abs(candidate.Residual) < Math.Abs(current.Residual)
                        || OppositeSigns(current.Residual, candidate.Residual)))
                {
                    previous = current;
                    currentDistance = proposed;
                    accepted = true;
                    break;
                }

                proposed = Midpoint(current.Distance, proposed);
            }

            if (!accepted) break;
        }

        var search = FindForwardBracket(origin, direction, initial, sag, best);
        best = Better(best, search.Best);
        bracket ??= search.Bracket;
        if (best is { } sampled && IsConverged(sampled))
        {
            return CreateVerifiedResult(origin, direction, sampled.Distance, sag, normal,
                iterations, sampled.ConditionEstimate);
        }

        if (bracket is { } rootBracket)
        {
            var lower = rootBracket.Lower;
            var upper = rootBracket.Upper;
            for (var bracketIteration = 0; bracketIteration < MaximumBracketIterations; bracketIteration++)
            {
                iterations++;
                var denominator = upper.Residual - lower.Residual;
                var candidateDistance = Math.Abs(denominator) > 1e-30
                    ? ((lower.Distance * upper.Residual) - (upper.Distance * lower.Residual)) / denominator
                    : double.NaN;
                if (!double.IsFinite(candidateDistance)
                    || candidateDistance <= lower.Distance
                    || candidateDistance >= upper.Distance)
                {
                    candidateDistance = Midpoint(lower.Distance, upper.Distance);
                }

                if (!TryEvaluate(origin, direction, candidateDistance, sag, out var candidate))
                {
                    candidateDistance = Midpoint(lower.Distance, upper.Distance);
                    if (!TryEvaluate(origin, direction, candidateDistance, sag, out candidate))
                    {
                        return best is { } domainCandidate
                            ? CandidateFailure(IntersectionStatus.DomainError, domainCandidate, iterations)
                            : IntersectionResult.Failure(IntersectionStatus.DomainError, iterations: iterations);
                    }
                }

                best = Better(best, candidate);
                if (IsConverged(candidate)
                    || Math.Abs(upper.Distance - lower.Distance) <= DistanceTolerance(candidate.Distance))
                {
                    return CreateVerifiedResult(origin, direction, candidate.Distance, sag, normal,
                        iterations, candidate.ConditionEstimate);
                }

                if (OppositeSigns(lower.Residual, candidate.Residual)) upper = candidate;
                else lower = candidate;
            }
        }

        if (best is null)
        {
            return IntersectionResult.Failure(
                search.SawDomainError ? IntersectionStatus.DomainError : IntersectionStatus.NoRoot,
                iterations: iterations);
        }

        return CandidateFailure(
            bracket is null ? IntersectionStatus.NoRoot : IntersectionStatus.MaxIterations,
            best.Value,
            iterations);
    }

    public static IntersectionResult CreateVerifiedResult(
        Vector3D origin,
        Vector3D direction,
        double distance,
        Func<double, double, double> sag,
        Func<Vector3D, Vector3D> normal,
        int iterations,
        double? conditionEstimate = null)
    {
        if (!double.IsFinite(distance) || distance < 0)
        {
            return IntersectionResult.Failure(IntersectionStatus.NoRoot, iterations: iterations);
        }

        var point = origin + (direction * distance);
        var surfaceSag = sag(point.X, point.Y);
        if (!double.IsFinite(surfaceSag))
        {
            return new IntersectionResult(
                IntersectionStatus.DomainError, distance, point,
                new Vector3D(double.NaN, double.NaN, double.NaN),
                double.NaN, iterations, conditionEstimate ?? double.PositiveInfinity);
        }

        var residual = point.Z - surfaceSag;
        if (!double.IsFinite(residual) || Math.Abs(residual) > ResidualTolerance(point, surfaceSag))
        {
            return new IntersectionResult(
                IntersectionStatus.MaxIterations, distance, point,
                new Vector3D(double.NaN, double.NaN, double.NaN),
                residual, iterations, conditionEstimate ?? double.PositiveInfinity);
        }

        var surfaceNormal = normal(point);
        if (!IsFiniteNonZero(surfaceNormal))
        {
            return new IntersectionResult(
                IntersectionStatus.InvalidNormal, distance, point, surfaceNormal,
                residual, iterations, conditionEstimate ?? double.PositiveInfinity);
        }

        surfaceNormal /= surfaceNormal.Length;
        var rayDirection = direction / direction.Length;
        var incidence = Math.Abs(Dot(rayDirection, surfaceNormal));
        var condition = conditionEstimate ?? (incidence <= 1e-15 ? double.PositiveInfinity : 1 / incidence);
        var status = incidence <= TangentIncidenceCosine
            ? IntersectionStatus.Tangent
            : IntersectionStatus.Success;
        return new IntersectionResult(status, distance, point, surfaceNormal, residual, iterations, condition);
    }

    public static bool IsFinite(Vector3D value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    public static bool IsFiniteNonZero(Vector3D value) => IsFinite(value) && value.Length > 1e-15;

    private static SearchResult FindForwardBracket(
        Vector3D origin,
        Vector3D direction,
        double initial,
        Func<double, double, double> sag,
        Evaluation? existingBest)
    {
        var best = existingBest;
        var upper = Math.Max(1, initial + Math.Max(1, Math.Abs(initial) * 0.25));
        var sawDomainError = false;
        Evaluation? previous = null;
        for (var expansion = 0; expansion < 16 && upper <= 1e12; expansion++)
        {
            const int subdivisions = 64;
            for (var index = 0; index <= subdivisions; index++)
            {
                var distance = upper * index / subdivisions;
                if (!TryEvaluate(origin, direction, distance, sag, out var sample))
                {
                    sawDomainError = true;
                    previous = null;
                    continue;
                }

                best = Better(best, sample);
                if (IsConverged(sample)) return new SearchResult(null, best, sawDomainError);
                if (previous is { } prior && OppositeSigns(prior.Residual, sample.Residual))
                {
                    return new SearchResult(Bracket.Ordered(prior, sample), best, sawDomainError);
                }

                previous = sample;
            }

            upper *= 2;
        }

        return new SearchResult(null, best, sawDomainError);
    }

    private static bool TryEvaluate(
        Vector3D origin,
        Vector3D direction,
        double distance,
        Func<double, double, double> sag,
        out Evaluation evaluation)
    {
        evaluation = default;
        if (!double.IsFinite(distance) || distance < 0) return false;
        var point = origin + (direction * distance);
        var surfaceSag = sag(point.X, point.Y);
        if (!double.IsFinite(surfaceSag)
            || !TrySagGradient(sag, point.X, point.Y, surfaceSag, out var dzdx, out var dzdy))
        {
            return false;
        }

        var residual = point.Z - surfaceSag;
        var derivative = direction.Z - (dzdx * direction.X) - (dzdy * direction.Y);
        var condition = Math.Abs(derivative) <= MinimumDerivative
            ? double.PositiveInfinity
            : direction.Length * Math.Sqrt(1 + (dzdx * dzdx) + (dzdy * dzdy)) / Math.Abs(derivative);
        evaluation = new Evaluation(distance, point, surfaceSag, residual, derivative, condition);
        return double.IsFinite(residual);
    }

    private static bool TrySagGradient(
        Func<double, double, double> sag,
        double x,
        double y,
        double center,
        out double dzdx,
        out double dzdy)
    {
        var hx = Math.Max(1e-8, 2e-6 * Math.Max(1, Math.Abs(x)));
        var hy = Math.Max(1e-8, 2e-6 * Math.Max(1, Math.Abs(y)));
        dzdx = Derivative(sag(x - hx, y), center, sag(x + hx, y), hx);
        dzdy = Derivative(sag(x, y - hy), center, sag(x, y + hy), hy);
        return double.IsFinite(dzdx) && double.IsFinite(dzdy);
    }

    private static double Derivative(double below, double center, double above, double step)
    {
        if (double.IsFinite(below) && double.IsFinite(above)) return (above - below) / (2 * step);
        if (double.IsFinite(above)) return (above - center) / step;
        if (double.IsFinite(below)) return (center - below) / step;
        return double.NaN;
    }

    private static bool IsConverged(Evaluation value) =>
        Math.Abs(value.Residual) <= ResidualTolerance(value.Point, value.SurfaceSag);

    private static double ResidualTolerance(Vector3D point, double surfaceSag) =>
        1e-10 * Math.Max(1, Math.Max(point.Length, Math.Abs(surfaceSag)));

    private static double DistanceTolerance(double distance) => 1e-12 * Math.Max(1, Math.Abs(distance));

    private static Evaluation? Better(Evaluation? current, Evaluation? candidate)
    {
        if (candidate is null) return current;
        return current is null || Math.Abs(candidate.Value.Residual) < Math.Abs(current.Value.Residual)
            ? candidate
            : current;
    }

    private static bool OppositeSigns(double left, double right) =>
        (left < 0 && right > 0) || (left > 0 && right < 0);

    private static IntersectionResult CandidateFailure(
        IntersectionStatus status,
        Evaluation candidate,
        int iterations) => new(
            status,
            candidate.Distance,
            candidate.Point,
            new Vector3D(double.NaN, double.NaN, double.NaN),
            candidate.Residual,
            iterations,
            candidate.ConditionEstimate);

    private static double Midpoint(double left, double right) => left + ((right - left) / 2);

    private static double Dot(Vector3D left, Vector3D right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private readonly record struct Evaluation(
        double Distance,
        Vector3D Point,
        double SurfaceSag,
        double Residual,
        double Derivative,
        double ConditionEstimate);

    private readonly record struct Bracket(Evaluation Lower, Evaluation Upper)
    {
        public static Bracket Ordered(Evaluation first, Evaluation second) =>
            first.Distance <= second.Distance ? new Bracket(first, second) : new Bracket(second, first);
    }

    private readonly record struct SearchResult(Bracket? Bracket, Evaluation? Best, bool SawDomainError);
}
