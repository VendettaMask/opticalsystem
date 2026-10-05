using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Optimization;

public static partial class MeritFunctionCatalog
{
    private static double EvaluateGeometricEnergy(Optic optic, MeritOperandDefinition definition)
    {
        var sampling = ZemaxIntegerParameter(definition, 0, 1);
        var wave = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        var field = IntegerDataParameter(definition, 0, definition.Field);
        var type = IntegerDataParameter(definition, 1, 1);
        if (CanonicalType(definition.Type) == "ERFP")
            return GeometricEnergyMetrics.EdgePositionMillimeters(optic, sampling, wave, field, type,
                ZemaxDataParameter(definition, 2, .5), ZemaxDataParameter(definition, 3, 0));
        var reference = IntegerDataParameter(definition, 2, 0);
        var noDiffraction = IntegerDataParameter(definition, 4, 0);
        var distribution = GeometricEnergyMetrics.Create(optic, sampling, wave, field,
            (EnergyRegion)type, (GeometricEnergyReference)reference, noDiffraction == 0);
        var value = ZemaxDataParameter(definition, 3, CanonicalType(definition.Type) == "GENC" ? .8 : 1);
        return CanonicalType(definition.Type) == "GENC" ? distribution.DistanceAtFraction(value) : distribution.FractionAtDistance(value);
    }
}
