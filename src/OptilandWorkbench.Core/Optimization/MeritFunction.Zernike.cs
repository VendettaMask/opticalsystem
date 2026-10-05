using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private readonly record struct ZernikeRequest(int Wave, int Field, int Sampling, ZernikeBasisKind Kind, double Epsilon, int Vertex)
    {
        public ZernikeFitResult Evaluate(Optic optic, int terms) => ZernikeMetrics.Evaluate(optic, Wave, Field, Sampling, Kind, terms, Epsilon, Vertex);
    }

    private static (int Term, ZernikeRequest Request) ZernikeParameters(MeritOperandDefinition definition)
    {
        var term = ZemaxIntegerParameter(definition, 0, 1);
        var kind = (ZernikeBasisKind)IntegerDataParameter(definition, 2, 0);
        if (!Enum.IsDefined(kind) || term < -8 || term > (kind == ZernikeBasisKind.Fringe ? 37 : 231))
            throw new ArgumentOutOfRangeException(nameof(definition), "ZERN Term 为 -8..37（Fringe）或 -8..231（Standard/Annular）。");
        return (term, new(ZemaxIntegerParameter(definition, 1, definition.Wavelength),
            IntegerDataParameter(definition, 1, definition.Field), IntegerDataParameter(definition, 0, 1), kind,
            FiniteParameter(definition, 3, 0), IntegerDataParameter(definition, 4, 0)));
    }

    private static double EvaluateZernikeStandalone(Optic optic, MeritOperandDefinition definition)
    {
        var (term, request) = ZernikeParameters(definition);
        return request.Evaluate(optic, Math.Max(11, term)).Value(term);
    }

    private sealed partial class OrderedMeritEvaluationContext
    {
        private (Optic Optic, ZernikeRequest Request, int Terms, int End)? _zernikeKey;
        private ZernikeFitResult? _zernikeFit;

        public double EvaluateZernike(Optic optic, MeritOperandDefinition definition)
        {
            var (term, request) = ZernikeParameters(definition);
            var maxTerm = Math.Max(11, term);
            var end = CurrentRowIndex;
            foreach (var direction in new[] { -1, 1 })
                for (var i = CurrentRowIndex + direction; i >= 0 && i < _definitions.Count; i += direction)
                {
                    var row = _definitions[i];
                    if (!row.Enabled || row.CompatibilityOnly || CanonicalType(row.Type) != "ZERN") break;
                    (int Term, ZernikeRequest Request) adjacent;
                    try { adjacent = ZernikeParameters(row); }
                    catch (ArgumentException) { break; }
                    if (adjacent.Request != request) break;
                    maxTerm = Math.Max(maxTerm, adjacent.Term);
                    end = Math.Max(end, i);
                }
            var key = (optic, request, maxTerm, end);
            if (_zernikeKey != key || _zernikeFit is null)
            {
                var computed = request.Evaluate(optic, maxTerm);
                _zernikeFit = computed; _zernikeKey = key;
            }
            return _zernikeFit.Value(term);
        }
    }
}
