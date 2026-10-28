using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    private readonly ComboBox _spectralModel = Choice("camera-spectral-model", "三波段模型", "连续光谱积分");
    private CameraSpectrumCurve? _illuminant, _reflectance, _plottedIlluminant, _plottedReflectance;
    private readonly TextBlock _sceneSpectraInfo = new() { Name = "camera-scene-spectra-info", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _scenePageInfo = new() { TextWrapping = TextWrapping.Wrap };
    private readonly AnalysisPlotControl _illuminantPlot = new() { Name = "camera-illuminant-plot", Height = 270, IsVisible = false };
    private readonly AnalysisPlotControl _reflectancePlot = new() { Name = "camera-reflectance-plot", Height = 270, IsVisible = false };
    private readonly StackPanel _illuminantLegend = SpectrumLegend(), _reflectanceLegend = SpectrumLegend();
    private bool IsContinuous => _spectralModel.SelectedIndex == (int)CameraSpectralModel.Continuous;

    private Control BuildSceneSpectraCard()
    {
        var light = new Button { Name = "camera-import-illuminant", Content = "导入光源光谱" };
        var material = new Button { Name = "camera-import-reflectance", Content = "导入场景反射率" };
        light.Click += async (_, _) => { var path = await PickSensorFileAsync("导入光源光谱 CSV", "*.csv"); if (path is not null) await ImportSceneSpectrumFromPathAsync(path, true); };
        material.Click += async (_, _) => { var path = await PickSensorFileAsync("导入场景反射率 CSV", "*.csv"); if (path is not null) await ImportSceneSpectrumFromPathAsync(path, false); };
        return Group("光源与反射率", new Control[]
        {
            LaboratoryPresentation.Commands(light, material), _sceneSpectraInfo,
            Number("spectrum-min", "积分起点 (nm)", 400, 200, 19999, 10),
            Number("spectrum-max", "积分终点 (nm)", 700, 201, 20000, 10),
            Number("spectrum-step", "最大积分间隔 (nm)", 100, 1, 1000, 10),
            LaboratoryPresentation.Hint("连续模式需光源、反射率和 QE 覆盖整个积分范围。光源为理想白目标的像面标定光谱 W/m²/nm，不从瓦数或 lux 换算。图片作为灰度空间图样，可在下方选择单材质或标签分区。切换后运行 B，A 保留。")
        });
    }
    private static StackPanel SpectrumLegend() => new()
    {
        Spacing = 2,
        IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(ResponsivePlotLegendGrid.LegendInset)
    };
    private Control BuildSceneSpectraPage() => Scroll(new StackPanel
    {
        Spacing = 12,
        Children =
        {
            LaboratoryPresentation.Card(new Border { Padding = new Thickness(12), Child = _scenePageInfo }),
            _materialMapView,
            LaboratoryPresentation.Card(new ResponsivePlotLegendGrid(_illuminantPlot, _illuminantLegend)),
            LaboratoryPresentation.Card(new ResponsivePlotLegendGrid(_reflectancePlot, _reflectanceLegend)),
            LaboratoryPresentation.Hint("无峰值归一化、无外推；积分保留所有曲线折点，并按最大间隔细分，每段使用 3 点 Gauss-Legendre。\n"
                + "连续模式：输入转线性灰度；标签分区在源平面施加不同反射谱，再经过一次逐波长光学传播，允许跨界模糊。光学预览为反射能量 / 输入总能量，最终图为 CFA + 基础 ISP 结果，不是色度学真彩色标定。\n"
                + "理想光学最多 36864 积分节点，先精确汇总光谱系数；当前镜头最多 32 节点并受共享光学总计算预算限制。可降低分辨率、减少数据折点/范围，或增大积分间隔。")
        }
    });

    internal async Task<bool> ImportSceneSpectrumFromPathAsync(string path, bool illuminant)
    {
        var generation = _generation; var serial = ++_sensorFileLoadSerial;
        try
        {
            var curve = illuminant ? await _application.CameraImaging.ImportIlluminantAsync(path)
                : await _application.CameraImaging.ImportReflectanceAsync(path);
            if (_closed || generation != _generation || serial != _sensorFileLoadSerial) return false;
            if (illuminant) _illuminant = curve; else _reflectance = curve;
            Invalidate("光谱已导入；A 保留。选择连续光谱积分后运行新的 B。"); return true;
        }
        catch (Exception ex) { if (!_closed) _status.Text = "光谱导入失败，现有数据与 A/B 保留：" + ex.Message; return false; }
    }

    private string? SpectrumReadiness()
    {
        if (!IsContinuous) return _spectralResponse is not null
            && (_spectralResponse.Samples[0].WavelengthNanometers > 450 || _spectralResponse.Samples[^1].WavelengthNanometers < 650)
            ? "三波段模式需要 QE 覆盖 450–650 nm；此曲线可用于其覆盖范围内的连续光谱。" : null;
        if (_illuminant is null || _spectralResponse is null) return "连续光谱需要导入光源和绝对 QE 曲线。";
        var reflectanceCurves = IsMaterialMode ? _materialScene?.Materials.Select(m => m.Reflectance).ToArray() : new[] { _reflectance };
        if (reflectanceCurves is null || reflectanceCurves.Any(c => c is null)) return "请导入材质场景并为每个材质指定反射率，或使用单材质反射率。";
        if (_numbers["spectrum-min"].Value is not { } start || _numbers["spectrum-max"].Value is not { } end
            || _numbers["spectrum-step"].Value is null) return "请输入有效积分范围和最大间隔。";
        var min = (double)start; var max = (double)end;
        if (min >= max) return "积分起点必须小于终点。";
        if (_illuminant.Samples[0].WavelengthNanometers > min || _illuminant.Samples[^1].WavelengthNanometers < max
            || reflectanceCurves.Any(curve => curve!.Samples[0].WavelengthNanometers > min || curve.Samples[^1].WavelengthNanometers < max)
            || _spectralResponse.Samples[0].WavelengthNanometers > min || _spectralResponse.Samples[^1].WavelengthNanometers < max)
            return "光源、反射率及 QE 必须覆盖完整积分范围；不自动截短或外推。";
        return null;
    }
    private void UpdateSceneSpectra()
    {
        var info = "光源：" + CurveInfo(_illuminant) + "\n反射率：" + (IsMaterialMode ? MaterialSceneSummary() : CurveInfo(_reflectance));
        _sceneSpectraInfo.Text = info; _scenePageInfo.Text = (IsContinuous ? "当前：连续光谱积分" : "当前：三波段模型（以下曲线暂不参与计算）") + "\n" + info;
        if (!ReferenceEquals(_plottedIlluminant, _illuminant))
        { _plottedIlluminant = _illuminant; PlotSpectrum(_illuminant, _illuminantPlot, _illuminantLegend); }
        if (IsMaterialMode)
        {
            if (!ReferenceEquals(_plottedMaterialScene, _materialScene) || _plottedReflectance is not null)
            { _plottedMaterialScene = _materialScene; _plottedReflectance = null; PlotMaterialReflectances(); }
        }
        else if (!ReferenceEquals(_plottedReflectance, _reflectance) || _plottedMaterialScene is not null)
        { _plottedMaterialScene = null; _plottedReflectance = _reflectance; PlotSpectrum(_reflectance, _reflectancePlot, _reflectanceLegend); }
    }
    private static string CurveInfo(CameraSpectrumCurve? curve) => curve is null ? "尚未导入" : $"{curve.Name} · {curve.Samples.Count} 点\n{OriginText(curve.Origin)}";
    private static void PlotSpectrum(CameraSpectrumCurve? curve, AnalysisPlotControl plot, StackPanel legend)
    {
        plot.IsVisible = legend.IsVisible = curve is not null; legend.Children.Clear();
        if (curve is null) { plot.Series = Array.Empty<AnalysisSeriesDto>(); return; }
        var light = curve.Kind == CameraSpectrumKind.Illuminant;
        plot.PlotOptions = new(Title: light ? "光源 · 绝对光谱辐照度" : "场景材质 · 反射率",
            XMinimum: curve.Samples[0].WavelengthNanometers, XMaximum: curve.Samples[^1].WavelengthNanometers,
            YMinimum: 0, YMaximum: light ? Math.Max(1e-12, curve.Samples.Max(s => s.Value) * 1.05) : 1, ShowLegend: false);
        var series = new[] { new AnalysisSeriesDto("波长", light ? "光谱辐照度" : "反射率",
            curve.Samples.Select(s => new AnalysisPointDto(s.WavelengthNanometers, s.Value)).ToArray(),
            Name: light ? "光源" : "反射率", ColorIndex: light ? 6 : 2,
            XQuantity: AnalysisAxisQuantity.Wavelength, XUnit: AnalysisAxisUnit.Nanometer,
            YQuantity: light ? AnalysisAxisQuantity.Irradiance : AnalysisAxisQuantity.Probability,
            YUnit: light ? AnalysisAxisUnit.WattsPerSquareMeterPerNanometer : AnalysisAxisUnit.Dimensionless) };
        plot.Series = series;
        var check = new CheckBox { Name = light ? "camera-illuminant-visible" : "camera-reflectance-visible", IsChecked = true, Content = series[0].Name };
        check.IsCheckedChanged += (_, _) => plot.SetVisibleSeries(check.IsChecked == true ? series : Array.Empty<AnalysisSeriesDto>());
        legend.Children.Add(check);
    }
    private static string SpectrumDescription(CameraImagingResult result) => result.Integration is not { } integration
        ? "三波段模型 · 650/550/450 nm"
        : $"连续光谱 · {result.Request.Integration!.MinimumNanometers:G5}–{result.Request.Integration.MaximumNanometers:G5} nm · {integration.Nodes.Count} 节点 · "
            + $"光源 {result.Request.Illuminant!.Name} · 反射率 {(integration.Materials is null ? result.Request.Reflectance!.Name : result.Request.MaterialScene!.Name + "（" + integration.Materials.Count + " 材质）")} · "
            + $"输入 {integration.IlluminantIrradianceWattsPerSquareMeter:G5} W/m² · {(integration.Materials is null ? "反射后" : "分区面积平均，不含灰度/光学")} {integration.ReflectedIrradianceWattsPerSquareMeter:G5} W/m²";
}
