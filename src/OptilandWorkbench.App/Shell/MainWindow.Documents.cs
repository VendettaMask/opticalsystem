using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Manufacturing;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App;

public sealed partial class MainWindow
{
    private async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开光学系统",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                NativeOpticFileType,
                LegacyOpticJsonFileType,
                PythonOptilandJsonFileType,
                CommercialOpticFileType,
                PlainSequentialFileType
            }
        });
        if (files.Count > 0)
        {
            await _panels.SaveCurrentSessionAsync();
            await _application.Documents.OpenAsync(files[0].Path.LocalPath);
            if (_application.MultiConfiguration.GetRows().Count > 1)
            {
                _panels.Show(WorkspacePanelId.MultiConfiguration);
            }
        }
    }

    private async Task SaveAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存 STAROPT 项目",
            SuggestedFileName = "optical-system.staropt",
            DefaultExtension = "staropt",
            FileTypeChoices = new[] { NativeOpticFileType }
        });
        if (file is not null)
        {
            await _application.Documents.SaveAsync(file.Path.LocalPath);
        }
    }

    private async Task SaveProjectAsync()
    {
        var currentPath = _application.Documents.CurrentPath;
        if (currentPath is not null &&
            currentPath.EndsWith(".staropt", StringComparison.OrdinalIgnoreCase))
        {
            await _application.Documents.SaveAsync(currentPath);
            return;
        }

        await SaveAsAsync();
    }

    private async Task ExportPythonJsonAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出 Python Optiland JSON",
            SuggestedFileName = "optic.optiland-python.json",
            FileTypeChoices = new[] { PythonOptilandJsonFileType }
        });
        if (file is not null)
        {
            await _application.Documents.SaveAsync(file.Path.LocalPath);
        }
    }

    private async Task ExportCadAsync()
    {
        var documentName = _application.Documents.GetSnapshot().Name;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出 CAD 模型",
            SuggestedFileName = CadSuggestedFileName(documentName),
            DefaultExtension = "step",
            FileTypeChoices = new[] { StepCadFileType }
        });
        if (file is null)
        {
            return;
        }

        _statusText.Text = "正在生成 STEP CAD 模型…";
        var result = await _application.CadExport.ExportAsync(
            file.Path.LocalPath,
            new CadExportOptionsDto(CadExportFormat.Step));
        _statusText.Text =
            $"CAD 已导出：{Path.GetFileName(result.Path)}（{result.ByteCount / 1024.0:0.#} KB）";
    }

    private static string CadSuggestedFileName(string documentName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = new string(documentName
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray())
            .Trim()
            .Trim('.');
        return $"{(string.IsNullOrWhiteSpace(safeName) ? "optical-system" : safeName)}.step";
    }

    private async Task ShowAboutAsync()
    {
        using var authorStream = Avalonia.Platform.AssetLoader.Open(
            new Uri("avares://OptilandWorkbench.App/Assets/Author.jpg"));
        using var authorBitmap = new Bitmap(authorStream);
        var dialog = new Window
        {
            Title = "关于 Optical System Design",
            Width = 640,
            Height = 370,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var closeButton = new Button { Content = "关闭", MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right };
        closeButton.Click += (_, _) => dialog.Close();
        var details = new StackPanel
        {
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "Optical System Design",
                    FontSize = 27,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = "S.T.A.R. Labs 出品",
                    FontSize = 17
                },
                new TextBlock
                {
                    Text = "面向光学系统设计、光线追迹与像质分析的桌面软件。",
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = ".NET 10  ·  Avalonia 12  ·  Managed CPU"
                }
            }
        };
        if (details.Children[1] is TextBlock brandText)
        {
            brandText.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextAccent);
        }
        if (details.Children[3] is TextBlock runtimeText)
        {
            runtimeText.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        }
        var main = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("190,*"),
            ColumnSpacing = 24
        };
        var portrait = new Border
        {
            Width = 180,
            Height = 180,
            CornerRadius = new CornerRadius(18),
            ClipToBounds = true,
            Child = new Image
            {
                Source = authorBitmap,
                Stretch = Stretch.UniformToFill
            }
        };
        portrait.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        Grid.SetColumn(details, 1);
        main.Children.Add(portrait);
        main.Children.Add(details);

        var root = new Grid
        {
            Margin = new Thickness(26),
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 18
        };
        Grid.SetRow(closeButton, 1);
        root.Children.Add(main);
        root.Children.Add(closeButton);
        dialog.Content = root;
        await dialog.ShowDialog(this);
    }
}
