using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Visualization;
using AnalysisAxisQuantity = OpticalSystemDesign.Application.Contracts.AnalysisAxisQuantity;
using AnalysisAxisUnit = OpticalSystemDesign.Application.Contracts.AnalysisAxisUnit;
using ContractAnalysisColorMap = OpticalSystemDesign.Application.Contracts.AnalysisColorMap;
using ContractAnalysisLineStyle = OpticalSystemDesign.Application.Contracts.AnalysisLineStyle;
using ContractAnalysisMarkerStyle = OpticalSystemDesign.Application.Contracts.AnalysisMarkerStyle;
using ContractAnalysisParameterDescriptor = OpticalSystemDesign.Application.Contracts.AnalysisParameterDescriptor;
using ContractAnalysisParameterKind = OpticalSystemDesign.Application.Contracts.AnalysisParameterKind;
using ContractAnalysisSeriesKind = OpticalSystemDesign.Application.Contracts.AnalysisSeriesKind;

namespace OpticalSystemDesign.Application.Services;

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
                    LineWidth: 2,
                    XQuantity: AnalysisAxisQuantity.Wavelength,
                    XUnit: AnalysisAxisUnit.Micrometer,
                    YQuantity: AnalysisAxisQuantity.RefractiveIndex,
                    YUnit: AnalysisAxisUnit.Dimensionless)
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
                Opacity: 0.8,
                XQuantity: AnalysisAxisQuantity.AbbeNumber,
                XUnit: AnalysisAxisUnit.Dimensionless,
                YQuantity: AnalysisAxisQuantity.RefractiveIndex,
                YUnit: AnalysisAxisUnit.Dimensionless))
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
                LineWidth: 2,
                XQuantity: AnalysisAxisQuantity.AbbeNumber,
                XUnit: AnalysisAxisUnit.Dimensionless,
                YQuantity: AnalysisAxisQuantity.RefractiveIndex,
                YUnit: AnalysisAxisUnit.Dimensionless));
        }

        return series;
    }

    private AnalysisViewDto BuildAthermalGlassMap(
        IReadOnlyList<CatalogGlassMaterial> glasses,
        CatalogGlassMaterial? selected)
    {
        var wavelengths = _runtime.Wavelengths.Select(wave => wave.Nanometers).Order().ToArray();
        var wavelengthF = wavelengths.First();
        var wavelengthD = _runtime.Wavelengths.FirstOrDefault(wave => wave.IsPrimary)?.Nanometers
            ?? wavelengths[wavelengths.Length / 2];
        var wavelengthC = wavelengths.Last();
        if (_runtime.CurrentOptic.Environment.MatchRefractiveIndexData
            && Math.Abs(_runtime.CurrentOptic.Environment.PressureAtmospheres - 1) > 1e-12)
            return MaterialView("无热化玻璃图", Array.Empty<AnalysisSeriesDto>(), new(), glasses, 0, selected,
                "无热化玻璃图按 1 atm 定义；启用环境折射率匹配时，请将系统压强设为 1 atm。")
                with { Outcome = Contracts.AnalysisOutcome.Unavailable };
        var points = new List<(CatalogGlassMaterial Glass, AnalysisPointDto Point)>();
        var largeDerivatives = new List<string>();
        AnalysisPointDto? selectedPoint = null;
        foreach (var glass in glasses)
        {
            if (!TryIndex(glass, wavelengthF, out var nf)
                || !TryIndex(glass, wavelengthD, out var nd)
                || !TryIndex(glass, wavelengthC, out var nc)
                || !CatalogThermalOptics.TryReferenceThermalPower(glass, wavelengthD, nd,
                    out var thermalPower, out var dnDt))
            {
                continue;
            }

            var chromaticPower = (nf - nc) / (nd - 1);
            if (!double.IsFinite(chromaticPower) || !double.IsFinite(thermalPower))
            {
                continue;
            }

            var point = new AnalysisPointDto(
                chromaticPower * 1e3,
                thermalPower * 1e6,
                glass.CatalogName);
            if (CatalogThermalOptics.RequiresSourceReview(dnDt))
                largeDerivatives.Add(FormattableString.Invariant($"{GlassLabel(glass)}: dn/dT={dnDt:G6}/K"));
            if (ReferenceEquals(glass, selected))
            {
                selectedPoint = point;
            }
            else
            {
                points.Add((glass, point));
            }
        }

        var series = points.GroupBy(item => item.Glass.ZemaxData?.Status ?? -1)
            .OrderBy(group => group.Key).Select(group => new AnalysisSeriesDto(
                "色光焦", "热光焦", group.Select(item => item.Point).ToArray(),
                ContractAnalysisSeriesKind.Scatter, GlassStatus(group.Key),
                ColorIndex: group.Key switch { 0 => 7, 1 => 2, 2 => 3, 3 => 0, 4 => 8, _ => 4 },
                ShowMarkers: true, MarkerStyle: ContractAnalysisMarkerStyle.Square, MarkerSize: 2.2,
                XQuantity: AnalysisAxisQuantity.ChromaticPower, XUnit: AnalysisAxisUnit.PartsPerThousand,
                YQuantity: AnalysisAxisQuantity.ThermalOpticalPower, YUnit: AnalysisAxisUnit.PartsPerMillionPerKelvin)).ToList();
        if (selectedPoint is not null)
        {
            series.Add(new AnalysisSeriesDto("色光焦", "热光焦", new[] { selectedPoint },
                ContractAnalysisSeriesKind.Scatter, $"所选：{GlassLabel(selected!)}", ColorIndex: 3,
                ShowMarkers: true, MarkerStyle: ContractAnalysisMarkerStyle.Square, MarkerSize: 5,
                XQuantity: AnalysisAxisQuantity.ChromaticPower, XUnit: AnalysisAxisUnit.PartsPerThousand,
                YQuantity: AnalysisAxisQuantity.ThermalOpticalPower, YUnit: AnalysisAxisUnit.PartsPerMillionPerKelvin));
            series.Insert(0, new AnalysisSeriesDto(
                "色光焦",
                "热光焦",
                new[]
                {
                    new AnalysisPointDto(0, 0),
                    new AnalysisPointDto(selectedPoint.X, selectedPoint.Y)
                },
                Name: "参考线",
                LineStyle: ContractAnalysisLineStyle.Dashed,
                ColorIndex: 3,
                LineWidth: 1,
                XQuantity: AnalysisAxisQuantity.ChromaticPower,
                XUnit: AnalysisAxisUnit.PartsPerThousand,
                YQuantity: AnalysisAxisQuantity.ThermalOpticalPower,
                YUnit: AnalysisAxisUnit.PartsPerMillionPerKelvin));
        }

        var allPoints = points.Select(item => item.Point).Concat(selectedPoint is null ? [] : new[] { selectedPoint }).ToArray();
        var outside = allPoints.Count(point => point.X < 0 || point.X > 60 || point.Y < -30 || point.Y > 30);
        var view = MaterialView(
            "无热化玻璃图",
            series,
            new AnalysisPlotOptionsDto(
                Title: "色光焦与热光焦",
                ShowVerticalZeroLine: true,
                ShowHorizontalZeroLine: true,
                XMinimum: 0, XMaximum: 60, YMinimum: -30, YMaximum: 30,
                ShowLegend: true, LegendBelow: true, ShowPointLabels: true, AvoidOverlappingPointLabels: true,
                HideTopAndRightAxes: true,
                DottedGrid: true,
                GridOpacity: 0.35),
            glasses,
            points.Count + (selectedPoint is null ? 0 : 1),
            selected,
            "使用工程最短、主波长和最长波长，在各玻璃目录参考温度、1 atm 下计算相对热光焦。名称按目录状态着色；默认范围外的有效点仍保留，可用“显示全部范围”查看。"
            + (largeDerivatives.Count == 0 ? "" : "\nTD 导出的 |dn/dT| ≥ 0.01/K，需核对源目录；未猜测修正或删除：\n" + string.Join("\n", largeDerivatives)));
        return view with { Rows = view.Rows.Concat(new[]
        {
            new AnalysisRowDto("最短 / 主 / 最长波长 (μm)", FormattableString.Invariant($"{wavelengthF / 1000:0.######} / {wavelengthD / 1000:0.######} / {wavelengthC / 1000:0.######}")),
            new AnalysisRowDto("默认范围内", (allPoints.Length - outside).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new AnalysisRowDto("默认范围外（保留）", outside.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new AnalysisRowDto("缺少有效波长或热数据", (glasses.Count - allPoints.Length).ToString(System.Globalization.CultureInfo.InvariantCulture))
        }).ToArray() };
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
                    MarkerSize: 3,
                    XQuantity: AnalysisAxisQuantity.Wavelength,
                    XUnit: AnalysisAxisUnit.Micrometer,
                    YQuantity: AnalysisAxisQuantity.Transmission,
                    YUnit: AnalysisAxisUnit.Dimensionless)
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
                    LineWidth: 2,
                    XQuantity: AnalysisAxisQuantity.Wavelength,
                    XUnit: AnalysisAxisUnit.Micrometer,
                    YQuantity: AnalysisAxisQuantity.Dispersion,
                    YUnit: AnalysisAxisUnit.InverseMicrometer)
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

    private static bool TryIndex(CatalogGlassMaterial glass, double wavelengthNanometers, out double value)
    {
        value = double.NaN;
        if (wavelengthNanometers < glass.MinimumWavelengthNanometers
            || wavelengthNanometers > glass.MaximumWavelengthNanometers
            || !double.IsFinite(wavelengthNanometers)
            || !double.IsFinite(glass.MinimumWavelengthNanometers)
            || !double.IsFinite(glass.MaximumWavelengthNanometers)
            || glass.MinimumWavelengthNanometers > glass.MaximumWavelengthNanometers
            || glass.Formula.StartsWith("tabulated ", StringComparison.Ordinal)
                && (glass.RefractiveIndexWavelengthsNanometers.Count == 0
                    || wavelengthNanometers < glass.RefractiveIndexWavelengthsNanometers.Min()
                    || wavelengthNanometers > glass.RefractiveIndexWavelengthsNanometers.Max()))
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
            or ArgumentException
            or IndexOutOfRangeException
            or NotSupportedException)
        {
            return false;
        }
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
