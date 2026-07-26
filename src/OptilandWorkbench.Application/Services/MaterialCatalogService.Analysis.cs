using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Visualization;
using ContractAnalysisColorMap = OptilandWorkbench.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OptilandWorkbench.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OptilandWorkbench.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OptilandWorkbench.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OptilandWorkbench.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OptilandWorkbench.Application.Contracts.AnalysisSeriesKind;

namespace OptilandWorkbench.Application.Services;

internal sealed partial class MaterialCatalogService
{
private static AnalysisViewDto BuildDispersionDiagram(
        CatalogGlassMaterial? selected,
        int sampleCount)
    {
        if (selected is null)
        {
            return CurveView(
                "色散图",
                Array.Empty<AnalysisSeriesDto>(),
                null,
                0,
                "没有可用于折射率计算的玻璃。");
        }

        var (minimum, maximum) = VisibleWavelengthRange(selected);
        var points = SampleIndexCurve(selected, minimum, maximum, sampleCount);
        var series = points.Count == 0
            ? Array.Empty<AnalysisSeriesDto>()
            : new[]
            {
                new AnalysisSeriesDto(
                    "波长 (μm)",
                    "折射率 n",
                    points,
                    Name: GlassLabel(selected),
                    ColorIndex: 0,
                    LineWidth: 2)
            };
        return CurveView(
            "色散图",
            series,
            selected,
            points.Count,
            $"使用目录色散公式绘制 {minimum:0.###}–{maximum:0.###} μm 范围内的折射率 n(λ)。",
            new AnalysisRowDto("色散公式", selected.Formula),
            new AnalysisRowDto("波长范围", $"{minimum:0.###}–{maximum:0.###} μm"));
    }

    private static IReadOnlyList<AnalysisPointDto> SampleIndexCurve(
        CatalogGlassMaterial glass,
        double minimum,
        double maximum,
        int sampleCount)
    {
        var count = Math.Clamp(sampleCount, 16, 1001);
        var points = new List<AnalysisPointDto>(count);
        for (var index = 0; index < count; index++)
        {
            var wavelength = minimum + ((maximum - minimum) * index / (count - 1.0));
            if (TryIndex(glass, wavelength * 1000.0, out var refractiveIndex))
            {
                points.Add(new AnalysisPointDto(
                    wavelength,
                    refractiveIndex,
                    $"{wavelength:0.####} μm"));
            }
        }

        return points;
    }

    private static (double Minimum, double Maximum) VisibleWavelengthRange(CatalogGlassMaterial glass)
    {
        var catalogMinimum = glass.MinimumWavelengthNanometers / 1000.0;
        var catalogMaximum = glass.MaximumWavelengthNanometers / 1000.0;
        var minimum = Math.Max(catalogMinimum, 0.4);
        var maximum = Math.Min(catalogMaximum, 0.8);
        return maximum > minimum
            ? (minimum, maximum)
            : (catalogMinimum, catalogMaximum);
    }

    private static AnalysisViewDto BuildGlassMap(
        IReadOnlyList<CatalogGlassMaterial> glasses,
        CatalogGlassMaterial? selected)
    {
        var points = new List<(CatalogGlassMaterial Glass, AnalysisPointDto Point)>();
        AnalysisPointDto? selectedPoint = null;
        foreach (var glass in glasses)
        {
            var dto = ToGlassMaterialDto(glass);
            if (!double.IsFinite(dto.AbbeNumber) || !double.IsFinite(dto.RefractiveIndexD))
            {
                continue;
            }

            var point = new AnalysisPointDto(
                dto.AbbeNumber,
                dto.RefractiveIndexD,
                glass.CatalogName);
            if (ReferenceEquals(glass, selected))
            {
                selectedPoint = point;
            }
            else
            {
                points.Add((glass, point));
            }
        }

        var series = BuildGlassMapSeries(points, selectedPoint);
        return MaterialView(
            "玻璃图",
            series,
            new AnalysisPlotOptionsDto(
                Title: "折射率 nd 与阿贝数 Vd",
                XMinimum: 20,
                XMaximum: 70,
                YMinimum: 1.4,
                YMaximum: 2.2,
                ShowLegend: false,
                HideTopAndRightAxes: true,
                DottedGrid: false,
                GridOpacity: 0.55,
                ReverseX: true,
                ShowPointLabels: true),
            glasses,
            points.Count + (selectedPoint is null ? 0 : 1),
            selected,
            "玻璃名称按材料库着色；横轴依照光学玻璃图惯例由左向右递减。");
    }

    private static IReadOnlyList<AnalysisSeriesDto> BuildGlassMapSeries(
        IReadOnlyList<(CatalogGlassMaterial Glass, AnalysisPointDto Point)> points,
        AnalysisPointDto? selectedPoint)
    {
        var series = points
            .GroupBy(item => item.Glass.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select((group, colorIndex) => new AnalysisSeriesDto(
                "阿贝数 Vd",
                "折射率 nd",
                group.Select(item => item.Point).ToArray(),
                ContractAnalysisSeriesKind.Scatter,
                group.Key,
                ColorIndex: colorIndex,
                ShowMarkers: true,
                MarkerStyle: ContractAnalysisMarkerStyle.Square,
                MarkerSize: 2.2,
                Opacity: 0.8))
            .ToList();
        if (selectedPoint is not null)
        {
            series.Add(new AnalysisSeriesDto(
                "阿贝数 Vd",
                "折射率 nd",
                new[] { selectedPoint },
                ContractAnalysisSeriesKind.Scatter,
                $"所选：{selectedPoint.Label}",
                ColorIndex: 3,
                ShowMarkers: true,
                MarkerStyle: ContractAnalysisMarkerStyle.Square,
                MarkerSize: 6,
                LineWidth: 2));
        }

        return series;
    }

    private static AnalysisViewDto BuildAthermalGlassMap(
        IReadOnlyList<CatalogGlassMaterial> glasses,
        CatalogGlassMaterial? selected)
    {
        const double wavelengthF = 486.1327;
        const double wavelengthD = 587.5618;
        const double wavelengthC = 656.2725;
        var points = new List<AnalysisPointDto>();
        AnalysisPointDto? selectedPoint = null;
        foreach (var glass in glasses)
        {
            if (!TryIndex(glass, wavelengthF, out var nf)
                || !TryIndex(glass, wavelengthD, out var nd)
                || !TryIndex(glass, wavelengthC, out var nc)
                || !TryThermalPower(glass, wavelengthD, nd, out var thermalPower))
            {
                continue;
            }

            var chromaticPower = (nf - nc) / (nd - 1);
            if (!double.IsFinite(chromaticPower) || !double.IsFinite(thermalPower))
            {
                continue;
            }

            var point = new AnalysisPointDto(
                chromaticPower,
                thermalPower * 1e6,
                GlassLabel(glass));
            if (ReferenceEquals(glass, selected))
            {
                selectedPoint = point;
            }
            else
            {
                points.Add(point);
            }
        }

        var series = ScatterSeries(
                points,
                selectedPoint,
                "色光焦 ω",
                "热光焦 γ (10⁻⁶/K)")
            .ToList();
        if (selectedPoint is not null)
        {
            series.Insert(0, new AnalysisSeriesDto(
                "色光焦 ω",
                "热光焦 γ (10⁻⁶/K)",
                new[]
                {
                    new AnalysisPointDto(0, 0),
                    new AnalysisPointDto(selectedPoint.X, selectedPoint.Y)
                },
                Name: "参考线",
                LineStyle: ContractAnalysisLineStyle.Dashed,
                ColorIndex: 3,
                LineWidth: 1));
        }

        return MaterialView(
            "无热化玻璃图",
            series,
            new AnalysisPlotOptionsDto(
                Title: "色光焦与热光焦",
                ShowVerticalZeroLine: true,
                ShowHorizontalZeroLine: true,
                ShowLegend: selectedPoint is not null,
                HideTopAndRightAxes: true,
                DottedGrid: true,
                GridOpacity: 0.35),
            glasses,
            points.Count + (selectedPoint is null ? 0 : 1),
            selected,
            "仅显示同时具备 TD 热系数和 TCE 数据的玻璃；参考线连接原点与所选玻璃。");
    }

    private static AnalysisViewDto BuildInternalTransmission(
        CatalogGlassMaterial? selected,
        double thicknessMillimeters)
    {
        var thickness = double.IsFinite(thicknessMillimeters)
            ? Math.Clamp(thicknessMillimeters, 0.01, 1000)
            : 10;
        var samples = selected?.ZemaxData?.InternalTransmissions
            .Where(sample => sample.WavelengthMicrometers > 0
                && sample.ThicknessMillimeters > 0
                && sample.Transmission >= 0
                && sample.Transmission <= 1)
            .OrderBy(sample => sample.WavelengthMicrometers)
            .Select(sample => new AnalysisPointDto(
                sample.WavelengthMicrometers,
                Math.Pow(Math.Clamp(sample.Transmission, 0, 1), thickness / sample.ThicknessMillimeters),
                $"{sample.WavelengthMicrometers:0.####} μm"))
            .ToArray()
            ?? Array.Empty<AnalysisPointDto>();
        var series = samples.Length == 0
            ? Array.Empty<AnalysisSeriesDto>()
            : new[]
            {
                new AnalysisSeriesDto(
                    "波长 (μm)",
                    "内部透过率",
                    samples,
                    Name: selected is null ? string.Empty : GlassLabel(selected),
                    ColorIndex: 0,
                    ShowMarkers: true,
                    LineWidth: 2,
                    MarkerSize: 3)
            };
        return CurveView(
            "内部透过率 vs. 波长",
            series,
            selected,
            samples.Length,
            $"目录透过率已按 Beer-Lambert 关系换算到 {thickness:0.###} mm 厚度。",
            new AnalysisRowDto("厚度", $"{thickness:0.###} mm"));
    }

    private static AnalysisViewDto BuildDispersionVsWavelength(
        CatalogGlassMaterial? selected,
        int sampleCount)
    {
        if (selected is null)
        {
            return CurveView(
                "色散 vs. 波长",
                Array.Empty<AnalysisSeriesDto>(),
                null,
                0,
                "没有可用于色散计算的玻璃。");
        }

        var (minimum, maximum) = VisibleWavelengthRange(selected);
        var count = Math.Clamp(sampleCount, 16, 1001);
        var points = new List<AnalysisPointDto>(count);
        var derivativeStep = Math.Max((maximum - minimum) / Math.Max(10000, count * 20), 1e-7);
        for (var index = 0; index < count; index++)
        {
            var wavelength = minimum + ((maximum - minimum) * index / (count - 1.0));
            var left = Math.Max(minimum, wavelength - derivativeStep);
            var right = Math.Min(maximum, wavelength + derivativeStep);
            if (right > left
                && TryIndex(selected, left * 1000.0, out var leftIndex)
                && TryIndex(selected, right * 1000.0, out var rightIndex))
            {
                var dispersion = (rightIndex - leftIndex) / (right - left);
                points.Add(new AnalysisPointDto(
                    wavelength,
                    dispersion,
                    $"{wavelength:0.####} μm"));
            }
        }

        var series = points.Count == 0
            ? Array.Empty<AnalysisSeriesDto>()
            : new[]
            {
                new AnalysisSeriesDto(
                    "波长 (μm)",
                    "色散 dn/dλ (μm⁻¹)",
                    points,
                    Name: GlassLabel(selected),
                    ColorIndex: 0,
                    LineWidth: 2)
            };
        return CurveView(
            "色散 vs. 波长",
            series,
            selected,
            points.Count,
            $"对目录折射率公式进行数值微分，绘制 {minimum:0.###}–{maximum:0.###} μm 范围内的 dn/dλ。",
            new AnalysisRowDto("色散公式", selected.Formula),
            new AnalysisRowDto("单位", "μm⁻¹"));
    }

    private static AnalysisViewDto MaterialView(
        string title,
        IReadOnlyList<AnalysisSeriesDto> series,
        AnalysisPlotOptionsDto options,
        IReadOnlyList<CatalogGlassMaterial> sourceGlasses,
        int plottedCount,
        CatalogGlassMaterial? selected,
        string note)
    {
        var rows = new[]
        {
            new AnalysisRowDto("材料库", sourceGlasses
                .Select(glass => glass.Manufacturer)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
                .ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new AnalysisRowDto("参与计算的玻璃", sourceGlasses.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new AnalysisRowDto("有效绘图点", plottedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new AnalysisRowDto("参考玻璃", selected is null ? "无" : GlassLabel(selected))
        };
        return new AnalysisViewDto(
            title,
            rows,
            note,
            series,
            options,
            Array.Empty<AnalysisPlotPaneDto>(),
            1);
    }

    private static AnalysisViewDto CurveView(
        string title,
        IReadOnlyList<AnalysisSeriesDto> series,
        CatalogGlassMaterial? selected,
        int pointCount,
        string note,
        params AnalysisRowDto[] extraRows)
    {
        var rows = new List<AnalysisRowDto>
        {
            new("玻璃", selected is null ? "无" : GlassLabel(selected)),
            new("数据点", pointCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        rows.AddRange(extraRows);
        return new AnalysisViewDto(
            title,
            rows,
            note,
            series,
            new AnalysisPlotOptionsDto(
                Title: title,
                ShowLegend: series.Count > 0,
                HideTopAndRightAxes: true,
                DottedGrid: true,
                GridOpacity: 0.35),
            Array.Empty<AnalysisPlotPaneDto>(),
            1);
    }

    private static IReadOnlyList<AnalysisSeriesDto> ScatterSeries(
        IReadOnlyList<AnalysisPointDto> points,
        AnalysisPointDto? selectedPoint,
        string xAxis,
        string yAxis)
    {
        var series = new List<AnalysisSeriesDto>
        {
            new(
                xAxis,
                yAxis,
                points,
                ContractAnalysisSeriesKind.Scatter,
                "目录玻璃",
                ColorIndex: 0,
                ShowMarkers: true,
                MarkerSize: 2.5,
                Opacity: 0.65)
        };
        if (selectedPoint is not null)
        {
            series.Add(new AnalysisSeriesDto(
                xAxis,
                yAxis,
                new[] { selectedPoint },
                ContractAnalysisSeriesKind.Scatter,
                selectedPoint.Label,
                ColorIndex: 3,
                ShowMarkers: true,
                MarkerStyle: ContractAnalysisMarkerStyle.Square,
                MarkerSize: 7,
                LineWidth: 2));
        }

        return series;
    }

    private static bool TryIndex(CatalogGlassMaterial glass, double wavelengthNanometers, out double value)
    {
        value = double.NaN;
        if (wavelengthNanometers < glass.MinimumWavelengthNanometers
            || wavelengthNanometers > glass.MaximumWavelengthNanometers)
        {
            return false;
        }

        try
        {
            value = glass.RefractiveIndex(wavelengthNanometers);
            return double.IsFinite(value) && value > 0;
        }
        catch (Exception exception) when (exception is InvalidDataException
            or InvalidOperationException
            or ArgumentOutOfRangeException
            or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryThermalPower(
        CatalogGlassMaterial glass,
        double wavelengthNanometers,
        double refractiveIndex,
        out double thermalPower)
    {
        thermalPower = double.NaN;
        var data = glass.ZemaxData;
        if (data is null
            || data.ThermalCoefficients.Count < 6
            || !data.ThermalExpansionLow.HasValue
            || refractiveIndex <= 1)
        {
            return false;
        }

        var d0 = data.ThermalCoefficients[0];
        var e0 = data.ThermalCoefficients[3];
        var lambdaTk = data.ThermalCoefficients[5];
        if (!double.IsFinite(d0) || !double.IsFinite(e0) || !double.IsFinite(lambdaTk))
        {
            return false;
        }

        var wavelengthMicrometers = wavelengthNanometers / 1000.0;
        var denominator = (wavelengthMicrometers * wavelengthMicrometers)
            - (Math.Sign(lambdaTk) * lambdaTk * lambdaTk);
        if (Math.Abs(denominator) <= 1e-12)
        {
            return false;
        }

        var dnDt = ((refractiveIndex * refractiveIndex) - 1) / (2 * refractiveIndex)
            * (d0 + (e0 / denominator));
        var expansion = data.IgnoreThermalExpansion
            ? 0
            : data.ThermalExpansionLow.Value * 1e-6;
        thermalPower = (dnDt / (refractiveIndex - 1)) - expansion;
        return double.IsFinite(thermalPower);
    }

    private static string GlassLabel(CatalogGlassMaterial glass) =>
        $"{glass.Manufacturer}:{glass.CatalogName}";

    private static double CalculateAbbeNumber(
        CatalogGlassMaterial glass,
        double refractiveIndexD,
        double wavelengthF,
        double wavelengthC)
    {
        var denominator = glass.RefractiveIndex(wavelengthF) - glass.RefractiveIndex(wavelengthC);
        return Math.Abs(denominator) > 1e-12
            ? (refractiveIndexD - 1.0) / denominator
            : double.NaN;
    }
}
