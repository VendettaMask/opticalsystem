using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateDiffractionEnergy(Optic optic, MeritOperandDefinition definition)
    {
        var reference = IntegerDataParameter(definition, 2, 0);
        var distribution = DiffractionEnergyMetrics.Create(optic,
            ZemaxIntegerParameter(definition, 0, 1), ZemaxIntegerParameter(definition, 1, definition.Wavelength),
            IntegerDataParameter(definition, 0, definition.Field), (EnergyRegion)IntegerDataParameter(definition, 1, 1), reference,
            reference >= 3 ? IntegerDataParameter(definition, 4, 1) : 1,
            reference >= 3 ? ZemaxDataParameter(definition, 5, 0) : 0);
        var value = ZemaxDataParameter(definition, 3, CanonicalType(definition.Type) == "DENC" ? .8 : 1);
        return CanonicalType(definition.Type) == "DENC" ? distribution.DistanceAtFraction(value) : distribution.FractionAtDistance(value);
    }
}
