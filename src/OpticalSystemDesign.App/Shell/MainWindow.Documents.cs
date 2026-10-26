using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Services.Updates;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App;

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
                OpticJsonFileType,
                CommercialOpticFileType,
                PlainSequentialFileType,
                new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
            }
        });
        if (files.Count > 0)
        {
            if (!await ConfirmUnsavedChangesAsync("打开另一个光学系统"))
            {
                return;
            }

            await _panels.SaveCurrentSessionAsync();
            await _application.Documents.OpenAsync(files[0].Path.LocalPath);
        }
    }

    private async Task<bool> TrySaveAsAsync(Window? owner = null)
    {
        var file = await (owner ?? this).StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存 STAROPT 项目",
            SuggestedFileName = "optical-system.staropt",
            DefaultExtension = "staropt",
            FileTypeChoices = new[] { NativeOpticFileType }
        });
        if (file is null)
        {
            return false;
        }

        return await TrySaveProjectToPathAsync(file.Path.LocalPath);
    }

    private async Task SaveProjectAsync()
    {
        await TrySaveProjectAsync();
    }

    private async Task<bool> TrySaveProjectAsync(Window? owner = null)
    {
        var currentPath = _application.Documents.CurrentPath;
        if (currentPath is not null &&
            currentPath.EndsWith(".staropt", StringComparison.OrdinalIgnoreCase))
        {
            return await TrySaveProjectToPathAsync(currentPath);
        }

        return await TrySaveAsAsync(owner);
    }

    internal async Task<bool> TrySaveProjectToPathAsync(string path)
    {
        var revision = _application.Documents.GetSnapshot().Revision;
        await _application.Documents.SaveAsync(path);
        var current = _application.Documents.GetSnapshot();
        return current.Revision == revision && !current.IsDirty;
    }

    private Task<bool> ConfirmUnsavedChangesAsync(string operationDescription, Window? owner = null)
    {
        return UnsavedChangesGuard.CanContinueAsync(
            _application.Documents.GetSnapshot().IsDirty || _panels.HasUnsavedToleranceChanges,
            () => new UnsavedChangesWindow(operationDescription)
                .ShowDialog<UnsavedChangesChoice>(owner ?? this),
            () => TrySaveAllChangesAsync(owner));
    }

    private async Task<bool> TrySaveAllChangesAsync(Window? owner = null)
    {
        if (_application.Documents.GetSnapshot().IsDirty && !await TrySaveProjectAsync(owner))
        {
            return false;
        }

        if (_panels.HasUnsavedToleranceChanges && !await _panels.SaveUnsavedToleranceChangesAsync(owner ?? this))
            return false;
        return !_application.Documents.GetSnapshot().IsDirty && !_panels.HasUnsavedToleranceChanges;
    }

    private async Task<bool> OpenLensLibraryProjectAsync(string path)
    {
        if (!await ConfirmUnsavedChangesAsync("打开镜头库中的光学系统"))
        {
            return false;
        }

        await _panels.SaveCurrentSessionAsync();
        await _application.Documents.OpenAsync(path);

        return true;
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
        var warning = result.Warnings is { Count: > 0 }
            ? $"；警告：{string.Join(" ", result.Warnings)}"
            : string.Empty;
        _statusText.Text =
            $"CAD 已导出：{Path.GetFileName(result.Path)}"
            + $"（{result.PartCount} 个零件，{result.TriangleCount:N0} 个三角形，"
            + $"{result.ByteCount / 1024.0:0.#} KB）"
            + warning;
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
            new Uri("avares://OpticalSystemDesign.App/Assets/Author.jpg"));
        using var authorBitmap = new Bitmap(authorStream);
        Window? dialog = null;
        dialog = CreateAboutDialog(authorBitmap, handoff: async prepared =>
        {
            if (_closed || _closeInProgress || _pendingOtaUpdate is not null) return false;
            // Updating and saving are separate choices. Keep About and its verified
            // download open when the user cancels this prompt or a chosen save.
            if (!await ConfirmUnsavedChangesAsync("安装更新并重启程序", dialog)) return false;
            if (_closed || _closeInProgress || dialog?.IsVisible != true) return false;
            _pendingOtaUpdate = prepared;
            prepared.HandedOff = true; // Closing About must not delete the verified download.
            dialog!.Close();
            Close();
            return true;
        });
        await dialog.ShowDialog(this);
    }

    internal static Window CreateAboutDialog(Bitmap authorBitmap, IReleaseUpdateService? updateService = null,
        Func<Uri, Task<bool>>? openReleasePage = null, Func<PreparedOtaUpdate, Task<bool>>? handoff = null,
        IOtaDownloader? downloader = null, Func<string?>? unavailableReason = null)
    {
        ArgumentNullException.ThrowIfNull(authorBitmap);
        var dialog = new Window
        {
            Title = "关于 Optical System Design",
            Width = 680,
            Height = 600,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var closeButton = new Button { Name = "CloseAboutWindow", Content = "关闭", MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        closeButton.Click += (_, _) => dialog.Close();
        var versionText = new TextBlock
        {
            Name = "AboutVersion",
            Text = $"版本 {ProductVersionInfo.Current}",
            FontSize = DisplayTypography.Body,
            TextWrapping = TextWrapping.Wrap
        };
        versionText.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        var brandText = new TextBlock
        {
            Name = "AboutPublisher",
            Text = "S.T.A.R. Labs 出品",
            FontSize = DisplayTypography.EmptyStateTitle
        };
        brandText.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextAccent);
        var runtimeText = new TextBlock
        {
            Name = "AboutRuntime",
            Text = ".NET 10  ·  Avalonia 12  ·  Managed CPU"
        };
        runtimeText.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        var details = new StackPanel
        {
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Name = "AboutProductName",
                    Text = "Optical System Design",
                    FontSize = DisplayTypography.EmptyStateIcon,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                versionText,
                new TextBlock { Name = "AboutBuild", Text = $"构建编号 {ProductVersionInfo.OtaBuildSequence.ToString(System.Globalization.CultureInfo.InvariantCulture)}", TextWrapping = TextWrapping.Wrap },
                new CompanyLogoControl
                {
                    Name = "AboutCompanyLogo", Width = 220, Height = 28,
                    HorizontalAlignment = HorizontalAlignment.Left
                },
                brandText,
                new TextBlock
                {
                    Text = "面向光学系统设计、光线追迹与像质分析的桌面软件。",
                    TextWrapping = TextWrapping.Wrap
                },
                runtimeText
            }
        };
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
        var updates = new SoftwareUpdatePanel(updateService, openReleasePage, handoff, downloader, unavailableReason);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        footer.Children.Add(updates);
        Grid.SetColumn(closeButton, 1);
        footer.Children.Add(closeButton);
        Grid.SetRow(footer, 1);
        root.Children.Add(new ScrollViewer
        {
            Name = "AboutDetailsScroll",
            Content = main,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        });
        root.Children.Add(footer);
        dialog.Content = root;
        dialog.Closed += (_, _) => updates.Dispose();
        DisplayTypography.Apply(dialog);
        UiLocalization.Attach(dialog);
        return dialog;
    }
}
