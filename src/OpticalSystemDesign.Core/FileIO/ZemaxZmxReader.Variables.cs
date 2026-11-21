using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.FileIO;

internal static partial class ZemaxZmxReader
{
    private static bool SupportsImportedBaseVariable(OpticalSurface surface, MultiConfigurationOperandKind kind)
    {
        if (kind is not (MultiConfigurationOperandKind.Curvature or MultiConfigurationOperandKind.Conic)) return true;
        if (surface.InteractionModel is CoordinateReferenceInteractionModel) return false;
        return surface.Geometry is StandardGeometry or PlaneGeometry or IGratingGeometry or EvenAsphereGeometry
            or OddAsphereGeometry or FringeSagGeometry or GridSagGeometry or NormalizedPolynomialGeometry
            or ConcentricZoneGeometry or IrregularGeometry or ForbesQGeometry or ForbesNormalSlopeGeometry
            or BiconicGeometry or SeparableBiconicGeometry or ToroidalGeometry
            || kind == MultiConfigurationOperandKind.Curvature && surface.Geometry is ChebyshevSagGeometry;
    }

    private static bool UsesExtraParameterColumns(string type) => IsExtendedPolynomial(type)
        || type is "FZERNSAG" or "GRID_SAG" or "CHEBYSHV" or "BINARY_2" or "BINARY_3" or "BIRE__IN" or "QED_TYPE";

    private static (MultiConfigurationOperandKind Kind, string Parameter)? VariableParameterBinding(
        string type, int number, bool extraData)
    {
        if (!extraData && number >= 13 && UsesExtraParameterColumns(type)) { number -= 12; extraData = true; }
        if (!extraData && type == "GRINSUR1" && number is >= 2 and <= 4)
            return (MultiConfigurationOperandKind.GradientIndexCoefficient, number == 2 ? "n0" : number == 3 ? "nr2" : "nr1");
        var parameter = extraData ? type switch
        {
            "XASPHERE" or "XOSPHERE" or "XPOLYNOM" when number >= 3 => $"c{number - 3}",
            "FZERNSAG" when number >= 3 => $"z{number - 3}",
            "QED_TYPE" when number >= 4 => $"q{number - 4}",
            "CHEBYSHV" when number >= 1 => $"c{number - 1}",
            "BINARY_3" when number >= 2 && (number - 2) % 4 == 0 => $"i{(number - 2) / 4}",
            "BINARY_3" when number >= 2 && (number - 2) % 4 == 2 => $"o{(number - 2) / 4}",
            "BINARY_3" when number >= 3 && (number - 3) % 4 == 0 => $"p{(number - 3) / 4}",
            "BINARY_3" when number >= 3 && (number - 3) % 4 == 2 => $"s{(number - 3) / 4}",
            _ => null
        } : type switch
        {
            "EVENASPH" or "ODDASPHE" or "BINARY_2" when number is >= 1 and <= 8 => $"c{number - 1}",
            "FZERNSAG" or "GRID_SAG" when number is >= 1 and <= 8 => $"a{number - 1}",
            "TOROIDAL" when number is >= 2 and <= 8 => $"a{number - 2}",
            "BINARY_3" when number is >= 1 and <= 3 => number == 1 ? "r0" : number == 2 ? "r1" : "b0",
            "TILTSURF" when number is 1 or 2 => $"t{number - 1}",
            "COORDBRK" when number is >= 1 and <= 5 => $"f{number - 1}",
            _ => null
        };
        return parameter is null ? null : (MultiConfigurationOperandKind.ShapeCoefficient, parameter);
    }

    private static void ImportVariableWarning(Optic optic, ZemaxSurface source, string field, int number, string reason)
    {
        var warning = $"ZMX SURF {source.Number} TYPE {source.Type} {field} {number}: {reason}";
        if (!optic.ImportVariableWarnings.Contains(warning, StringComparer.Ordinal)) optic.ImportVariableWarnings = [.. optic.ImportVariableWarnings, warning];
    }

    private static IEnumerable<(string Field, int Number)> VariableDeclarations(ZemaxSurface surface)
    {
        if (surface.RadiusVariable) yield return ("CURV", 0);
        if (surface.ThicknessVariable) yield return ("VDSZ", 0);
        if (surface.ConicVariable) yield return ("VCON", 0);
        foreach (var number in surface.VariableParameters) yield return ("VPAR", number);
        foreach (var number in surface.VariableExtraData) yield return ("XDAT", number);
        foreach (var number in surface.GlassVariableFields) yield return ("GLAS", number);
    }

    private static void BlockTraceVariables(Optic optic)
    {
        foreach (var surface in optic.SurfaceGroup.Items)
            surface.BlockedVariableProperties = new[] { "radius", "thickness", "conic", "semiDiameter" }
                .Concat(SurfaceShapeParameters.CoefficientNames(surface).Select(SurfaceShapeParameters.Property))
                .Concat(surface.MaterialAfter is GradientIndexMaterial gradient
                    ? GradientIndexProfileData.Keys(GradientIndexProfileData.Kind(gradient.Profile)).Select(GradientIndexParameters.Property) : []).ToArray();
    }

    private static void InstallImportedVariables(Optic optic, ZemaxSurface source, OpticalSurface surface, bool referencePlane)
    {
        var shapes = new List<string>();
        if (source.ConicVariable && SupportsImportedBaseVariable(surface, MultiConfigurationOperandKind.Conic)) shapes.Add("conic");
        else if (source.ConicVariable && !referencePlane)
            ImportVariableWarning(optic, source, "VCON", 0, "conic is unavailable on the imported geometry; value retained as fixed");
        if (source.RadiusVariable && !referencePlane && !SupportsImportedBaseVariable(surface, MultiConfigurationOperandKind.Curvature))
        {
            surface.RadiusVariable = false;
            ImportVariableWarning(optic, source, "CURV", 0, "curvature is unavailable on the imported geometry; value retained as fixed");
        }
        foreach (var field in source.GlassVariableFields)
            if (surface.MaterialAfter is not ModelGlassMaterial || field is not (7 or 8 or 9))
                ImportVariableWarning(optic, source, "GLAS", field, $"glass selection variable has no supported mapping; source record: {source.GlassRecord}");
        if (referencePlane && source.ConicVariable) ImportVariableWarning(optic, source, "VCON", 0, "coordinate-reference conic is not an optimization parameter");
        if (referencePlane && source.RadiusVariable) ImportVariableWarning(optic, source, "CURV", 0, "coordinate-reference curvature is not an optimization parameter");
        var gradient = surface.MaterialAfter as GradientIndexMaterial;
        var gradients = new Dictionary<string, GradientIndexVariableRange>(StringComparer.Ordinal);
        foreach (var (number, extra) in source.VariableParameters.Order().Select(number => (number, false))
                     .Concat(source.VariableExtraData.Order().Select(number => (number, true))))
        {
            var field = extra ? "XDAT" : "VPAR";
            if (!extra && source.Type == "COORDBRK" && number is >= 1 and <= 5 && source.SupportsCoordinateVariables) continue;
            var binding = VariableParameterBinding(source.Type, number, extra);
            if (!extra && source.ParameterPickups.ContainsKey(number - 1))
            { ImportVariableWarning(optic, source, field, number, "variable marker conflicts with a parameter pickup; pickup retained"); continue; }
            if (binding is not { } mapped)
            { ImportVariableWarning(optic, source, field, number, $"no supported optimization variable mapping; source value {(extra ? source.ExtraData.GetValueOrDefault(number) : source.Parameter(number - 1)):R} retained as fixed"); continue; }
            if (mapped.Kind == MultiConfigurationOperandKind.ShapeCoefficient && SurfaceShapeParameters.CoefficientNames(surface).Contains(mapped.Parameter))
                shapes.Add(mapped.Parameter);
            else if (mapped.Kind == MultiConfigurationOperandKind.GradientIndexCoefficient && gradient is not null)
            {
                gradients[mapped.Parameter] = GradientIndexParameters.DefaultVariableRange(gradient, mapped.Parameter);
                if (GradientIndexParameters.ScalarParaxialVariableDisabledReason(GradientIndexProfileData.Kind(gradient.Profile), mapped.Parameter) is { } reason)
                    ImportVariableWarning(optic, source, field, number, reason);
            }
            else ImportVariableWarning(optic, source, field, number, $"coefficient is unavailable on the imported model; source value {(extra ? source.ExtraData.GetValueOrDefault(number) : source.Parameter(number - 1)):R} retained as fixed");
        }
        surface.ShapeVariables = shapes.Distinct(StringComparer.Ordinal).ToArray();
        if (gradient is not null && gradients.Count > 0)
            surface.MaterialAfter = new GradientIndexMaterial(gradient.Name, gradient.Profile,
                gradient.MaximumPathLength, gradient.IntegrationOptions, gradients);
    }

    private static void ApplyConfigurationVariableMasks(ZemaxDocument document, ZemaxSurface surface, int configurationIndex)
    {
        foreach (var operand in document.ConfigurationOperands.Where(operand => operand.Target == surface.Number
                     && operand.ConfigurationIndex == configurationIndex && operand.Command is "PRAM" or "EDVA"))
        {
            var number = operand.AuxiliaryIndex + 1;
            var extra = operand.Command == "EDVA";
            if (number <= 0) continue;
            if (!extra && number >= 13 && UsesExtraParameterColumns(surface.Type)) { number -= 12; extra = true; }
            var variables = extra ? surface.VariableExtraData : surface.VariableParameters;
            variables.Remove(number);
            if (!extra && number < 13 && UsesExtraParameterColumns(surface.Type)) surface.VariableParameters.Remove(operand.AuxiliaryIndex + 1);
            if (extra) surface.VariableParameters.Remove(number + 12);
            if (operand.SolveCode == 1) variables.Add(number);
        }
    }

    private static List<MultiConfigurationPickup> ImportParameterPickups(ZemaxDocument document,
        IReadOnlyList<int>[] sourceNumbers, IReadOnlyList<Optic> configurations,
        List<MultiConfigurationOperand> rows, HashSet<MultiConfigurationLinkOverride> links)
    {
        var result = new List<MultiConfigurationPickup>();
        MultiConfigurationOperand? ParameterRow(ZemaxSurface source, int number, int configuration)
        {
            var binding = VariableParameterBinding(source.Type, number, false);
            var surface = sourceNumbers[configuration].ToList().IndexOf(source.Number);
            return binding is { } mapped && surface > 0 ? new(mapped.Kind, surface, mapped.Parameter) : null;
        }
        double Unit(ZemaxSurface source, int number) => number >= 13 && UsesExtraParameterColumns(source.Type)
            ? ScaleSurfaceExtraData(document, source.Type, number - 12, 1)
            : ScaleSurfaceParameter(document, source.Type, number - 1, 1, source.TypeArguments);
        foreach (var target in document.Surfaces)
            foreach (var (parameter, pickup) in target.ParameterPickups)
                for (var configuration = 0; configuration < configurations.Count; configuration++)
                {
                    if (document.ConfigurationOperands.Any(operand => operand.Target == target.Number && operand.ConfigurationIndex == configuration
                        && (operand.Command == "PRAM" && operand.AuxiliaryIndex == parameter
                            || parameter >= 12 && operand.Command == "EDVA" && operand.AuxiliaryIndex == parameter - 12))) continue;
                    var source = document.Surfaces.FirstOrDefault(surface => surface.Number == pickup.SourceSurface);
                    if (source is null) continue;
                    var sourceIndex = sourceNumbers[configuration].ToList().IndexOf(source.Number);
                    var sourceNumber = pickup.SourceColumn == 0 ? parameter + 1 : pickup.SourceColumn - 5;
                    var sourceRow = pickup.SourceColumn switch
                    {
                        0 or >= 5 and <= 17 => ParameterRow(source, sourceNumber, configuration),
                        1 => new(MultiConfigurationOperandKind.Curvature, sourceIndex),
                        2 => new(MultiConfigurationOperandKind.Thickness, sourceIndex),
                        3 => new(MultiConfigurationOperandKind.Conic, sourceIndex),
                        4 => new(MultiConfigurationOperandKind.SemiDiameter, sourceIndex),
                        _ => null
                    };
                    var targetRow = ParameterRow(target, parameter + 1, configuration);
                    try
                    {
                        if (sourceRow is null || targetRow is null || pickup.SourceColumn == 1)
                            throw new NotSupportedException("parameter pickup has no supported live coefficient mapping");
                        _ = sourceRow.Read(configurations[configuration]); _ = targetRow.Read(configurations[configuration]);
                        var sourceSurface = configurations[configuration].SurfaceGroup.Items[sourceIndex];
                        if (!SupportsImportedBaseVariable(sourceSurface, sourceRow.Kind))
                            throw new NotSupportedException("parameter pickup source is unavailable on the imported geometry");
                        if (sourceRow.Kind == MultiConfigurationOperandKind.SemiDiameter && (!sourceSurface.SemiDiameterFixed || sourceSurface.SemiDiameterMaximum))
                            throw new NotSupportedException("parameter pickup depends on an automatic ray-envelope semi-diameter");
                        var sourceUnit = pickup.SourceColumn switch { 2 or 4 => document.LengthScaleToMillimeters, 3 => 1, _ => Unit(source, sourceNumber) };
                        var targetUnit = Unit(target, parameter + 1);
                        var scale = pickup.Scale * targetUnit / sourceUnit; var offset = pickup.Offset * targetUnit;
                        if (!double.IsFinite(scale) || !double.IsFinite(offset)) throw new NotSupportedException("parameter pickup unit conversion is not finite");
                        if (!rows.Contains(sourceRow)) rows.Add(sourceRow); if (!rows.Contains(targetRow)) rows.Add(targetRow);
                        result.Add(new(configuration, targetRow, configuration, sourceRow, scale, offset));
                        if (configuration > 0) links.Add(new(configuration, targetRow.SurfaceNumber, targetRow.Property));
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
                    {
                        ImportVariableWarning(configurations[configuration], target, "PPAR", parameter + 1, exception.Message);
                        if (sourceRow is not null && sourceIndex >= 0)
                        {
                            var sourceSurface = configurations[configuration].SurfaceGroup.Items[sourceIndex];
                            sourceSurface.BlockedVariableProperties = [.. sourceSurface.BlockedVariableProperties, sourceRow.Property];
                            if (sourceRow.Kind == MultiConfigurationOperandKind.SemiDiameter && (!sourceSurface.SemiDiameterFixed || sourceSurface.SemiDiameterMaximum))
                                BlockTraceVariables(configurations[configuration]);
                        }
                    }
                }
        return result;
    }

    private static List<MultiConfigurationOperand> OrderPickupRows(List<MultiConfigurationOperand> rows, IReadOnlyList<MultiConfigurationPickup> pickups)
    {
        var dependencies = pickups.Select(pickup => (Source: pickup.SourceOperand, Target: pickup.Operand)).Distinct().ToArray();
        var pending = rows.ToDictionary(row => row, row => dependencies.Count(edge => edge.Target == row));
        var positions = rows.Select((row, index) => (row, index)).ToDictionary(item => item.row, item => item.index);
        var ready = new PriorityQueue<MultiConfigurationOperand, int>();
        foreach (var row in rows.Where(row => pending[row] == 0)) ready.Enqueue(row, positions[row]);
        var ordered = new List<MultiConfigurationOperand>();
        while (ready.TryDequeue(out var row, out _))
        {
            ordered.Add(row);
            foreach (var edge in dependencies.Where(edge => edge.Source == row))
                if (--pending[edge.Target] == 0) ready.Enqueue(edge.Target, positions[edge.Target]);
        }
        if (ordered.Count != rows.Count) throw new InvalidDataException("Cyclic coefficient pickup dependencies.");
        return ordered;
    }
}
