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

public sealed partial class MainWindow : Window
{
    private static readonly FilePickerFileType NativeOpticFileType = new("STAROPT 光学设计项目")
    {
        Patterns = new[] { "*.staropt" },
        AppleUniformTypeIdentifiers = new[] { "public.data" },
        MimeTypes = new[] { "application/vnd.starlabs.staropt" }
    };

    private static readonly FilePickerFileType LegacyOpticJsonFileType = new("旧版 Optiland JSON（兼容导入）")
    {
        Patterns = new[] { "*.optiland.json", "*.optic.json", "*.json", "*.optiland" },
        AppleUniformTypeIdentifiers = new[] { "public.json" },
        MimeTypes = new[] { "application/json" }
    };

    private static readonly FilePickerFileType PythonOptilandJsonFileType = new("Python Optiland 0.5.8 JSON")
    {
        Patterns = new[] { "*.optiland-python.json", "*.python-optiland.json" },
        AppleUniformTypeIdentifiers = new[] { "public.json" },
        MimeTypes = new[] { "application/json" }
    };

    private static readonly FilePickerFileType CommercialOpticFileType = new("序列光学格式")
    {
        Patterns = new[] { "*.zmx", "*.seq", "*.len" },
        MimeTypes = new[] { "text/plain" }
    };

    private static readonly FilePickerFileType ZemaxOpticFileType = new("Zemax 光学系统")
    {
        Patterns = new[] { "*.zmx" },
        MimeTypes = new[] { "text/plain" }
    };

    private static readonly FilePickerFileType PlainSequentialFileType = new("序列光学文本")
    {
        Patterns = new[] { "*.lens", "*.dat", "*.txt" },
        MimeTypes = new[] { "text/plain" }
    };

    private static readonly FilePickerFileType StepCadFileType = new("STEP CAD 模型")
    {
        Patterns = new[] { "*.step", "*.stp" },
        AppleUniformTypeIdentifiers = new[] { "public.item" },
        MimeTypes = new[] { "model/step", "application/step" }
    };

    private static IReadOnlyList<AnalysisRibbonCommand> AnalysisRibbonCommands =>
        WorkbenchAnalysisCatalog.RibbonCommands;

    private static string[] AnalysisRibbonGroupOrder =>
        WorkbenchAnalysisCatalog.RibbonGroupOrder;

    private static IReadOnlyList<AnalysisRibbonMenu> AnalysisRibbonMenus =>
        WorkbenchAnalysisCatalog.RibbonMenus;

    internal static IReadOnlyList<string> AnalysisRibbonCategories => AnalysisRibbonGroupOrder;

    internal static IReadOnlyList<string> NativeProjectFilePatterns =>
        NativeOpticFileType.Patterns ?? Array.Empty<string>();

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> AnalysisRibbonCommandsByCategory =>
        AnalysisRibbonCommands
            .GroupBy(command => command.Group)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(command => command.Id).ToArray());

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> AnalysisRibbonMenusByCategory =>
        AnalysisRibbonMenus
            .GroupBy(menu => menu.Group)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(menu => menu.Label).ToArray());

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> AnalysisRibbonCommandsByMenu =>
        AnalysisRibbonMenus.ToDictionary(
            menu => menu.Label,
            menu => (IReadOnlyList<string>)menu.CommandIds
                .Where(commandId => !string.Equals(commandId, "-", StringComparison.Ordinal))
                .Select(commandId => AnalysisRibbonCommands.First(command => command.Id == commandId).Label)
                .ToArray());

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> AnalysisRibbonCommandIdsByMenu =>
        AnalysisRibbonMenus.ToDictionary(
            menu => menu.Label,
            menu => (IReadOnlyList<string>)menu.CommandIds
                .Where(commandId => !string.Equals(commandId, "-", StringComparison.Ordinal))
                .ToArray());


    internal static IReadOnlyDictionary<string, string> AnalysisRibbonDisplayNames =>
        AnalysisRibbonCommands.ToDictionary(command => command.Id, command => command.Label);
    private readonly IWorkbenchApplication _application;
    private readonly AppSettings _settings;
    private readonly ActionManager _actions = new();
    private readonly PanelManager _panels;
    private readonly TextBlock _statusText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _eflText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _fNumberText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _apertureText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _trackText = new() { VerticalAlignment = VerticalAlignment.Center };
    private bool _closeAfterPersistence;
    private bool _closeInProgress;
    private bool _closed;
    private bool _startupCompleted;
    private readonly string? _startupDocumentPath;

    internal event EventHandler? StartupCompleted;

    public MainWindow()
    {
        var startup = StartupRequest.Parse(Environment.GetCommandLineArgs().Skip(1));
        _startupDocumentPath = startup.DocumentPath;
        _settings = AppSettings.Load();
        ConfigureDisplaySettings();
        _application = WorkbenchApplication.Create(
            startup.Sample,
            UserGlassCatalogDirectory(),
            BundledLensLibraryDirectory(),
            InstalledZemaxStockCatalogDirectory());
        _panels = new PanelManager(
            _application,
            _settings,
            openProjectAsync: OpenLensLibraryProjectAsync);
        RegisterActions();
        _actions.ExecutionFailed += OnActionExecutionFailed;
        _panels.PersistenceFailed += OnWorkspacePersistenceFailed;

        Title = "Optical System Design";
        Icon = BrandAssets.LoadWindowIcon();
        Width = Math.Clamp(_settings.WindowWidth, 720, 4096);
        Height = Math.Clamp(_settings.WindowHeight, 640, 2160);
        MinWidth = 720;
        MinHeight = 640;
        ApplyTheme(save: false);
        Content = BuildShell();
        DisplayTypography.Apply(this);

        _application.Events.Changed += OnWorkspaceChanged;
        _application.Events.StatusChanged += OnWorkspaceStatusChanged;
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
        KeyDown += OnWindowKeyDown;
        RefreshStatus();
        if (!string.IsNullOrWhiteSpace(_settings.LoadWarning))
        {
            _statusText.Text = $"{_settings.LoadWarning}   |   {_statusText.Text}";
        }
    }

    private async void OnOpened(object? sender, EventArgs args)
    {
        try
        {
            if (_startupDocumentPath is not null)
            {
                await _application.Documents.OpenAsync(_startupDocumentPath);
            }

            await _panels.InitializeAsync();
        }
        catch (Exception exception)
        {
            _panels.ResetLayout();
            _statusText.Text = $"工作区恢复失败：{exception.Message}";
        }
        finally
        {
            if (!_startupCompleted)
            {
                _startupCompleted = true;
                StartupCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_closeAfterPersistence)
        {
            return;
        }

        args.Cancel = true;
        if (_closeInProgress)
        {
            return;
        }

        _closeInProgress = true;
        try
        {
            if (!await ConfirmUnsavedChangesAsync("退出程序"))
            {
                return;
            }

            SaveLayout();
            await _panels.SaveCurrentSessionAsync();
            _closeAfterPersistence = true;
            Close();
        }
        catch (Exception exception)
        {
            _statusText.Text = $"关闭前保存失败：{exception.Message}";
        }
        finally
        {
            _closeInProgress = false;
        }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        _closed = true;
        Opened -= OnOpened;
        Closing -= OnClosing;
        Closed -= OnClosed;
        KeyDown -= OnWindowKeyDown;
        _actions.ExecutionFailed -= OnActionExecutionFailed;
        _panels.PersistenceFailed -= OnWorkspacePersistenceFailed;
        _application.Events.Changed -= OnWorkspaceChanged;
        _application.Events.StatusChanged -= OnWorkspaceStatusChanged;
        _panels.Dispose();
        _application.Dispose();
    }

}
