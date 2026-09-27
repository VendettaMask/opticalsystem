using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

/// <summary>
/// Independent box coordinates for curvature and thickness. Each thickness consumes a
/// fraction of the remaining track slack; all decoded points satisfy the coupled track
/// limit. Finite differences act on this mapping, never on a coupled projection.
/// </summary>
internal sealed class FlatStartSolverCoordinates
{
    private readonly int _surfaceCount;
    private readonly double _focalLength;
    private readonly double[] _minimum;
    private readonly double _slack;

    public FlatStartSolverCoordinates(InitialStructureSpecification specification, int elementCount,
        double maximumScaledCurvature)
    {
        _surfaceCount = 2 * elementCount;
        _focalLength = specification.EffectiveFocalLengthMillimeters;
        var fixedBack = specification.FlatStart!.FixedBackFocusMillimeters;
        _minimum = Enumerable.Range(0, _surfaceCount - (fixedBack.HasValue ? 1 : 0)).Select(index =>
            index == _surfaceCount - 1 ? specification.MinimumBackFocusMillimeters
            : index % 2 == 0 ? specification.MinimumCenterThicknessMillimeters : specification.MinimumAirGapMillimeters).ToArray();
        _slack = Math.Max(0, specification.MaximumTrackLengthMillimeters - _minimum.Sum() - (fixedBack ?? 0));
        Lower = Enumerable.Repeat(-maximumScaledCurvature, _surfaceCount).Concat(Enumerable.Repeat(0.0, _minimum.Length)).ToArray();
        Upper = Enumerable.Repeat(maximumScaledCurvature, _surfaceCount)
            .Concat(Enumerable.Repeat(_slack == 0 ? 0.0 : 1.0, _minimum.Length)).ToArray();
    }

    public double[] Lower { get; }
    public double[] Upper { get; }

    public double[] Encode(IReadOnlyList<double> physical)
    {
        Validate(physical);
        var result = physical.ToArray();
        var remaining = _slack;
        for (var index = 0; index < _minimum.Length; index++)
        {
            var excess = Math.Max(0, physical[_surfaceCount + index] * _focalLength - _minimum[index]);
            result[_surfaceCount + index] = remaining <= 0 ? 0 : Math.Clamp(excess / remaining, 0, 1);
            remaining *= 1 - result[_surfaceCount + index];
        }
        for (var index = 0; index < _surfaceCount; index++)
            result[index] = Math.Clamp(result[index], Lower[index], Upper[index]);
        return result;
    }

    public double[] Decode(IReadOnlyList<double> coordinates)
    {
        Validate(coordinates);
        var result = coordinates.ToArray();
        var remaining = _slack;
        for (var index = 0; index < result.Length; index++)
            if (result[index] < Lower[index] || result[index] > Upper[index])
                throw new ArgumentOutOfRangeException(nameof(coordinates), "Solver coordinates are outside their independent bounds.");
        for (var index = 0; index < _minimum.Length; index++)
        {
            var excess = remaining * coordinates[_surfaceCount + index];
            result[_surfaceCount + index] = (_minimum[index] + excess) / _focalLength;
            remaining -= excess;
        }
        return result;
    }

    private void Validate(IReadOnlyList<double> vector)
    {
        if (vector.Count != Lower.Length || vector.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Invalid solver coordinates.", nameof(vector));
    }
}
