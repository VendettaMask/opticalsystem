using System.Reflection;
using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.NonSequential;

public static class NonSequentialDesignParameters
{
    public static IReadOnlyList<string> ObjectParameterNames(NonSequentialObjectDefinition item) => Array.AsReadOnly(item.Parameters.GetType()
        .GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.PropertyType == typeof(double)
            && p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0).Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());

    public static double Read(NonSequentialDocument doc, NonSequentialDesignVariable variable)
    {
        if (!doc.TryFindObject(variable.ObjectId, out var item)) throw new ArgumentException("Variable object is absent.");
        var p = item.LocalCoordinateSystem;
        return variable.Domain switch
        {
            NonSequentialVariableDomain.Position => Component(p.Origin, variable.Parameter),
            NonSequentialVariableDomain.Rotation => variable.Parameter switch { "X" => p.RotationXDegrees, "Y" => p.RotationYDegrees, "Z" => p.RotationZDegrees, _ => throw new ArgumentException("Rotation axis must be X/Y/Z.") },
            NonSequentialVariableDomain.ObjectParameter => Property(item, variable.Parameter).GetValue(item.Parameters) is double v ? v : throw new ArgumentException("Invalid numeric object parameter."),
            NonSequentialVariableDomain.SourcePhase when item.Parameters is SourceParameters source && variable.Parameter == "PhaseDegrees" => source.Physics?.PhaseDegrees ?? 0,
            _ => throw new ArgumentException("Unsupported variable domain.")
        };
    }

    public static NonSequentialDocument Apply(NonSequentialDocument source, IReadOnlyList<NonSequentialDesignVariable> variables,
        IReadOnlyList<double> values, bool requireBounds = true)
    {
        if (variables.Count != values.Count || variables.Count is < 1 or > 64) throw new ArgumentException("Specify 1–64 variables and matching values.");
        var changes = new Dictionary<Guid, NonSequentialObjectDefinition>();
        for (var i = 0; i < variables.Count; i++)
        {
            var v = variables[i]; var value = values[i];
            if (!double.IsFinite(value) || requireBounds && (value < v.LowerBound || value > v.UpperBound)) throw new ArgumentException("Variable is outside its declared bounds.");
            if (!changes.TryGetValue(v.ObjectId, out var item) && !source.TryFindObject(v.ObjectId, out item!)) throw new ArgumentException("Variable object is absent.");
            var pose = item.LocalCoordinateSystem;
            item = v.Domain switch
            {
                NonSequentialVariableDomain.Position => item with { LocalCoordinateSystem = pose with { Origin = Replace(pose.Origin, v.Parameter, value) } },
                NonSequentialVariableDomain.Rotation => item with { LocalCoordinateSystem = v.Parameter switch
                    { "X" => pose with { RotationXDegrees = value }, "Y" => pose with { RotationYDegrees = value }, "Z" => pose with { RotationZDegrees = value }, _ => throw new ArgumentException("Rotation axis must be X/Y/Z.") } },
                NonSequentialVariableDomain.ObjectParameter => WithParameter(item, v.Parameter, value),
                NonSequentialVariableDomain.SourcePhase when item.Parameters is SourceParameters p && v.Parameter == "PhaseDegrees" => item with
                    { Parameters = p with { Physics = (p.Physics ?? new()) with { PhaseDegrees = value } } },
                _ => throw new ArgumentException("Unsupported variable domain.")
            };
            changes[v.ObjectId] = item;
        }
        return new(source.Name, source.Wavelengths, source.Objects.Select(o => changes.GetValueOrDefault(o.Id, o)).ToArray(),
            source.AmbientMaterial, source.TraceSettings, source.MeshAssets);
    }

    internal static void Validate(NonSequentialDocument source, IReadOnlyList<NonSequentialDesignVariable> variables)
    {
        if (variables is null || variables.Count is < 1 or > 64 || variables.Any(v => v is null || !Enum.IsDefined(v.Domain)
            || !double.IsFinite(v.LowerBound) || !double.IsFinite(v.UpperBound) || v.LowerBound >= v.UpperBound || !double.IsFinite(v.Step) || v.Step <= 0)
            || variables.Select(v => (v.ObjectId, v.Domain, v.Parameter)).Distinct().Count() != variables.Count)
            throw new ArgumentException("Design needs 1–64 unique, bounded variables with positive physical steps.");
        foreach (var v in variables)
        { var value = Read(source, v); if (!double.IsFinite(value) || value < v.LowerBound || value > v.UpperBound) throw new ArgumentException("Nominal variable is outside the design bounds."); }
    }
    private static PropertyInfo Property(NonSequentialObjectDefinition item, string name) => ObjectParameterNames(item).Contains(name)
        ? item.Parameters.GetType().GetProperty(name)! : throw new ArgumentException("Only declared writable floating-point object parameters are continuous design variables.");
    private static NonSequentialObjectDefinition WithParameter(NonSequentialObjectDefinition item, string name, double value)
    {
        var property = Property(item, name); var copy = item.Parameters with { }; property.SetValue(copy, value);
        return item with { Parameters = copy };
    }
    private static double Component(Vector3D p, string axis) => axis switch { "X" => p.X, "Y" => p.Y, "Z" => p.Z, _ => throw new ArgumentException("Position axis must be X/Y/Z.") };
    private static Vector3D Replace(Vector3D p, string axis, double value) => axis switch
        { "X" => new(value, p.Y, p.Z), "Y" => new(p.X, value, p.Z), "Z" => new(p.X, p.Y, value), _ => throw new ArgumentException("Position axis must be X/Y/Z.") };
}
