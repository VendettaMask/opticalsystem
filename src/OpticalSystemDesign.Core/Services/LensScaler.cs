using System.Globalization;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Core.Services;

/// <summary>Detached, dimensional scaling of a sequential prescription. Wavelengths,
/// film thicknesses, optical constants and merit targets retain their physical values.</summary>
public static class LensScaler
{
    public static Optic Scale(Optic source, double factor, int firstSurface, int lastSurface)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(factor) || factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(factor), "缩放因子必须是大于零的有限数值。");
        var snapshot = source.ToSnapshot();
        if (firstSurface > lastSurface || !snapshot.Surfaces.Any(s => s.Number == firstSurface)
            || !snapshot.Surfaces.Any(s => s.Number == lastSurface))
            throw new ArgumentException("起始面和终止面必须存在，且起始面不能晚于终止面。");
        if (factor == 1) return source.Clone();
        double F(int number) => number >= firstSurface && number <= lastSurface ? factor : 1;
        var originals = source.SurfaceGroup.Items.ToArray();
        var surfaces = new List<SurfaceSnapshot>(snapshot.Surfaces.Count);
        for (var index = 0; index < snapshot.Surfaces.Count; index++)
        {
            var surface = snapshot.Surfaces[index];
            var f = F(surface.Number);
            var frame = surface.CoordinateSystem;
            if (frame is not null)
            {
                Vector3D origin;
                if (index == 0) origin = new(Multiply(frame.OriginX, f), Multiply(frame.OriginY, f), Multiply(frame.OriginZ, f));
                else
                {
                    var previous = snapshot.Surfaces[index - 1];
                    var previousFrame = originals[index - 1].CoordinateSystem;
                    var delta = new Vector3D(frame.OriginX - previous.CoordinateSystem!.OriginX,
                        frame.OriginY - previous.CoordinateSystem.OriginY, frame.OriginZ - previous.CoordinateSystem.OriginZ);
                    var local = previousFrame.ToLocalDirection(delta);
                    var gap = double.IsPositiveInfinity(previous.Thickness) && previous.Number == 0 ? 0 : previous.Thickness;
                    var transformed = previousFrame.ToGlobalDirection(new Vector3D(
                        Multiply(local.X, f), Multiply(local.Y, f), Multiply(gap, F(previous.Number)) + Multiply(local.Z - gap, f)));
                    var before = surfaces[^1].CoordinateSystem!;
                    origin = new(before.OriginX + transformed.X, before.OriginY + transformed.Y, before.OriginZ + transformed.Z);
                }
                if (!double.IsFinite(origin.X) || !double.IsFinite(origin.Y) || !double.IsFinite(origin.Z))
                    throw new ArgumentException("缩放后的表面坐标超出有效范围。");
                frame = frame with { OriginX = origin.X, OriginY = origin.Y, OriginZ = origin.Z };
            }
            if (f == 1) { surfaces.Add(surface with { CoordinateSystem = frame }); continue; }
            var semiDiameter = Multiply(surface.SemiDiameter, f);
            if (semiDiameter < .1) throw new ArgumentException($"表面 {surface.Number} 缩放后的净半径小于当前支持的最小值 0.1 mm。");
            try
            {
                var components = surface.Components;
                if (components is not null)
                {
                    CheckMaterial(components.MaterialBeforeComponent);
                    CheckMaterial(components.MaterialAfterComponent);
                    components = components with
                    {
                        Geometry = Transform(components.Geometry, f, GeometryPower),
                        Interaction = TransformInteraction(components.Interaction, f),
                        PhysicalAperture = Transform(components.PhysicalAperture, f, AperturePower)
                    };
                }
                surfaces.Add(surface with
                {
                    Radius = Length(surface.Radius, f),
                    Thickness = surface.Number == 0 && double.IsPositiveInfinity(surface.Thickness)
                        ? double.PositiveInfinity : Multiply(surface.Thickness, f),
                    SemiDiameter = semiDiameter,
                    MechanicalSemiDiameter = surface.MechanicalSemiDiameter is { } mechanical ? Multiply(mechanical, f) : null,
                    ChipZone = Multiply(surface.ChipZone, f),
                    Components = components,
                    CoordinateSystem = frame,
                    Placement = surface.Placement is { } placement ? placement with
                    {
                        X = Multiply(placement.X, f),
                        Y = Multiply(placement.Y, f),
                        Z = Multiply(placement.Z, f),
                        ObjectDistanceAtImport = Multiply(placement.ObjectDistanceAtImport, f),
                        Break = placement.Break is { } operation ? operation with { X = Multiply(operation.X, f), Y = Multiply(operation.Y, f) } : null
                    } : null
                });
            }
            catch (NotSupportedException exception)
            {
                throw new NotSupportedException($"表面 {surface.Number}：{exception.Message}", exception);
            }
        }
        var whole = firstSurface == snapshot.Surfaces[0].Number && lastSurface == snapshot.Surfaces[^1].Number;
        var backFocus = snapshot.SolveSettings?.DesiredBackFocus ?? 0;
        if (whole) backFocus = Multiply(backFocus, factor);
        else if (snapshot.SolveSettings?.KeepImageAtBackFocus == true)
        {
            for (var i = 0; i < surfaces.Count; i++)
                if (double.IsFinite(snapshot.Surfaces[i].Thickness))
                    backFocus += surfaces[i].Thickness - snapshot.Surfaces[i].Thickness;
            if (!double.IsFinite(backFocus)) throw new ArgumentException("缩放后的后焦距求解目标超出有效范围。");
        }
        var result = snapshot with
        {
            Surfaces = surfaces,
            Aperture = whole && snapshot.Aperture is { Kind: "EntrancePupilDiameter" } aperture
                ? aperture with { Value = Multiply(aperture.Value, factor) } : snapshot.Aperture,
            Fields = whole && source.FieldDefinition != FieldDefinitionKind.Angle
                ? snapshot.Fields.Select(field => field with { XAngleDegrees = Multiply(field.XAngleDegrees, factor), YAngleDegrees = Multiply(field.YAngleDegrees, factor) }).ToList()
                : snapshot.Fields,
            RadiusPickups = snapshot.RadiusPickups?.Select(p => p with
            { Scale = Multiply(p.Scale, F(p.TargetSurface) / F(p.SourceSurface)), Offset = Multiply(p.Offset, F(p.TargetSurface)) }).ToList(),
            ThicknessPickups = ScalePickups(snapshot.ThicknessPickups, F),
            SemiDiameterPickups = ScalePickups(snapshot.SemiDiameterPickups, F),
            SolveSettings = snapshot.SolveSettings is { } solve
                ? solve with { DesiredBackFocus = backFocus } : null
        };
        OpticSnapshotValidator.Validate(result);
        var scaled = source.Clone();
        scaled.ApplySnapshot(result);
        return scaled;
    }

    public static double OperandMultiplier(Optic optic, MultiConfigurationOperand operand, double factor, int first, int last)
    {
        if (operand.SurfaceNumber < first || operand.SurfaceNumber > last) return 1;
        var power = operand.Kind switch
        {
            MultiConfigurationOperandKind.Thickness or MultiConfigurationOperandKind.SemiDiameter => 1,
            MultiConfigurationOperandKind.Curvature => -1,
            MultiConfigurationOperandKind.Conic => 0,
            MultiConfigurationOperandKind.ShapeCoefficient => ShapePower(optic, operand),
            _ => throw new NotSupportedException("暂不支持带梯度折射率系数拾取的镜头缩放。")
        };
        return Multiply(1, Math.Pow(factor, power));
    }

    private static int ShapePower(Optic optic, MultiConfigurationOperand operand)
    {
        var geometry = optic.ToSnapshot().Surfaces.Single(s => s.Number == operand.SurfaceNumber).Components!.Geometry!;
        var parameter = operand.Parameter!;
        if (geometry.Kind == "concentric_zones" && parameter[0] is 'i' or 'o')
            return GeometryPower(geometry.Children![parameter[0] == 'i' ? "inner" : "outer"].Kind, "c" + parameter[1..]);
        return GeometryPower(geometry.Kind, parameter);
    }

    private static List<SurfaceValuePickupSnapshot>? ScalePickups(List<SurfaceValuePickupSnapshot>? pickups, Func<int, double> factor) =>
        pickups?.Select(p => p with
        {
            Scale = Multiply(p.Scale, factor(p.TargetSurface) / factor(p.SourceSurface)),
            Offset = Multiply(p.Offset, factor(p.TargetSurface))
        }).ToList();

    private static double Length(double value, double factor) => double.IsInfinity(value) ? value : Multiply(value, factor);

    private static double Multiply(double value, double multiplier)
    {
        var result = value * multiplier;
        if (!double.IsFinite(result) || !double.IsFinite(multiplier) || multiplier <= 0 || (value != 0 && result == 0))
            throw new ArgumentException("缩放后的数值超出有效范围，请调整缩放因子。");
        return result;
    }

    private static void CheckMaterial(ComponentSnapshot? material)
    {
        if (material is null) return;
        if (material.Kind == "gradient_index") throw new NotSupportedException("梯度折射率材料尚无完整缩放规则，未修改镜头。");
        if (material.Children is not null) foreach (var child in material.Children.Values) CheckMaterial(child);
    }

    private static ComponentSnapshot? Transform(ComponentSnapshot? component, double factor, Func<string, string, int> power)
    {
        if (component is null) return null;
        // Validate even empty payloads, so opaque components cannot masquerade as a supported plane.
        _ = power(component.Kind, "");
        return component with
        {
            Numbers = component.Numbers.ToDictionary(p => p.Key, p => power(component.Kind, p.Key) is var exponent && exponent != 0
                ? Length(p.Value, Math.Pow(factor, exponent)) : p.Value),
            Children = component.Children?.ToDictionary(p => p.Key, p => Transform(p.Value, factor, power)!)
        };
    }

    private static int GeometryPower(string kind, string key)
    {
        if (kind is not ("plane" or "tilted_plane" or "standard" or "plane_grating" or "standard_grating"
            or "even_asphere" or "odd_asphere" or "extended_asphere" or "extended_odd_asphere" or "extended_polynomial"
            or "biconic" or "separable_biconic" or "toroidal" or "polynomial" or "chebyshev" or "chebyshev_sag"
            or "zernike" or "forbes_q" or "forbes_normal_slope" or "irregular_sag" or "fringe_sag" or "grid_sag" or "concentric_zones"))
            throw new NotSupportedException($"面形 {kind} 尚无完整缩放规则，未修改镜头。");
        if (key == "") return 0;
        if (key is "radius" or "radiusX" or "radiusY" or "tangentialRadius" or "sagittalRadius" or "normalizationRadius"
            or "normalizationX" or "normalizationY" or "pupilRadius" or "decenterX" or "decenterY"
            or "spherical" or "astigmatism" or "coma" or "boundaryRadius" or "spacingX" or "spacingY" or "periodMicrometers") return 1;
        if (key is "conic" or "conicX" or "conicY" or "tiltX" or "tiltY" or "angle" or "angleRadians" or "order"
            or "termCount" or "coefficientCount" or "asphereCount" or "fringeCount" or "extrapolate" or "breakSag"
            or "maximumXOrder" or "maximumYOrder" or "rows" or "columns" or "linear" or "tangentX" or "tangentY"
            or "referenceIncidenceNormalX" or "referenceIncidenceNormalY" or "referenceIncidenceNormalZ") return 0;
        if (key.StartsWith("c_", StringComparison.Ordinal)) return kind == "polynomial" ? 1 - PairDegree(key) : 1;
        if (Index(key, "c", out var c)) return kind switch
        {
            "even_asphere" => 1 - 2 * (c + 1),
            "odd_asphere" => -c,
            "chebyshev_sag" => -1,
            _ => 1
        };
        if (Index(key, "a", out var a)) return 1 - 2 * (a + 1);
        if (Index(key, "q", out _) || Index(key, "z", out _)) return 1;
        if (Index(key, "dxy", out _)) return -1;
        if (Index(key, "dx", out _) || Index(key, "dy", out _) || Index(key, "noData", out _)) return 0;
        throw new NotSupportedException($"面形 {kind} 的参数 {key} 尚无缩放量纲。");
    }

    private static int AperturePower(string kind, string key)
    {
        if (kind is not ("circular" or "annular" or "offset_radial" or "circular_obscuration" or "spider_obscuration"
            or "rectangular" or "elliptical" or "polygon" or "file" or "union" or "intersection" or "difference"))
            throw new NotSupportedException($"孔径 {kind} 尚无缩放规则。");
        return key is "" or "armCount" or "vertexCount" or "count" or "skipHeader" ? 0 : 1;
    }

    private static ComponentSnapshot? TransformInteraction(ComponentSnapshot? component, double factor)
    {
        if (component is null) return null;
        if (component.Kind is not ("refractive" or "reflective" or "coordinate_reference" or "birefringent_interface"
            or "thin_lens" or "paraxial_lens" or "ruled_grating" or "diffractive"))
            throw new NotSupportedException($"光线交互 {component.Kind} 尚无完整缩放规则，未修改镜头。");
        return component with
        {
            Numbers = component.Numbers.ToDictionary(p => p.Key, p => p.Key switch
        {
            "focalLength" or "axisDisplayLength" => Multiply(p.Value, factor),
            "powerX" or "powerY" or "linesPerMicrometer" or "grooveFrequency" => Multiply(p.Value, 1 / factor),
            "isReflective" or "ignoredSurface" or "isExit" or "opdMode" or "order" => p.Value,
            _ => throw new NotSupportedException($"交互参数 {p.Key} 尚无缩放规则。")
        })
        };
    }

    private static bool Index(string key, string prefix, out int index) => int.TryParse(key.AsSpan(Math.Min(prefix.Length, key.Length)),
        NumberStyles.None, CultureInfo.InvariantCulture, out index) && key.StartsWith(prefix, StringComparison.Ordinal);
    private static int PairDegree(string key)
    {
        var pair = key.Split('_');
        return int.Parse(pair[1], CultureInfo.InvariantCulture) + int.Parse(pair[2], CultureInfo.InvariantCulture);
    }
}
