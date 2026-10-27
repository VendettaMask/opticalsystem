using System.Globalization;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Application.Contracts;
using SkiaSharp;

namespace OpticalSystemDesign.App.Manufacturing;

internal static partial class OpticalDrawingRendererCore
{
    internal static string Gb1991RadiusDimensionText(double radius) =>
        Math.Abs(radius) < 1e-12 || !double.IsFinite(radius) ? "R∞" : "R" + Math.Abs(radius).ToString("G12", CultureInfo.InvariantCulture);

    private static string Gb1991RadiusIndication(SurfaceRowDto surface) =>
        (IsGb1991Asphere(surface) ? "基底" : "") + Gb1991RadiusDimensionText(surface.Radius);

    private static bool IsGb1991Asphere(SurfaceRowDto surface) =>
        surface.GeometryDefinition?.Kind is "even_asphere" or "odd_asphere" or "forbes_q"
        || (surface.GeometryDefinition?.Kind != "plane" && double.IsFinite(surface.Radius)
            && Math.Abs(surface.Radius) >= 1e-12 && Math.Abs(surface.Conic) > 0);

    private static ManufacturingComponentProfile BuildGb1991MechanicalProfile(OpticalElementDefinition component, double frontZ) =>
        BuildManufacturingComponentProfile(component with
        {
            FrontSurface = component.FrontSurface with { SemiDiameter = component.FrontSurface.MechanicalSemiDiameter ?? component.FrontSurface.SemiDiameter },
            BackSurface = component.BackSurface with { SemiDiameter = component.BackSurface.MechanicalSemiDiameter ?? component.BackSurface.SemiDiameter }
        }, frontZ);

    private static bool TryGb1991EffectiveSemiDiameter(OpticalDrawingSheet sheet, out double radius, out string? error)
    {
        radius = double.PositiveInfinity;
        error = null;
        foreach (var surface in sheet.Element.Surfaces)
        {
            var aperture = surface.DrawingAperture;
            if (aperture is null && surface.ApertureKind is not ("无" or "none" or ""))
                error = $"表面{surface.Number}缺少物理孔径尺寸快照，不能仅按显示名称填写D0。";
            else if (aperture is { Kind: not ("none" or "circular") })
                error = $"表面{surface.Number}含非居中圆形孔径，当前老国标透镜图不能将其简化为圆形D0。";
            var local = surface.SemiDiameter;
            if (aperture is { Kind: "circular" })
            {
                if (aperture.CircularRadius is not { } circular || !double.IsFinite(circular) || circular <= 0)
                    error = $"表面{surface.Number}的圆形孔径尺寸缺失或无效。";
                else local = Math.Min(local, circular);
            }
            if (!double.IsFinite(local) || local <= 0)
                error = $"表面{surface.Number}的有效半口径必须为正有限值。";
            if (error is not null) return false;
            radius = Math.Min(radius, local);
        }
        return true;
    }

    internal static IReadOnlyList<string> Gb1991GeometryDefinitionLines(OpticalDrawingSheet sheet)
    {
        var lines = new List<string>();
        var z = 0d;
        for (var index = 0; index < sheet.Element.Components.Count; index++)
        {
            var component = sheet.Element.Components[index];
            var profile = BuildGb1991MechanicalProfile(component, z);
            z = profile.BackVertexZ;
            var edge = profile.Back[^1].Z - profile.Front[^1].Z;
            lines.Add($"{(sheet.Element.IsCemented ? $"L{index + 1} " : "")}图示边缘厚度（参考）：t={edge:0.######} mm，直径{profile.ProfileSemiDiameter * 2:0.######} mm。");
        }
        for (var index = 0; index < sheet.Element.Surfaces.Count; index++)
        {
            var surface = sheet.Element.Surfaces[index];
            if (!IsGb1991Asphere(surface)) continue;
            var kind = surface.GeometryDefinition?.Kind ?? "standard";
            var coefficients = surface.GeometryDefinition?.Numbers
                .Where(pair => pair.Key.StartsWith('c') && int.TryParse(pair.Key.AsSpan(1), out _))
                .OrderBy(pair => int.Parse(pair.Key.AsSpan(1), CultureInfo.InvariantCulture)).ToArray() ?? [];
            if (kind == "forbes_q")
            {
                lines.Add($"S{index + 1} 非球面（Forbes Q）：{sheet.LegacyGb1991?.AsphericDefinitionReference}");
                lines.Add("面型参数（r、z以mm计）：" + string.Join("；", surface.GeometryDefinition!.Numbers
                    .Where(pair => pair.Key is not ("radius" or "conic"))
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={Exact(pair.Value)}")));
            }
            else
            {
                var polynomial = kind == "even_asphere" ? "+Σa(2i+2)·r^(2i+2)" : kind == "odd_asphere" ? "+Σa(i+1)·r^(i+1)" : "";
                lines.Add($"S{index + 1} 非球面：z=c·r²/[1+√(1-(1+k)c²r²)]{polynomial}；r、z以mm计，c=1/R（R∞时c=0）。");
                if (coefficients.Length > 0)
                    lines.Add($"i=0…{coefficients.Length - 1}；" + string.Join("；", coefficients.Select((pair, i) =>
                        $"a{(kind == "even_asphere" ? 2 * (i + 1) : i + 1)}={Exact(pair.Value)}")));
            }
            lines.Add($"S{index + 1} 基底{(Math.Abs(surface.Radius) < 1e-12 || !double.IsFinite(surface.Radius) ? "R∞" : $"R={Exact(surface.Radius)} mm")}；k={Exact(surface.Conic)}；原点为本面顶点，z沿光轴，r为径向坐标。");
        }
        return lines;
        static string Exact(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    }

    internal sealed record Gb1991RoughnessAnnotation(string Scope, double? RaMicrometers, double? SamplingLengthMillimeters);

    internal static IReadOnlyList<Gb1991RoughnessAnnotation> Gb1991RoughnessAnnotations(OpticalDrawingSheet sheet)
    {
        var requirements = sheet.LegacyGb1991 ?? new LegacyGb1991Requirements();
        return new[]
        {
            new Gb1991RoughnessAnnotation($"S1–S{sheet.Element.Surfaces.Count}光学面", requirements.OpticalSurfaceRaMicrometers, requirements.OpticalRaSamplingLengthMillimeters),
            new Gb1991RoughnessAnnotation("其余表面", requirements.OtherSurfaceRaMicrometers, requirements.OtherRaSamplingLengthMillimeters)
        };
    }

    private static float Gb1991RoughnessHeight(OpticalDrawingSheet sheet) =>
        25 + Gb1991RoughnessAnnotations(sheet).Sum(item => item.RaMicrometers is null ? 12f : 25f);

    private static void DrawGb1991Roughness(SKCanvas canvas, OpticalDrawingSheet sheet, float left, float y, SKPaint thin)
    {
        DrawText(canvas, "表面粗糙度（Ra：μm；取样长度l：mm）：", left, y, 7, SKTextAlign.Left);
        y += 25;
        foreach (var item in Gb1991RoughnessAnnotations(sheet))
        {
            DrawText(canvas, item.Scope, left, y, 7, SKTextAlign.Left);
            if (item.RaMicrometers is not { } ra)
            {
                DrawText(canvas, "未规定", left + 82, y, 7, SKTextAlign.Left);
                y += 12;
                continue;
            }
            // Basic unequal-leg 60-degree symbol. The extension carries the value and
            // sampling length; no material-removal bar/circle or polishing grade is invented.
            var x = left + 86;
            var tip = y + 3;
            canvas.DrawLine(x - 3, tip - 5.196f, x, tip, thin);
            canvas.DrawLine(x, tip, x + 8, tip - 13.856f, thin);
            canvas.DrawLine(x + 8, tip - 13.856f, x + 78, tip - 13.856f, thin);
            DrawText(canvas, "Ra " + ra.ToString("G12", CultureInfo.InvariantCulture), x + 11, tip - 16, 7, SKTextAlign.Left);
            DrawText(canvas, item.SamplingLengthMillimeters is { } length ? "l=" + length.ToString("G12", CultureInfo.InvariantCulture) : "l 未规定",
                x + 12, tip - 4, 6.5f, SKTextAlign.Left);
            y += 25;
        }
    }

    private static IReadOnlyList<string> ValidateGb1991Annotations(OpticalDrawingSheet sheet)
    {
        var issues = new List<string>();
        var requirements = sheet.LegacyGb1991 ?? new LegacyGb1991Requirements();
        foreach (var item in Gb1991RoughnessAnnotations(sheet))
        {
            Positive(item.RaMicrometers, item.Scope + " Ra");
            Positive(item.SamplingLengthMillimeters, item.Scope + "取样长度");
            if (item.SamplingLengthMillimeters is not null && item.RaMicrometers is null)
                issues.Add(item.Scope + "填写取样长度时必须同时填写Ra。");
        }
        Positive(requirements.FringeReferenceWavelengthNanometers, "检验波长");
        if (!TryGb1991EffectiveSemiDiameter(sheet, out _, out var apertureError)) issues.Add(apertureError!);
        if (!string.IsNullOrWhiteSpace(requirements.TestPlateGrade))
        {
            var plate = requirements.TestPlateGrade.Trim().ToUpperInvariant().Replace("：", ":", StringComparison.Ordinal);
            var entries = plate.Split([';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var faces = new HashSet<int>();
            if (plate is not ("A" or "B") && (entries.Length == 0 || entries.Any(entry =>
            {
                var match = Regex.Match(entry, "^S([1-9][0-9]*):([AB])$", RegexOptions.CultureInvariant);
                return !match.Success || !int.TryParse(match.Groups[1].Value, out var face)
                    || face > sheet.Element.Surfaces.Count || !faces.Add(face);
            }))) issues.Add("ΔR样板等级须为A或B；分面可填S1:A；S2:B，不是mm半径公差。");
        }
        var z = 0d;
        foreach (var component in sheet.Element.Components)
        {
            var profile = BuildGb1991MechanicalProfile(component, z);
            z = profile.BackVertexZ;
            if (Math.Abs(profile.ProfileSemiDiameter * 2 - component.Diameter) > 1e-6)
                issues.Add($"元件{component.ElementNumber}的机械口径超出可用面型或出现表面相交，不能裁切轮廓后仍按原直径出老国标图。");
        }
        if (sheet.Element.Surfaces.Any(surface => surface.GeometryDefinition?.Kind == "forbes_q")
            && string.IsNullOrWhiteSpace(requirements.AsphericDefinitionReference))
            issues.Add("Forbes Q非球面须填写面型定义或受控图纸引用，不能仅凭基底R出图。");
        return issues;
        void Positive(double? value, string name)
        {
            if (value is { } number && (!double.IsFinite(number) || number <= 0))
                issues.Add(name + "必须为正有限值；未规定时留空。");
        }
    }
}
