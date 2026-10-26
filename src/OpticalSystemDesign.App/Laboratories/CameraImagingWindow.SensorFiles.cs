using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    private CameraResponseCurve? _spectralResponse;
    private CameraResponseCurve? _plottedResponse;
    private CameraSensorIdentity? _sensorIdentity;
    private bool _applyingSensor;
    private int _sensorFileLoadSerial;
    private readonly TextBlock _sensorProfileInfo = new() { Name = "camera-sensor-profile-info", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _responseInfo = new() { Name = "camera-response-info", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _responsePageInfo = new() { TextWrapping = TextWrapping.Wrap };
    private readonly AnalysisPlotControl _responsePlot = new() { Name = "camera-response-plot", Height = 340, IsVisible = false };
    private readonly StackPanel _responseLegend = new()
    {
        Name = "camera-response-legend",
        Spacing = 2,
        IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Avalonia.Thickness(ResponsivePlotLegendGrid.LegendInset)
    };
    private readonly Button _clearResponse = new() { Name = "camera-clear-response", Content = "移除响应曲线" };
    internal CameraResponseCurve? SpectralResponse => _spectralResponse;

    private Control BuildSensorFilesCard()
    {
        var importSensor = new Button { Name = "camera-import-sensor", Content = "导入传感器" };
        var saveSensor = new Button { Name = "camera-save-sensor", Content = "保存参数/模板" };
        var importResponse = new Button { Name = "camera-import-response", Content = "导入 QE 曲线" };
        importSensor.Click += async (_, _) =>
        {
            var path = await PickSensorFileAsync("导入传感器参数", "*.json");
            if (path is not null) await ImportSensorFromPathAsync(path);
        };
        importResponse.Click += async (_, _) =>
        {
            var path = await PickSensorFileAsync("导入光谱响应 CSV", "*.csv");
            if (path is not null) await ImportResponseFromPathAsync(path);
        };
        saveSensor.Click += async (_, _) => await SaveSensorProfileAsync();
        _clearResponse.Click += (_, _) => ClearSpectralResponse();
        return Group("传感器文件与响应", new Control[]
        {
            LaboratoryPresentation.Commands(importSensor, saveSensor, importResponse, _clearResponse),
            _sensorProfileInfo, _responseInfo,
            LaboratoryPresentation.Hint("JSON 保存当前传感器参数；CSV 使用绝对 QE，支持比例或明确标注的百分数。导入来源不代表已核实测量。")
        });
    }

    private Control BuildResponsePage() => Scroll(new StackPanel
    {
        Spacing = 12,
        Children =
        {
            LaboratoryPresentation.Card(new Border { Padding = new Avalonia.Thickness(12), Child = _responsePageInfo }),
            LaboratoryPresentation.Card(new ResponsivePlotLegendGrid(_responsePlot, _responseLegend)),
            LaboratoryPresentation.Hint("红/绿/蓝线分别为 R/G/B CFA 的绝对总 QE（含滤色片），固定纵轴 0–1。线性插值，禁止外推和峰值归一化。\n"
                + "三波段模式在 650/550/450 nm 取样；连续模式在指定范围积分光源 × 反射率 × QE。导入曲线替代固定 QE。\n"
                + "CSV 表头：wavelength_nm,qe_r,qe_g,qe_b；百分数表头：wavelength_nm,qe_r_percent,qe_g_percent,qe_b_percent。\n"
                + "需要 2–4096 点，波长严格递增。三波段模式需覆盖 450–650 nm；连续模式需覆盖整个积分范围。QE 为比例 0–1 或百分数 0–100；不将相对响应、A/W 或未知单位解释为 QE。")
        }
    });

    internal async Task<bool> ImportSensorFromPathAsync(string path)
    {
        var generation = _generation; var serial = ++_sensorFileLoadSerial;
        try
        {
            var profile = await _application.CameraImaging.ImportSensorAsync(path);
            if (_closed || generation != _generation || serial != _sensorFileLoadSerial) return false;
            _applyingSensor = true;
            try
            {
                var s = profile.Sensor;
                foreach (var (key, value) in new (string, double)[]
                {
                    ("exposure", s.ExposureMilliseconds), ("pitch", s.PixelPitchMicrometers), ("irradiance", s.ReferenceIrradiance),
                    ("qe-r", s.RedQe), ("qe-g", s.GreenQe), ("qe-b", s.BlueQe), ("full-well", s.FullWellElectrons),
                    ("read", s.ReadNoiseElectrons), ("dark", s.DarkElectronsPerSecond), ("prnu", s.PrnuPercent),
                    ("dsnu", s.DsnuElectrons), ("gain", s.GainDnPerElectron), ("black", s.BlackLevel), ("bits", s.BitDepth), ("seed", s.Seed)
                }) _numbers[key].Value = (decimal)value;
                _noise.IsChecked = s.NoiseEnabled; _bayer.SelectedIndex = (int)s.Pattern;
                _spectralResponse = profile.SpectralResponse;
                _sensorIdentity = new(profile.Name, profile.Source, profile.Origin);
            }
            finally { _applyingSensor = false; }
            Invalidate("传感器参数已导入；A 保留原参数，请运行新的 B。"); return true;
        }
        catch (Exception ex)
        { if (!_closed) _status.Text = "参数导入失败，现有参数与 A/B 保留：" + ex.Message; return false; }
    }

    internal async Task<bool> ImportResponseFromPathAsync(string path)
    {
        var generation = _generation; var serial = ++_sensorFileLoadSerial;
        try
        {
            var response = await _application.CameraImaging.ImportResponseAsync(path);
            if (_closed || generation != _generation || serial != _sensorFileLoadSerial) return false;
            _spectralResponse = response; MarkSensorModified();
            Invalidate("光谱响应已导入并替代固定 QE；A 保留原响应，请运行新的 B。"); return true;
        }
        catch (Exception ex)
        { if (!_closed) _status.Text = "响应导入失败，现有曲线与 A/B 保留：" + ex.Message; return false; }
    }

    internal void ClearSpectralResponse()
    {
        if (_spectralResponse is null) return;
        _spectralResponse = null; MarkSensorModified(); Invalidate("已恢复固定三波段 QE；A 对照保留。");
    }

    private async Task SaveSensorProfileAsync()
    {
        try
        {
            var profile = new CameraSensorProfile(CameraSensorProfile.FormatIdentifier, 1,
                _sensorIdentity?.Name ?? "Generic laboratory sensor", _sensorIdentity?.Source ?? "User configured, not calibrated",
                ReadSensor(), _spectralResponse);
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存传感器参数/模板",
                SuggestedFileName = "sensor.camera-sensor.json",
                DefaultExtension = "json",
                FileTypeChoices = new[] { new FilePickerFileType("传感器 JSON") { Patterns = new[] { "*.json" } } }
            });
            if (file is null || _closed) return;
            await _application.CameraImaging.SaveSensorAsync(profile, file.Path.LocalPath);
            if (!_closed) _status.Text = "传感器参数及当前响应曲线已保存；可修改 JSON 或在另一轮导入。";
        }
        catch (Exception ex) { if (!_closed) _status.Text = "参数保存失败：" + ex.Message; }
    }

    private async Task<string?> PickSensorFileAsync(string title, string pattern)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = title, AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType(title) { Patterns = new[] { pattern } } } });
        return _closed || files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    private CameraSensorParameters ReadSensor() => new(V("exposure"), V("pitch"), V("irradiance"), V("qe-r"), V("qe-g"), V("qe-b"),
        V("full-well"), V("read"), V("dark"), V("prnu"), V("dsnu"), V("gain"), (int)V("black"), (int)V("bits"),
        _noise.IsChecked == true, (int)V("seed"), (CameraBayerPattern)_bayer.SelectedIndex);

    private void MarkSensorModified()
    { if (_sensorIdentity is not null) _sensorIdentity = _sensorIdentity with { Modified = true }; }

    private void UpdateSensorFiles()
    {
        _sensorProfileInfo.Text = _sensorIdentity is null ? "通用手动参数 · 尚未导入传感器文件"
            : $"{_sensorIdentity.Name}{(_sensorIdentity.Modified ? "（参数已修改）" : "")}\n{_sensorIdentity.Source}\n{OriginText(_sensorIdentity.Origin)}";
        _responseInfo.Text = _spectralResponse is null ? "响应：固定三波段 QE"
            : $"响应：{_spectralResponse.Name} · {_spectralResponse.Samples.Count} 点\n{OriginText(_spectralResponse.Origin)}";
        _responsePageInfo.Text = _spectralResponse is null ? "尚未导入 QE 曲线。可先保存参数模板，或从左侧导入响应 CSV。"
            : $"{_spectralResponse.Name}\n{_spectralResponse.Source}\n{OriginText(_spectralResponse.Origin)}";
        foreach (var key in new[] { "qe-r", "qe-g", "qe-b" })
            if (_numbers.TryGetValue(key, out var control)) ControlAvailability.Set(control, _spectralResponse is null && !IsContinuous,
                IsContinuous ? "连续光谱使用绝对 QE 曲线；固定 QE 在三波段模式中可用。" : "已使用导入的绝对 QE 曲线；移除曲线后可编辑固定 QE。");
        if (_numbers.TryGetValue("irradiance", out var irradiance)) ControlAvailability.Set(irradiance, !IsContinuous,
            "连续光谱使用光源 CSV 的绝对 W/m²/nm；每波段辐照度仅用于三波段模式。");
        ControlAvailability.Set(_clearResponse, _spectralResponse is not null, "当前没有导入的响应曲线。");
        if (ReferenceEquals(_plottedResponse, _spectralResponse)) return;
        _plottedResponse = _spectralResponse; _responsePlot.IsVisible = _responseLegend.IsVisible = _spectralResponse is not null;
        _responseLegend.Children.Clear();
        if (_spectralResponse is null) { _responsePlot.Series = Array.Empty<AnalysisSeriesDto>(); return; }
        var samples = _spectralResponse.Samples;
        _responsePlot.PlotOptions = new(Title: "光谱响应 · 总量子效率", XMinimum: samples[0].WavelengthNanometers,
            XMaximum: samples[^1].WavelengthNanometers, YMinimum: 0, YMaximum: 1, ShowLegend: false);
        var series = Enumerable.Range(0, 3).Select(c => new AnalysisSeriesDto("波长 (nm)", "绝对 QE (0–1)",
            samples.Select(s => new AnalysisPointDto(s.WavelengthNanometers, c == 0 ? s.RedQe : c == 1 ? s.GreenQe : s.BlueQe)).ToArray(),
            Name: new[] { "R QE", "G QE", "B QE" }[c], ColorIndex: new[] { 3, 2, 0 }[c],
            XQuantity: AnalysisAxisQuantity.Wavelength, XUnit: AnalysisAxisUnit.Nanometer,
            YQuantity: AnalysisAxisQuantity.Probability, YUnit: AnalysisAxisUnit.Dimensionless)).ToArray();
        _responsePlot.Series = series;
        var enabled = new[] { true, true, true };
        for (var c = 0; c < series.Length; c++)
        {
            var index = c;
            var check = new CheckBox
            {
                Name = "camera-response-visible-" + c,
                IsChecked = true,
                Margin = new Avalonia.Thickness(2, 1),
                Content = new TextBlock
                {
                    Text = series[c].Name,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(AnalysisPlotControl.SeriesColor(series[c]))
                }
            };
            check.IsCheckedChanged += (_, _) =>
            {
                enabled[index] = check.IsChecked == true;
                _responsePlot.SetVisibleSeries(series.Where((_, i) => enabled[i]).ToArray());
            };
            _responseLegend.Children.Add(check);
        }
    }
    private static string OriginText(CameraFileOrigin? origin) => origin is null ? "来源未附文件指纹"
        : $"{origin.FileName} · SHA-256 {origin.Sha256[..Math.Min(12, origin.Sha256.Length)]}…";
}
