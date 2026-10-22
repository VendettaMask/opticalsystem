using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using static OpticalSystemDesign.Application.Services.WorkbenchMapper;

namespace OpticalSystemDesign.Application.Services;

internal sealed partial class AnalysisService
{
    private static void ValidateConfigurationInputs(Optic optic, IReadOnlyDictionary<string, string> settings, int number)
    {
        int Selected(string key) => int.TryParse(settings.GetValueOrDefault(key),
            System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
        if (Selected("FieldNumber") > Math.Max(1, optic.Fields.Count))
            throw new ArgumentException($"结构 {number} 未定义所选视场 {Selected("FieldNumber")}。", "FieldNumber");
        if (Selected("WavelengthNumber") > Math.Max(1, optic.Wavelengths.Count))
            throw new ArgumentException($"结构 {number} 未定义所选波长 {Selected("WavelengthNumber")}。", "WavelengthNumber");
        if (Selected("SurfaceNumber") is > 0 and var surface && !optic.SurfaceGroup.Items.Any(item => item.Number == surface))
            throw new ArgumentException($"结构 {number} 未定义所选表面 {surface}。", "SurfaceNumber");
    }

    private static AnalysisViewDto BuildConfigurationOverlay(
        LoadedOpticalDocument document, string key, IReadOnlyDictionary<string, string> settings, CancellationToken token)
    {
        var views = document.Configurations.Select((optic, index) =>
        {
            token.ThrowIfCancellationRequested();
            var worker = new WorkbenchRuntime(optic);
            try
            {
                var normalized = NormalizeAnalysisSettings(worker, key, settings);
                var view = ToAnalysisViewDto(worker.BuildAnalysisView(key, normalized, token));
                if (view.Outcome != AnalysisOutcome.Success)
                    throw new InvalidOperationException(view.OutcomeReason ?? view.ReportText);
                return view;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new InvalidOperationException($"结构 {index + 1}：{exception.Message}", exception);
            }
        }).ToArray();
        var first = views[0];
        if (views.Any(view => view.PlotPanes.Count != first.PlotPanes.Count || view.PlotPaneColumns != first.PlotPaneColumns))
            throw new InvalidOperationException("各结构的视场或绘图布局不同，无法叠加；请选择单个结构或使用结构矩阵点列图。");

        AnalysisSeriesDto[] OverlaySeries(Func<AnalysisViewDto, IReadOnlyList<AnalysisSeriesDto>> select)
        {
            var groups = views.Select(select).ToArray();
            var axes = groups.SelectMany(group => group).Where(series => series.Points.Count > 0)
                .Select(series => (series.XQuantity, series.XUnit, series.YQuantity, series.YUnit)).Distinct().ToArray();
            if (axes.Length > 1)
                throw new InvalidOperationException("各结构的坐标量或单位不同，无法在同一坐标轴叠加；请选择当前的或单个结构。");
            return groups.SelectMany((group, index) => group.Select(series => series with
            {
                Name = $"结构 {index + 1} · {series.Name}",
                LegendLabel = $"结构 {index + 1} · {(string.IsNullOrWhiteSpace(series.LegendLabel) ? series.Name : series.LegendLabel)}",
                ConfigurationNumber = index + 1,
                // Keep the wavelength color; line and point styles identify configurations.
                LineStyle = (AnalysisLineStyle)(index % 3),
                MarkerStyle = (AnalysisMarkerStyle)(index % Enum.GetValues<AnalysisMarkerStyle>().Length)
            })).ToArray();
        }

        var panes = first.PlotPanes.Select((pane, index) => pane with
        {
            Series = OverlaySeries(view => view.PlotPanes[index].Series),
            PlotOptions = OverlayOptions(views.Select(view => view.PlotPanes[index].PlotOptions)),
            Metrics = views.SelectMany((view, configuration) => (view.PlotPanes[index].Metrics ?? [])
                .Select(metric => metric with { Label = $"结构 {configuration + 1} · {metric.Label}" })).ToArray(),
            Footer = string.Join(Environment.NewLine, views.Select((view, configuration) =>
                $"结构 {configuration + 1} · {view.PlotPanes[index].Footer}"))
        }).ToArray();
        var rows = views.SelectMany((view, index) => view.Rows.Select(row =>
            row with { Metric = $"结构 {index + 1} · {row.Metric}" })).ToArray();
        AnalysisTableDto? table = null;
        if (views.All(view => view.Table is not null)
            && views.All(view => view.Table!.Columns.SequenceEqual(first.Table!.Columns)))
            table = new AnalysisTableDto(new[] { "结构" }.Concat(first.Table!.Columns).ToArray(),
                views.SelectMany((view, index) => view.Table!.Rows.Select(row =>
                    (IReadOnlyList<string>)new[] { (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) }.Concat(row).ToArray())).ToArray());
        return first with
        {
            Series = first.PlotPanes.Count == 0 ? OverlaySeries(view => view.Series) : panes.SelectMany(pane => pane.Series).ToArray(),
            PlotOptions = OverlayOptions(views.Select(view => view.PlotOptions)),
            PlotPanes = panes,
            Rows = rows,
            Table = table,
            ReportText = string.Join(Environment.NewLine + Environment.NewLine, views.Select((view, index) =>
                $"结构 {index + 1}{Environment.NewLine}{view.ReportText}")),
            PresentationKind = WorkbenchAnalysisCatalog.PresentationKind(key)
        };
    }

    private static AnalysisPlotOptionsDto OverlayOptions(IEnumerable<AnalysisPlotOptionsDto> source)
    {
        var options = source.ToArray();
        double? Minimum(Func<AnalysisPlotOptionsDto, double?> select) =>
            options.All(option => select(option).HasValue) ? options.Min(option => select(option)) : null;
        double? Maximum(Func<AnalysisPlotOptionsDto, double?> select) =>
            options.All(option => select(option).HasValue) ? options.Max(option => select(option)) : null;
        return options[0] with
        {
            XMinimum = Minimum(option => option.XMinimum), XMaximum = Maximum(option => option.XMaximum),
            YMinimum = Minimum(option => option.YMinimum), YMaximum = Maximum(option => option.YMaximum), ShowLegend = true
        };
    }

    private static AnalysisViewDto RemapSingleConfigurationMatrix(AnalysisViewDto view, int number) => view with
    {
        PlotPanes = view.PlotPanes.Select(pane => pane.MatrixCell is { } cell ? pane with
        {
            Title = $"结构 {number} · {cell.FieldLabel}",
            PlotOptions = pane.PlotOptions with { Title = $"结构 {number} · {cell.FieldLabel}" },
            MatrixCell = cell with { ConfigurationNumber = number }
        } : pane).ToArray()
    };

    private static AnalysisViewDto DescribeConfiguration(AnalysisViewDto view, AnalysisConfigurationScopeDto scope)
    {
        var label = scope.Selection == AnalysisConfigurationSelection.All
            ? $"所有（{scope.TotalCount}）"
            : scope.Selection == AnalysisConfigurationSelection.Current
                ? $"当前的（{scope.ConfigurationNumbers[0]} / {scope.TotalCount}）"
                : $"{scope.ConfigurationNumbers[0]} / {scope.TotalCount}";
        return view with
        {
            ConfigurationScope = scope,
            Series = view.Series.Select(series => series with
            {
                ConfigurationNumber = scope.ConfigurationNumbers.Count == 1 ? scope.ConfigurationNumbers[0] : series.ConfigurationNumber
            }).ToArray(),
            PlotPanes = view.PlotPanes.Select(pane => pane with
            {
                Series = pane.Series.Select(series => series with
                {
                    ConfigurationNumber = pane.MatrixCell?.ConfigurationNumber
                        ?? (scope.ConfigurationNumbers.Count == 1 ? scope.ConfigurationNumbers[0] : series.ConfigurationNumber)
                }).ToArray()
            }).ToArray(),
            Rows = new[] { new AnalysisRowDto("结构", label) }.Concat(view.Rows).ToArray(),
            ReportText = $"结构：{label}{Environment.NewLine}{view.ReportText}"
        };
    }

    private static OpticalDocumentSnapshot SourceSummary(OpticalDocumentSnapshot source, Optic optic) => source with
    {
        Name = optic.Name,
        EffectiveFocalLength = Computable(optic) ? optic.Paraxial.EstimateEffectiveFocalLength() : double.NaN,
        FNumber = Computable(optic) ? optic.Paraxial.EstimateFNumber() : double.NaN,
        ApertureValue = optic.Aperture.Value,
        TotalTrack = optic.SurfaceGroup.TotalTrack,
        SurfaceCount = optic.SurfaceGroup.Items.Count,
        FieldCount = optic.Fields.Count,
        WavelengthCount = optic.Wavelengths.Count,
        EntrancePupilDiameter = Computable(optic) ? optic.Paraxial.EstimateEntrancePupilDiameter() : double.NaN
    };

    private static bool Computable(Optic optic) =>
        Core.Capabilities.OpticCapabilityPreflight.Inspect(optic).Count == 0;
}
