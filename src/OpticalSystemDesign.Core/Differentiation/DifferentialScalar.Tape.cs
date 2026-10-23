namespace OpticalSystemDesign.Core.Differentiation;

public readonly partial struct DifferentialScalar
{
    internal DifferentialScalar(double value, DifferentialTape tape, int node, double originX, double originY, double tangentBound)
        => (Value, _gradient, _tape, _node, _originX, _originY, _tangentBound) = (value, null, tape, node, originX, originY, tangentBound);

    internal int TapeNode(DifferentialTape tape)
    {
        if (_tape is null) return tape.Import(_gradient);
        if (!ReferenceEquals(_tape, tape)) throw new InvalidOperationException("Independent ray tapes cannot be combined.");
        return _node;
    }

    private double TapeDerivative(int index) => index == _tape!.ParameterCount ? _originX
        : index == _tape.ParameterCount + 1 ? _originY
        : throw new InvalidOperationException("Parameter tangents are pending GPU evaluation.");

    private static DifferentialScalar TapeCombine(double value, DifferentialScalar a, double da, DifferentialScalar b, double db)
    {
        var tape = a._tape ?? b._tape!;
        var bound = Math.Abs(da) * Bound(a) + Math.Abs(db) * Bound(b);
        // A finite bound guarantees that an inactive penalty cannot hide a non-finite
        // parameter tangent. Unrepresentable cases retain the original CPU contract.
        if (!double.IsFinite(bound) || !double.IsFinite(da) || !double.IsFinite(db))
            throw new GpuTapeRangeException("The tangent graph needs CPU extended-range evaluation.");
        var node = tape.Combine(a.TapeNode(tape), da, b.TapeNode(tape), db);
        return new(value, tape, node,
            da * Origin(a, 0) + db * Origin(b, 0), da * Origin(a, 1) + db * Origin(b, 1), bound);

        double Origin(DifferentialScalar scalar, int axis) => scalar._tape is not null
            ? axis == 0 ? scalar._originX : scalar._originY
            : scalar._gradient is null ? 0 : scalar._gradient[tape.ParameterCount + axis];
        double Bound(DifferentialScalar scalar) => scalar._tape is not null ? scalar._tangentBound
            : scalar._gradient is null ? 0 : scalar._gradient.Take(tape.ParameterCount).Select(Math.Abs).DefaultIfEmpty().Max();
    }
}
