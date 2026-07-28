using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed partial class AnalysisPanel
{
    private void RebuildParameterPanel()
    {
        _parameterPanel.Children.Clear();
        _parameterControls.Clear();
        var descriptors = _analyses.GetParameters(AnalysisName);
        if (string.Equals(AnalysisKey, "Full Field Spot Diagram", StringComparison.Ordinal))
        {
            _parameterPanel.Children.Add(BuildFullFieldSpotSettings(descriptors));
            return;
        }
        if (string.Equals(AnalysisKey, "Matrix Spot Diagram", StringComparison.Ordinal))
        {
            _parameterPanel.Children.Add(BuildMatrixSpotSettings(descriptors));
            return;
        }
        if (AnalysisKey is "Spot Diagram"
            or "Configuration Matrix Spot Diagram")
        {
            _parameterPanel.Children.Add(BuildSpotDiagramSettings(descriptors));
            return;
        }
        if (string.Equals(AnalysisKey, "Through Focus", StringComparison.Ordinal))
        {
            _parameterPanel.Children.Add(BuildThroughFocusSettings(descriptors));
            return;
        }
        if (string.Equals(AnalysisKey, "Angle vs Image Height", StringComparison.Ordinal))
        {
            _parameterPanel.Children.Add(BuildReferenceStyleSettings(
                descriptors,
                new[] { "FieldDensity" },
                new[] { "WavelengthNumber" }));
            return;
        }
        if (string.Equals(AnalysisKey, "Cardinal Points Data", StringComparison.Ordinal))
        {
            _parameterPanel.Children.Add(BuildReferenceStyleSettings(
                descriptors,
                new[] { "ReferenceSurfaceNumber" },
                Array.Empty<string?>()));
            return;
        }
        if (string.Equals(AnalysisKey, "Wavefront Map", StringComparison.Ordinal))
        {
            _parameterPanel.Children.Add(BuildWavefrontMapSettings(descriptors));
            return;
        }
        if (string.Equals(AnalysisKey, "PSF", StringComparison.Ordinal))
        {
            _parameterPanel.Children.Add(BuildReferenceStyleSettings(
                descriptors,
                new string?[]
                {
                    "Sampling",
                    "Display",
                    "Rotation",
                    "ImageDeltaMicrometers",
                    null,
                    "UsePolarization"
                },
                new[]
                {
                    "WavelengthNumber",
                    "FieldNumber",
                    "Type",
                    "DisplayAs",
                    "SurfaceNumber",
                    "Normalized"
                }));
            return;
        }
        if (string.Equals(AnalysisKey, "FFT PSF Cross Section", StringComparison.Ordinal))
        {
            _parameterPanel.Children.Add(BuildReferenceStyleSettings(
                descriptors,
                new[]
                {
                    "Sampling",
                    "Row",
                    "GraphScaleMicrometers",
                    "UsePolarization"
                },
                new[]
                {
                    "WavelengthNumber",
                    "FieldNumber",
                    "Type",
                    "Normalized"
                }));
            return;
        }
        if (string.Equals(AnalysisKey, "FFT Line Edge Spread", StringComparison.Ordinal))
        {
            _parameterPanel.Children.Add(BuildReferenceStyleSettings(
                descriptors,
                new[]
                {
                    "Sampling",
                    "Spread",
                    "GraphScaleMicrometers",
                    "UsePolarization"
                },
                new[]
                {
                    "WavelengthNumber",
                    "FieldNumber",
                    "Type",
                    "UseCoherentPsf"
                }));
            return;
        }
        if (string.Equals(AnalysisKey, "Huygens PSF", StringComparison.Ordinal))
        {
            _parameterPanel.Children.Add(BuildReferenceStyleSettings(
                descriptors,
                new[]
                {
                    "PupilSampling",
                    "ImageSampling",
                    "ImageDeltaMicrometers",
                    "Rotation",
                    "UsePolarization",
                    "UseCentroid"
                },
                new[]
                {
                    "WavelengthNumber",
                    "FieldNumber",
                    "Type",
                    "DisplayAs",
                    "Normalized"
                }));
            return;
        }
        if (string.Equals(AnalysisKey, "Foucault Analysis", StringComparison.Ordinal))
        {
            _parameterPanel.Children.Add(BuildReferenceStyleSettings(
                descriptors,
                new[] { "Sampling", "Type", "DisplayAs", "KnifeEdge", "DataSource" },
                new string?[]
                {
                    "WavelengthNumber",
                    "FieldNumber",
                    null,
                    "YPositionMicrometers",
                    "UsePolarization"
                }));
            return;
        }

        foreach (var descriptor in descriptors)
        {
            _parameterPanel.Children.Add(Label(descriptor.DisplayName));
            var value = _settings.TryGetValue(descriptor.Key, out var saved)
                ? saved
                : descriptor.DefaultValue;
            var control = CreateParameterControl(descriptor, value);
            _parameterControls[descriptor.Key] = control;
            _parameterPanel.Children.Add(control);
        }
    }

    private Control BuildSpotDiagramSettings(
        IReadOnlyList<AnalysisParameterDescriptor> descriptors)
    {
        var leftKeys = new[]
        {
            "RayDensity",
            "Pattern",
            "ColorRaysBy",
            "Reference",
            "UsePolarization",
            "DirectionCosines",
            "ShowAiryDisk"
        };
        var rightKeys = new[]
        {
            "WavelengthNumber",
            "FieldNumber",
            "SurfaceNumber",
            "DisplayScale",
            "PlotScaleMicrometers",
            "ScatterRays",
            "UseSymbols"
        };
        return BuildReferenceStyleSettings(descriptors, leftKeys, rightKeys);
    }

    private Control BuildFullFieldSpotSettings(
        IReadOnlyList<AnalysisParameterDescriptor> descriptors)
    {
        var leftKeys = new[]
        {
            "RayDensity",
            "Pattern",
            "ColorRaysBy",
            "Reference",
            "Magnification",
            "UsePolarization",
            "ShowAiryDisk"
        };
        var rightKeys = new[]
        {
            "WavelengthNumber",
            "FieldNumber",
            "SurfaceNumber",
            "DisplayScale",
            "PlotScaleMicrometers",
            "ScatterRays",
            "UseSymbols"
        };
        return BuildReferenceStyleSettings(descriptors, leftKeys, rightKeys);
    }

    private Control BuildMatrixSpotSettings(
        IReadOnlyList<AnalysisParameterDescriptor> descriptors)
    {
        string?[] leftKeys =
        {
            "RayDensity",
            "Pattern",
            "ColorRaysBy",
            "Reference",
            null,
            "UsePolarization",
            "DirectionCosines",
            "ShowAiryDisk"
        };
        string?[] rightKeys =
        {
            "WavelengthNumber",
            "FieldNumber",
            "SurfaceNumber",
            "DisplayScale",
            "PlotScaleMicrometers",
            "ScatterRays",
            "UseSymbols",
            "IgnoreLateralColor"
        };
        return BuildReferenceStyleSettings(descriptors, leftKeys, rightKeys);
    }

    private Control BuildThroughFocusSettings(
        IReadOnlyList<AnalysisParameterDescriptor> descriptors)
    {
        var leftKeys = new[]
        {
            "RayDensity",
            "Pattern",
            "ColorRaysBy",
            "Reference",
            "DefocusStepMicrometers",
            "UsePolarization",
            "ShowAiryDisk"
        };
        var rightKeys = new[]
        {
            "WavelengthNumber",
            "FieldNumber",
            "SurfaceNumber",
            "DisplayScale",
            "PlotScaleMicrometers",
            "ScatterRays",
            "UseSymbols"
        };
        return BuildReferenceStyleSettings(descriptors, leftKeys, rightKeys);
    }

    private Control BuildWavefrontMapSettings(
        IReadOnlyList<AnalysisParameterDescriptor> descriptors)
    {
        var byKey = descriptors.ToDictionary(descriptor => descriptor.Key);
        var leftKeys = new[]
        {
            "Sampling",
            "Rotation",
            "DisplayScale",
            "Apodization",
            "ReferenceChiefRay",
            "UseExitPupilShape"
        };
        var rightKeys = new[]
        {
            "WavelengthNumber",
            "FieldNumber",
            "SurfaceNumber",
            "DisplayAs",
            "RemoveTilt"
        };
        return BuildReferenceStyleSettings(
            descriptors,
            leftKeys,
            rightKeys,
            additionalRows: 1,
            addAdditionalRows: (grid, row) =>
            {
                var pupilGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,*,Auto,*"),
                    Margin = new Thickness(8, 0, 4, 0)
                };
                pupilGrid.Children.Add(new TextBlock
                {
                    Text = "子孔径数据 →",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 14, 0)
                });
                AddPupilSetting("PupilSx", "Sx:", 1, 2);
                AddPupilSetting("PupilSy", "Sy:", 3, 4);
                AddPupilSetting("PupilSr", "Sr:", 5, 6);
                Grid.SetRow(pupilGrid, row);
                Grid.SetColumnSpan(pupilGrid, 5);
                grid.Children.Add(pupilGrid);

                void AddPupilSetting(string key, string labelText, int labelColumn, int controlColumn)
                {
                    var descriptor = byKey[key];
                    var label = new TextBlock
                    {
                        Text = labelText,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0)
                    };
                    var value = _settings.TryGetValue(key, out var saved)
                        ? saved
                        : descriptor.DefaultValue;
                    var control = CreateParameterControl(descriptor, value);
                    control.HorizontalAlignment = HorizontalAlignment.Stretch;
                    control.Margin = new Thickness(2, 0, 8, 0);
                    _parameterControls[key] = control;
                    WireSpotAutoApply(control);
                    Grid.SetColumn(label, labelColumn);
                    Grid.SetColumn(control, controlColumn);
                    pupilGrid.Children.Add(label);
                    pupilGrid.Children.Add(control);
                }
            });
    }

    private Control BuildReferenceStyleSettings(
        IReadOnlyList<AnalysisParameterDescriptor> descriptors,
        IReadOnlyList<string?> leftKeys,
        IReadOnlyList<string?> rightKeys,
        int additionalRows = 0,
        Action<Grid, int>? addAdditionalRows = null)
    {
        var byKey = descriptors.ToDictionary(descriptor => descriptor.Key);
        var settingRowCount = Math.Max(leftKeys.Count, rightKeys.Count);
        var rowCount = settingRowCount + Math.Max(0, additionalRows);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,240,24,Auto,240"),
            RowDefinitions = new RowDefinitions(
                string.Join(',', Enumerable.Repeat("34", rowCount)) + ",Auto,42"),
            MinWidth = 780,
            MaxWidth = 960
        };

        for (var row = 0; row < settingRowCount; row++)
        {
            if (row < leftKeys.Count && leftKeys[row] is { } leftKey)
            {
                AddSpotSetting(grid, byKey[leftKey], row, 0, 1);
            }

            if (row < rightKeys.Count && rightKeys[row] is { } rightKey)
            {
                AddSpotSetting(grid, byKey[rightKey], row, 3, 4);
            }
        }
        addAdditionalRows?.Invoke(grid, settingRowCount);

        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(4, 8),
            VerticalAlignment = VerticalAlignment.Center
        };
        separator.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Border);
        Grid.SetRow(separator, rowCount);
        Grid.SetColumnSpan(separator, 5);
        grid.Children.Add(separator);

        var applyButton = new Button { Content = "应用", MinWidth = 86 };
        applyButton.Click += async (_, _) => await RunAsync();
        var okButton = new Button { Content = "确定", MinWidth = 86 };
        okButton.Click += async (_, _) =>
        {
            await RunAsync();
            _settingsHost.IsVisible = false;
        };
        var cancelButton = new Button { Content = "取消", MinWidth = 86 };
        cancelButton.Click += (_, _) =>
        {
            RebuildParameterPanel();
            _settingsHost.IsVisible = false;
        };
        var saveButton = new Button { Content = "保存", MinWidth = 86 };
        saveButton.Click += async (_, _) => await SaveSettingsPresetAsync();
        var loadButton = new Button { Content = "载入", MinWidth = 86 };
        loadButton.Click += async (_, _) => await LoadSettingsPresetAsync();
        var resetButton = new Button { Content = "重置", MinWidth = 86 };
        resetButton.Click += async (_, _) =>
        {
            _settings = _analyses.MergeSettings(AnalysisName, null);
            RebuildParameterPanel();
            if (_parameterAutoApply.IsChecked == true)
            {
                await RunAsync();
            }
        };

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Children =
            {
                _parameterAutoApply,
                new WrapPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Children = { applyButton, okButton, cancelButton }
                },
                new WrapPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { saveButton, loadButton, resetButton }
                }
            }
        };
        foreach (var button in new[] { applyButton, okButton, cancelButton, saveButton, loadButton, resetButton })
        {
            button.Margin = new Thickness(3, 4);
        }

        Grid.SetColumn(footer.Children[1], 1);
        Grid.SetColumn(footer.Children[2], 2);
        Grid.SetRow(footer, rowCount + 1);
        Grid.SetColumnSpan(footer, 5);
        grid.Children.Add(footer);
        return grid;
    }

    private void AddSpotSetting(
        Grid grid,
        AnalysisParameterDescriptor descriptor,
        int row,
        int labelColumn,
        int controlColumn)
    {
        var label = Label(descriptor.DisplayName + "：");
        label.Margin = new Thickness(8, 0, 8, 0);
        var value = _settings.TryGetValue(descriptor.Key, out var saved)
            ? saved
            : descriptor.DefaultValue;
        var control = CreateParameterControl(descriptor, value);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.Width = double.NaN;
        _parameterControls[descriptor.Key] = control;
        WireSpotAutoApply(control);

        Grid.SetRow(label, row);
        Grid.SetColumn(label, labelColumn);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, controlColumn);
        grid.Children.Add(label);
        grid.Children.Add(control);
    }

    private void WireSpotAutoApply(Control control)
    {
        void Schedule()
        {
            if (_parameterAutoApply.IsChecked != true || _disposed)
            {
                return;
            }

            _stateText.Text = "设置已更改，正在自动刷新…";
            _automaticRefreshTimer.Stop();
            _automaticRefreshTimer.Start();
        }

        switch (control)
        {
            case NumericUpDown numeric:
                numeric.ValueChanged += (_, _) => Schedule();
                break;
            case ComboBox combo:
                combo.SelectionChanged += (_, _) => Schedule();
                break;
            case CheckBox check:
                check.IsCheckedChanged += (_, _) => Schedule();
                break;
        }
    }

    private async Task SaveSettingsPresetAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
            {
                return;
            }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = $"保存{AnalysisName}设置",
                SuggestedFileName = string.Equals(AnalysisKey, "Through Focus", StringComparison.Ordinal)
                    ? "through-focus-spot-settings.json"
                    : "spot-diagram-settings.json",
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("分析设置") { Patterns = new[] { "*.json" } }
                }
            });
            if (file is null)
            {
                return;
            }

            var settings = CaptureParameterSettings();
            var json = System.Text.Json.JsonSerializer.Serialize(
                settings,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(file.Path.LocalPath, json);
            _stateText.Text = $"{AnalysisName}设置已保存";
        }
        catch (Exception exception)
        {
            _stateText.Text = $"保存设置失败：{exception.Message}";
        }
    }

    private async Task LoadSettingsPresetAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
            {
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"载入{AnalysisName}设置",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("分析设置") { Patterns = new[] { "*.json" } }
                }
            });
            if (files.Count == 0)
            {
                return;
            }

            var json = await File.ReadAllTextAsync(files[0].Path.LocalPath);
            var saved = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
            _settings = _analyses.MergeSettings(AnalysisName, saved);
            RebuildParameterPanel();
            if (_parameterAutoApply.IsChecked == true)
            {
                await RunAsync();
            }
            else
            {
                _stateText.Text = $"{AnalysisName}设置已载入";
            }
        }
        catch (Exception exception)
        {
            _stateText.Text = $"载入设置失败：{exception.Message}";
        }
    }

    private Dictionary<string, string> CaptureParameterSettings()
    {
        var settings = _analyses.MergeSettings(AnalysisName, null);
        foreach (var descriptor in _analyses.GetParameters(AnalysisName))
        {
            if (!_parameterControls.TryGetValue(descriptor.Key, out var control))
            {
                continue;
            }

            settings[descriptor.Key] = control switch
            {
                NumericUpDown numeric when numeric.Value.HasValue =>
                    numeric.Value.Value.ToString(CultureInfo.InvariantCulture),
                ComboBox combo when combo.SelectedItem is string selected => selected,
                CheckBox check => (check.IsChecked == true).ToString(CultureInfo.InvariantCulture),
                _ => settings[descriptor.Key]
            };
        }

        return settings;
    }

    private IReadOnlyDictionary<string, string>? SavedAnalysisSettings()
    {
        return _appSettings.AnalysisSettings.TryGetValue(AnalysisKey, out var settings)
            ? settings
            : null;
    }

    private void SaveAnalysisSettings()
    {
        if (_settings.Count == 0)
        {
            _appSettings.AnalysisSettings.Remove(AnalysisKey);
        }
        else
        {
            _appSettings.AnalysisSettings[AnalysisKey] = new Dictionary<string, string>(_settings);
        }

        _appSettings.Save();
    }

    private static Control CreateParameterControl(AnalysisParameterDescriptor descriptor, string value)
    {
        return descriptor.Kind switch
        {
            AnalysisParameterKind.Choice => ChoiceInput(descriptor, value),
            AnalysisParameterKind.Boolean => BooleanInput(value),
            _ => NumericInput(descriptor, value)
        };
    }

    private static NumericUpDown NumericInput(AnalysisParameterDescriptor descriptor, string value)
    {
        var input = new NumericUpDown
        {
            Minimum = (decimal)descriptor.Minimum,
            Maximum = (decimal)descriptor.Maximum,
            Increment = (decimal)descriptor.Increment,
            Width = descriptor.Kind == AnalysisParameterKind.Double ? 108 : 92,
            ShowButtonSpinner = false
        };
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            input.Value = Math.Clamp(parsed, input.Minimum, input.Maximum);
        }
        else if (decimal.TryParse(descriptor.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var fallback))
        {
            input.Value = Math.Clamp(fallback, input.Minimum, input.Maximum);
        }

        return input;
    }

    private static ComboBox ChoiceInput(AnalysisParameterDescriptor descriptor, string value)
    {
        var choices = descriptor.Choices?.ToArray() ?? Array.Empty<string>();
        return new ComboBox
        {
            ItemsSource = choices,
            SelectedItem = choices.Contains(value) ? value : descriptor.DefaultValue,
            MinWidth = 104
        };
    }

    private static CheckBox BooleanInput(string value) => new()
    {
        IsChecked = bool.TryParse(value, out var flag) && flag,
        VerticalAlignment = VerticalAlignment.Center
    };
}
