using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.App.Manufacturing;

public enum ManufacturabilitySeverity
{
    Pass,
    Warning,
    Error
}

public sealed record OpticalElementDefinition(
    int ElementNumber,
    SurfaceRowDto FrontSurface,
    SurfaceRowDto BackSurface)
{
    public string DisplayName =>
        $"元件 {ElementNumber}  S{FrontSurface.Number}-S{BackSurface.Number}  {FrontSurface.Material}";

    public string Material => FrontSurface.Material;

    public double Diameter => MechanicalDiameter;

    public double MechanicalDiameter => Math.Max(
        FrontSurface.SemiDiameter,
        BackSurface.SemiDiameter) * 2;

    public double CenterThickness => FrontSurface.Thickness;

    public double ClearSemiDiameter => Math.Min(
        FrontSurface.SemiDiameter,
        BackSurface.SemiDiameter);
}

public sealed record OpticalDrawingElementDefinition(
    IReadOnlyList<OpticalElementDefinition> Components)
{
    public bool IsCemented => Components.Count > 1;

    public string ComponentNumbers => string.Join("+", Components.Select(component => component.ElementNumber));

    public string DisplayName => IsCemented
        ? $"\u80f6\u5408\u900f\u955c {ComponentNumbers}  S{FrontSurface.Number}-S{BackSurface.Number}  {Material}"
        : $"\u5355\u900f\u955c {ComponentNumbers}  S{FrontSurface.Number}-S{BackSurface.Number}  {Material}";

    public SurfaceRowDto FrontSurface => Components[0].FrontSurface;

    public SurfaceRowDto BackSurface => Components[^1].BackSurface;

    public IReadOnlyList<SurfaceRowDto> Surfaces => Components
        .Select(component => component.FrontSurface)
        .Append(BackSurface)
        .ToArray();

    public string Material => string.Join(" + ", Components.Select(component => component.Material));

    public double Diameter => Components.Max(component => component.Diameter);

    public double CenterThickness => Components.Sum(component => component.CenterThickness);

    public double ClearSemiDiameter => Components.Min(component => component.ClearSemiDiameter);

    public static implicit operator OpticalDrawingElementDefinition(OpticalElementDefinition element) =>
        new(new[] { element });
}

public sealed record ManufacturabilitySettings(
    double MinimumCenterThickness = 1.0,
    double MinimumEdgeThickness = 0.8,
    double MaximumDiameterThicknessRatio = 25,
    double MinimumRadiusDiameterRatio = 0.55,
    double MaximumEdgeSlopeDegrees = 60);

public sealed record ManufacturabilityFinding(
    int ElementNumber,
    string Surfaces,
    ManufacturabilitySeverity Severity,
    string Check,
    string MeasuredValue,
    string Recommendation)
{
    public string SeverityText => Severity switch
    {
        ManufacturabilitySeverity.Error => "不可加工",
        ManufacturabilitySeverity.Warning => "需评审",
        _ => "通过"
    };
}

public sealed record ManufacturabilityGeometryMetric(
    int ElementNumber,
    string Surfaces,
    string Item,
    string Value,
    string Note = "");

public sealed record ManufacturabilityReport(
    IReadOnlyList<OpticalElementDefinition> Elements,
    IReadOnlyList<ManufacturabilityGeometryMetric> GeometryMetrics,
    IReadOnlyList<ManufacturabilityFinding> Findings)
{
    public int ErrorCount => Findings.Count(item => item.Severity == ManufacturabilitySeverity.Error);

    public int WarningCount => Findings.Count(item => item.Severity == ManufacturabilitySeverity.Warning);

    public int PassCount => Findings.Count(item => item.Severity == ManufacturabilitySeverity.Pass);
}

public static class OpticalManufacturingModel
{
    public static IReadOnlyList<OpticalElementDefinition> BuildElements(
        IReadOnlyList<SurfaceRowDto> surfaces)
    {
        var unsupported = surfaces.Where(surface => !surface.GeometryComputable).ToArray();
        if (unsupported.Length > 0)
        {
            var details = string.Join(
                "；",
                unsupported.Select(surface =>
                    $"表面 {surface.Number}，原始类型“{surface.GeometryKind.Replace("不支持：", string.Empty, StringComparison.Ordinal)}”：当前版本不支持该几何"));
            throw new InvalidOperationException(
                $"无法导出制造数据/图纸。{details}；不会将其按标准面或平面处理。");
        }

        var elements = new List<OpticalElementDefinition>();
        for (var index = 0; index + 1 < surfaces.Count; index++)
        {
            var front = surfaces[index];
            if (!IsOpticalMaterial(front.Material) || front.Thickness <= 0)
            {
                continue;
            }

            elements.Add(new OpticalElementDefinition(elements.Count + 1, front, surfaces[index + 1]));
        }

        return elements;
    }

    public static IReadOnlyList<OpticalDrawingElementDefinition> BuildDrawingElements(
        IReadOnlyList<SurfaceRowDto> surfaces)
    {
        var singleElements = BuildElements(surfaces);
        var drawings = new List<OpticalDrawingElementDefinition>();
        var cementedRun = new List<OpticalElementDefinition>();

        foreach (var element in singleElements)
        {
            if (cementedRun.Count > 0
                && cementedRun[^1].BackSurface.Number != element.FrontSurface.Number)
            {
                AddCementedDrawing();
            }

            drawings.Add(element);
            cementedRun.Add(element);
        }

        AddCementedDrawing();
        return drawings;

        void AddCementedDrawing()
        {
            if (cementedRun.Count > 1)
            {
                drawings.Add(new OpticalDrawingElementDefinition(cementedRun.ToArray()));
            }

            cementedRun.Clear();
        }
    }

    public static ManufacturabilityReport Evaluate(
        IReadOnlyList<SurfaceRowDto> surfaces,
        ManufacturabilitySettings settings)
    {
        var elements = BuildElements(surfaces);
        var metrics = elements.SelectMany(BuildGeometryMetrics).ToArray();
        var findings = new List<ManufacturabilityFinding>();
        foreach (var element in elements)
        {
            EvaluateElement(element, settings, findings);
        }

        return new ManufacturabilityReport(elements, metrics, findings);
    }

    public static double? Sag(double radius, double conic, double height)
    {
        if (Math.Abs(radius) < 1e-12 || !double.IsFinite(radius))
        {
            return 0;
        }

        var curvature = 1 / radius;
        var radialSquared = height * height;
        var radicand = 1 - ((1 + conic) * curvature * curvature * radialSquared);
        if (radicand < 0)
        {
            return null;
        }

        var denominator = 1 + Math.Sqrt(radicand);
        return Math.Abs(denominator) < 1e-12
            ? null
            : curvature * radialSquared / denominator;
    }

    public static double? MinimumEdgeThickness(OpticalElementDefinition element)
    {
        var minimum = double.PositiveInfinity;
        for (var sample = 0; sample <= 64; sample++)
        {
            var height = element.ClearSemiDiameter * sample / 64.0;
            var frontSag = Sag(
                element.FrontSurface.Radius,
                element.FrontSurface.Conic,
                height);
            var backSag = Sag(
                element.BackSurface.Radius,
                element.BackSurface.Conic,
                height);
            if (frontSag is null || backSag is null)
            {
                return null;
            }

            minimum = Math.Min(
                minimum,
                element.CenterThickness + backSag.Value - frontSag.Value);
        }

        return minimum;
    }

    private static IReadOnlyList<ManufacturabilityGeometryMetric> BuildGeometryMetrics(
        OpticalElementDefinition element)
    {
        var surfaces = SurfaceRange(element);
        var result = new List<ManufacturabilityGeometryMetric>
        {
            Metric(element, "机械直径", FormatMillimeters(element.MechanicalDiameter), "按前后表面较大半口径计算"),
            Metric(element, "中心厚度", FormatMillimeters(element.CenterThickness)),
            Metric(element, "有光焦度面半径绝对值", PoweredRadiusText(element)),
            Metric(element, "全口径弧高", FullApertureSagText(element), "按共同净半口径计算"),
            Metric(element, "全口径边厚", FullApertureEdgeThicknessText(element), "按共同净半口径计算"),
            Metric(element, "球面边缘倾角", EdgeSlopeText(element), "按共同净半口径的局部切线角计算"),
            Metric(element, "表面类型", SurfaceTypeText(element))
        };
        return result;

        ManufacturabilityGeometryMetric Metric(
            OpticalElementDefinition item,
            string name,
            string value,
            string note = "") =>
            new(item.ElementNumber, surfaces, name, value, note);
    }

    private static string PoweredRadiusText(OpticalElementDefinition element)
    {
        var powered = Surfaces(element)
            .Where(surface => double.IsFinite(surface.Radius) && Math.Abs(surface.Radius) > 1e-12)
            .Select(surface => $"S{surface.Number} |R| {FormatNumber(Math.Abs(surface.Radius))} mm")
            .ToArray();
        return powered.Length == 0 ? "无有光焦度面" : string.Join("；", powered);
    }

    private static string FullApertureSagText(OpticalElementDefinition element)
    {
        var values = Surfaces(element).Select(surface =>
        {
            var sag = Sag(surface.Radius, surface.Conic, element.ClearSemiDiameter);
            return sag is null
                ? $"S{surface.Number} 超出实数矢高范围"
                : $"S{surface.Number} {FormatSignedMillimeters(sag.Value)}";
        });
        return string.Join("；", values);
    }

    private static string FullApertureEdgeThicknessText(OpticalElementDefinition element)
    {
        var edgeThickness = EdgeThicknessAt(element, element.ClearSemiDiameter);
        return edgeThickness is null
            ? "超出实数矢高范围"
            : FormatMillimeters(edgeThickness.Value);
    }

    private static string EdgeSlopeText(OpticalElementDefinition element)
    {
        var values = Surfaces(element)
            .Where(surface => double.IsFinite(surface.Radius) && Math.Abs(surface.Radius) > 1e-12)
            .Select(surface =>
            {
                var slope = EdgeSlopeDegrees(surface, element.ClearSemiDiameter);
                return slope is null
                    ? $"S{surface.Number} 超出实数矢高范围"
                    : $"S{surface.Number} {slope.Value:0.###}°";
            })
            .ToArray();
        return values.Length == 0 ? "0°（平面）" : string.Join("；", values);
    }

    private static string SurfaceTypeText(OpticalElementDefinition element) =>
        string.Join("；", Surfaces(element)
            .Select(surface => $"S{surface.Number} {SurfaceTypeDisplay(surface.GeometryKind)}"));

    private static string SurfaceTypeDisplay(string geometryKind) =>
        geometryKind is "平面" or "标准球面/圆锥" ? "标准面" : geometryKind;

    private static IEnumerable<SurfaceRowDto> Surfaces(OpticalElementDefinition element)
    {
        yield return element.FrontSurface;
        yield return element.BackSurface;
    }

    private static string SurfaceRange(OpticalElementDefinition element) =>
        $"S{element.FrontSurface.Number}-S{element.BackSurface.Number}";

    private static double? EdgeThicknessAt(OpticalElementDefinition element, double height)
    {
        var frontSag = Sag(
            element.FrontSurface.Radius,
            element.FrontSurface.Conic,
            height);
        var backSag = Sag(
            element.BackSurface.Radius,
            element.BackSurface.Conic,
            height);
        return frontSag is null || backSag is null
            ? null
            : element.CenterThickness + backSag.Value - frontSag.Value;
    }

    private static string FormatMillimeters(double value) => $"{FormatNumber(value)} mm";

    private static string FormatSignedMillimeters(double value) =>
        $"{(value >= 0 ? "+" : string.Empty)}{FormatNumber(value)} mm";

    private static string FormatNumber(double value) => value.ToString("0.###");

    private static void EvaluateElement(
        OpticalElementDefinition element,
        ManufacturabilitySettings settings,
        ICollection<ManufacturabilityFinding> findings)
    {
        var surfaceText = $"S{element.FrontSurface.Number}-S{element.BackSurface.Number}";
        var before = findings.Count;
        if (element.CenterThickness <= 0)
        {
            Add(ManufacturabilitySeverity.Error, "中心厚度", $"{element.CenterThickness:0.###} mm", "中心厚度必须大于 0 mm。");
        }
        else if (element.CenterThickness < settings.MinimumCenterThickness)
        {
            Add(ManufacturabilitySeverity.Warning, "中心厚度", $"{element.CenterThickness:0.###} mm", $"建议不小于 {settings.MinimumCenterThickness:0.###} mm，或由工艺人员确认夹持方案。");
        }

        var edgeThickness = MinimumEdgeThickness(element);
        if (edgeThickness is null)
        {
            Add(ManufacturabilitySeverity.Error, "曲面有效域", "超出实数矢高范围", "减小净口径、增大曲率半径或检查圆锥系数。");
        }
        else if (edgeThickness <= 0)
        {
            Add(ManufacturabilitySeverity.Error, "最小边缘厚度", $"{edgeThickness:0.###} mm", "两曲面发生相交，必须修改半径、口径或中心厚度。");
        }
        else if (edgeThickness < settings.MinimumEdgeThickness)
        {
            Add(ManufacturabilitySeverity.Warning, "最小边缘厚度", $"{edgeThickness:0.###} mm", $"建议不小于 {settings.MinimumEdgeThickness:0.###} mm，以满足倒边和装夹余量。");
        }

        if (element.CenterThickness > 0)
        {
            var ratio = element.Diameter / element.CenterThickness;
            if (ratio > settings.MaximumDiameterThicknessRatio)
            {
                Add(ManufacturabilitySeverity.Warning, "口径/厚度比", ratio.ToString("0.##"), $"高于设定上限 {settings.MaximumDiameterThicknessRatio:0.##}，薄片加工和检测易变形。");
            }
        }

        CheckSurface(element.FrontSurface, "前表面");
        CheckSurface(element.BackSurface, "后表面");

        if (findings.Count == before)
        {
            Add(ManufacturabilitySeverity.Pass, "综合评估", "满足当前规则", "可进入公差细化和工艺审核。");
        }

        return;

        void CheckSurface(SurfaceRowDto surface, string label)
        {
            if (Math.Abs(surface.Radius) > 1e-12)
            {
                var ratio = Math.Abs(surface.Radius) / Math.Max(1e-9, element.Diameter);
                if (ratio < settings.MinimumRadiusDiameterRatio)
                {
                    Add(ManufacturabilitySeverity.Warning, $"{label}曲率", $"|R|/D = {ratio:0.###}", "曲面较陡，建议确认磨削工具、抛光头可达性和检测补偿能力。");
                }

                var edgeSlope = EdgeSlopeDegrees(surface, element.ClearSemiDiameter);
                if (edgeSlope is null)
                {
                    Add(ManufacturabilitySeverity.Error, $"{label}有效域", "无实数矢高", "减小口径或重新检查曲率半径和圆锥系数。");
                }
                else if (edgeSlope > settings.MaximumEdgeSlopeDegrees)
                {
                    Add(ManufacturabilitySeverity.Warning, $"{label}边缘斜率", $"{edgeSlope:0.#}°", $"超过 {settings.MaximumEdgeSlopeDegrees:0.#}°，需要专用工装或非传统加工路线。");
                }
            }

            if (!surface.GeometryKind.Contains("标准", StringComparison.OrdinalIgnoreCase)
                && !surface.GeometryKind.Contains("平面", StringComparison.OrdinalIgnoreCase))
            {
                Add(ManufacturabilitySeverity.Warning, $"{label}面型", surface.GeometryKind, "属于特殊面型，建议采用 CNC/磁流变加工并明确面形检测数据接口。");
            }
        }

        void Add(
            ManufacturabilitySeverity severity,
            string check,
            string measured,
            string recommendation)
        {
            findings.Add(new ManufacturabilityFinding(
                element.ElementNumber,
                surfaceText,
                severity,
                check,
                measured,
                recommendation));
        }
    }

    private static double? EdgeSlopeDegrees(SurfaceRowDto surface, double semiDiameter)
    {
        var delta = Math.Max(1e-6, semiDiameter * 1e-4);
        var outer = Sag(surface.Radius, surface.Conic, semiDiameter);
        var inner = Sag(surface.Radius, surface.Conic, Math.Max(0, semiDiameter - delta));
        return outer is null || inner is null
            ? null
            : Math.Atan(Math.Abs((outer.Value - inner.Value) / delta)) * 180 / Math.PI;
    }

    private static bool IsOpticalMaterial(string material)
    {
        return !string.IsNullOrWhiteSpace(material)
            && !material.Equals("Air", StringComparison.OrdinalIgnoreCase)
            && !material.Equals("Vacuum", StringComparison.OrdinalIgnoreCase)
            && !material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
    }
}
