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

    public double Diameter => FrontSurface.SemiDiameter * 2;

    public double CenterThickness => FrontSurface.Thickness;

    public double ClearSemiDiameter => Math.Min(
        FrontSurface.SemiDiameter,
        BackSurface.SemiDiameter);
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

public sealed record ManufacturabilityReport(
    IReadOnlyList<OpticalElementDefinition> Elements,
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

    public static ManufacturabilityReport Evaluate(
        IReadOnlyList<SurfaceRowDto> surfaces,
        ManufacturabilitySettings settings)
    {
        var elements = BuildElements(surfaces);
        var findings = new List<ManufacturabilityFinding>();
        foreach (var element in elements)
        {
            EvaluateElement(element, settings, findings);
        }

        return new ManufacturabilityReport(elements, findings);
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
