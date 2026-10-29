namespace OpticalSystemDesign.Core.Services;

public sealed record ThickDelanoElement(double FrontZMillimeters, double ThicknessMillimeters, double RefractiveIndex);
public sealed record ThickDelanoConstruction(double[] FrontCurvaturesPerMillimeter, double[] BackCurvaturesPerMillimeter,
    double[] FrontMarginalHeightsMillimeters, double[] BackMarginalHeightsMillimeters,
    double[] ReducedSegmentSlopes, double OutputSlope);

/// <summary>Inverse paraxial construction on the actual glass and air segments. The prescribed
/// marginal ray meets a fixed stop, image plane and output slope; this is an initializer, not physical acceptance.</summary>
public static class FixedStopDelano
{
    public static ThickDelanoConstruction Construct(IReadOnlyList<ThickDelanoElement> elements,
        double entranceSemiDiameterMillimeters, double stopZMillimeters, double stopSemiDiameterMillimeters,
        double imageZMillimeters, double focalLengthMillimeters, IReadOnlyList<double> slopeShape)
    {
        ArgumentNullException.ThrowIfNull(elements); ArgumentNullException.ThrowIfNull(slopeShape);
        var count = elements.Count; var dimension = 2 * count - 1;
        if (count is < 2 or > 16 || slopeShape.Count != dimension || slopeShape.Any(v => !double.IsFinite(v))
            || new[] { entranceSemiDiameterMillimeters, stopSemiDiameterMillimeters, focalLengthMillimeters }.Any(v => !double.IsFinite(v) || v <= 0)
            || !double.IsFinite(stopZMillimeters) || !double.IsFinite(imageZMillimeters)
            || elements.Any(e => e is null || !double.IsFinite(e.FrontZMillimeters) || !double.IsFinite(e.ThicknessMillimeters)
                || e.ThicknessMillimeters <= 0 || !double.IsFinite(e.RefractiveIndex) || e.RefractiveIndex <= 1))
            throw new ArgumentException("Finite forward glass segments, a positive aperture/focal length and a matching slope shape are required.");
        var outputSlope = -entranceSemiDiameterMillimeters / focalLengthMillimeters;
        var lengths = new double[dimension]; var stopRow = new double[dimension];
        for (var i = 0; i < count; i++)
        {
            var lens = elements[i]; var back = lens.FrontZMillimeters + lens.ThicknessMillimeters;
            if (!double.IsFinite(back) || stopZMillimeters >= lens.FrontZMillimeters && stopZMillimeters <= back)
                throw new ArgumentException("The independent stop must be in an air interval.");
            lengths[2 * i] = lens.ThicknessMillimeters / lens.RefractiveIndex;
            if (stopZMillimeters > back) stopRow[2 * i] = lengths[2 * i];
            if (i == count - 1) continue;
            var gap = elements[i + 1].FrontZMillimeters - back;
            if (!double.IsFinite(gap) || gap <= 0) throw new ArgumentException("Glass elements must be in forward nonintersecting order.");
            lengths[2 * i + 1] = gap;
            stopRow[2 * i + 1] = Math.Clamp(stopZMillimeters - back, 0, gap);
        }
        var last = elements[^1]; var lastBack = last.FrontZMillimeters + last.ThicknessMillimeters;
        if (imageZMillimeters <= lastBack || stopZMillimeters >= imageZMillimeters)
            throw new ArgumentException("The image must follow the last element and stop.");
        var slopes = slopeShape.Select(v => v * outputSlope).ToArray();
        var stopTarget = stopSemiDiameterMillimeters - entranceSemiDiameterMillimeters
            - (stopZMillimeters > lastBack ? (stopZMillimeters - lastBack) * outputSlope : 0);
        var endTarget = -(imageZMillimeters - lastBack) * outputSlope - entranceSemiDiameterMillimeters;
        var basis = new List<(double[] Normal, double Component)>();
        Project(stopRow, stopTarget); Project(lengths, endTarget);
        for (var i = 0; i < dimension; i++) slopes[i] += basis.Sum(row => row.Normal[i] * row.Component);
        var frontHeights = new double[count]; var backHeights = new double[count];
        var frontCurvatures = new double[count]; var backCurvatures = new double[count];
        var height = entranceSemiDiameterMillimeters; double incomingSlope = 0;
        for (var i = 0; i < count; i++)
        {
            frontHeights[i] = height;
            var internalSlope = slopes[2 * i]; var outgoingSlope = i == count - 1 ? outputSlope : slopes[2 * i + 1];
            var backHeight = height + lengths[2 * i] * internalSlope;
            if (Math.Abs(height) < 1e-12 || Math.Abs(backHeight) < 1e-12)
                throw new ArithmeticException("A marginal-ray zero at a powered surface makes the inverse construction singular.");
            var indexStep = elements[i].RefractiveIndex - 1;
            frontCurvatures[i] = (incomingSlope - internalSlope) / (indexStep * height);
            backCurvatures[i] = (outgoingSlope - internalSlope) / (indexStep * backHeight);
            backHeights[i] = backHeight;
            height = i == count - 1 ? backHeight : backHeight + lengths[2 * i + 1] * outgoingSlope;
            incomingSlope = outgoingSlope;
        }
        if (frontCurvatures.Concat(backCurvatures).Concat(slopes).Any(v => !double.IsFinite(v)))
            throw new ArithmeticException("The thick Delano construction is not representable.");
        return new(frontCurvatures, backCurvatures, frontHeights, backHeights, slopes, outputSlope);

        void Project(double[] row, double target)
        {
            var vector = row.ToArray(); var component = target - row.Zip(slopes, (a, b) => a * b).Sum();
            var originalNorm = vector.Aggregate(0.0, double.Hypot);
            foreach (var previous in basis)
            {
                var overlap = vector.Zip(previous.Normal, (a, b) => a * b).Sum();
                for (var i = 0; i < dimension; i++) vector[i] -= overlap * previous.Normal[i];
                component -= overlap * previous.Component;
            }
            var norm = vector.Aggregate(0.0, double.Hypot);
            if (norm <= 1e-12 * Math.Max(1, originalNorm))
            {
                if (Math.Abs(component) > 1e-9 * Math.Max(1, Math.Abs(target)))
                    throw new ArithmeticException("The fixed-stop/image requirements are inconsistent for these segments.");
                return;
            }
            basis.Add((vector.Select(v => v / norm).ToArray(), component / norm));
        }
    }
}
