using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
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
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed partial class AnalysisPanel
{
    private static readonly System.Text.Json.JsonSerializerOptions SettingsJsonOptions = new()
    {
        WriteIndented = true
    };

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
        if (string.Equals(AnalysisKey, "Image Simulation", StringComparison.Ordinal))
        {
            _parameterPanel.Children.Add(BuildImageSimulationSettings(descriptors));
            return;
        }

        _parameterPanel.Children.Add(BuildAutomaticTwoColumnSettings(descriptors));
    }

    private Control BuildAutomaticTwoColumnSettings(
        IReadOnlyList<AnalysisParameterDescriptor> descriptors)
    {
        var keys = descriptors.Select(descriptor => descriptor.Key).ToArray();
        var split = (keys.Length + 1) / 2;
        return BuildReferenceStyleSettings(
            descriptors,
            keys.Take(split).ToArray(),
            keys.Skip(split).ToArray());
    }

    private Control BuildImageSimulationSettings(
        IReadOnlyList<AnalysisParameterDescriptor> descriptors)
    {
        var byKey = descriptors.ToDictionary(descriptor => descriptor.Key);
        var panel = new StackPanel
        {
            Spacing = 4
        };
        AddSection(
            "源位图设置",
            new[] { "SourceFile", "FieldHeight", "SourceFlip", "SourceRotation" },
            new[] { "Oversampling", "GuardBand", "WavelengthNumber", "FieldNumber" });
        AddSection(
            "网格卷积设置",
            new[] { "NumRays", "PsfGridColumns", "UsePolarization", "ApplyFixedApertures" },
            new[] { "PsfSize", "PsfGridRows", "AberrationMode", "RelativeIllumination" });
        AddSection(
            "探测器和显示设置",
            new string?[] { "DisplayAs", "Reference", "ImageFlip", "CompressFrame", "OutputFile" },
            new string?[] { "PixelSize", "DetectorXPixels", "DetectorYPixels", null, null });

        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(4, 8, 4, 2)
        };
        separator.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Border);
        panel.Children.Add(separator);
        panel.Children.Add(BuildImageSimulationFooter());
        return panel;

        void AddSection(
            string title,
            IReadOnlyList<string?> leftKeys,
            IReadOnlyList<string?> rightKeys)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"----- {title} -----",
                FontSize = DisplayTypography.CardTitle,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 3)
            });
            var rows = Math.Max(leftKeys.Count, rightKeys.Count);
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,10,Auto,*"),
                RowDefinitions = new RowDefinitions(
                    string.Join(',', Enumerable.Repeat("34", rows)))
            };
            for (var row = 0; row < rows; row++)
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

            panel.Children.Add(grid);
        }
    }

    private Control BuildImageSimulationFooter()
    {
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
        foreach (var button in new[] { applyButton, okButton, cancelButton, saveButton, loadButton, resetButton })
        {
            button.Margin = new Thickness(3, 4);
        }

        var footer = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _parameterAutoApply,
                applyButton,
                okButton,
                cancelButton,
                saveButton,
                loadButton,
                resetButton
            }
        };
        _parameterAutoApply.Margin = new Thickness(3, 8, 8, 4);
        return footer;
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
                var pupilGrid = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(8, 0, 4, 0)
                };
                pupilGrid.Children.Add(new TextBlock
                {
                    Text = "子孔径数据 →",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 14, 0)
                });
                AddPupilSetting("PupilSx", "Sx:");
                AddPupilSetting("PupilSy", "Sy:");
                AddPupilSetting("PupilSr", "Sr:");
                Grid.SetRow(pupilGrid, row);
                Grid.SetColumnSpan(pupilGrid, 5);
                grid.Children.Add(pupilGrid);

                void AddPupilSetting(string key, string labelText)
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
                    control.Margin = new Thickness(2, 0, 10, 0);
                    _parameterControls[key] = control;
                    WireSpotAutoApply(control);
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
        var rowDefinitions = rowCount == 0
            ? "Auto,42"
            : string.Join(',', Enumerable.Repeat("34", rowCount)) + ",Auto,42";
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,10,Auto,*"),
            RowDefinitions = new RowDefinitions(rowDefinitions)
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

        var footer = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _parameterAutoApply,
                applyButton,
                okButton,
                cancelButton,
                saveButton,
                loadButton,
                resetButton
            }
        };
        foreach (var button in new[] { applyButton, okButton, cancelButton, saveButton, loadButton, resetButton })
        {
            button.Margin = new Thickness(3, 4);
        }

        _parameterAutoApply.Margin = new Thickness(3, 8, 8, 4);
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

            _operationStatus.MarkStale("设置已更改，正在自动刷新…");
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
            case FilePathInput file:
                file.Input.TextChanged += (_, _) => Schedule();
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
                SettingsJsonOptions);
            await BoundedApplicationFile.WriteAllTextAtomicAsync(
                file.Path.LocalPath,
                json,
                BoundedApplicationFile.MaximumSettingsBytes,
                "Analysis settings");
            _operationStatus.MarkSynced($"{AnalysisName}设置已保存");
        }
        catch (Exception exception)
        {
            _operationStatus.MarkFailed($"保存设置失败：{exception.Message}");
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

            var json = await BoundedApplicationFile.ReadAllTextAsync(
                files[0].Path.LocalPath,
                BoundedApplicationFile.MaximumSettingsBytes,
                "Analysis settings");
            var saved = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                    json,
                    SettingsJsonOptions)
                ?? new Dictionary<string, string>();
            _settings = _analyses.MergeSettings(AnalysisName, saved);
            RebuildParameterPanel();
            if (_parameterAutoApply.IsChecked == true)
            {
                await RunAsync();
            }
            else
            {
                _operationStatus.MarkSynced($"{AnalysisName}设置已载入");
            }
        }
        catch (Exception exception)
        {
            _operationStatus.MarkFailed($"载入设置失败：{exception.Message}");
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
                FilePathInput file => file.Value,
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

    private Control CreateParameterControl(AnalysisParameterDescriptor descriptor, string value)
    {
        Control control = descriptor.Kind switch
        {
            AnalysisParameterKind.Choice => ChoiceInput(descriptor, value),
            AnalysisParameterKind.Boolean => BooleanInput(value),
            AnalysisParameterKind.File when descriptor.Key == "OutputFile" =>
                new FilePathInput(value, SelectImageOutputFileAsync, "选择输出 BMP、PNG 或 JPEG 文件"),
            AnalysisParameterKind.File => FileInput(value),
            _ => NumericInput(descriptor, value)
        };
        var automationTarget = control is FilePathInput file ? file.Input : control;
        AutomationProperties.SetName(automationTarget, descriptor.DisplayName);
        AutomationProperties.SetAutomationId(automationTarget, $"analysis-parameter-{descriptor.Key}");
        return control;
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
    private FilePathInput FileInput(string value) =>
        new(value, SelectImageFileAsync);

    private async Task<string?> SelectImageFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "\u9009\u62E9\u56FE\u50CF\u6A21\u62DF\u4F4D\u56FE",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("\u4F4D\u56FE")
                {
                    Patterns = new[] { "*.bmp", "*.png", "*.jpg", "*.jpeg" }
                }
            }
        });
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    private async Task<string?> SelectImageOutputFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "选择图像模拟输出文件",
            SuggestedFileName = "image-simulation.png",
            DefaultExtension = "png",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("位图图像")
                {
                    Patterns = new[] { "*.png", "*.bmp", "*.jpg", "*.jpeg" }
                }
            }
        });
        return file?.Path.LocalPath;
    }

    private sealed class FilePathInput : Grid
    {
        internal FilePathInput(
            string value,
            Func<Task<string?>> browse,
            string placeholder = "选择 BMP、PNG 或 JPEG 图像")
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto");
            Input = new TextBox
            {
                Text = value,
                MinWidth = 0,
                PlaceholderText = placeholder
            };
            var button = new Button
            {
                Content = "\u6D4F\u89C8\u2026",
                Margin = new Thickness(6, 0, 0, 0)
            };
            AutomationProperties.SetName(button, "浏览文件");
            button.Click += async (_, _) =>
            {
                var selected = await browse();
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    Input.Text = selected;
                }
            };
            Children.Add(Input);
            Grid.SetColumn(button, 1);
            Children.Add(button);
        }

        internal TextBox Input { get; }

        internal string Value => Input.Text?.Trim() ?? string.Empty;
    }

}
