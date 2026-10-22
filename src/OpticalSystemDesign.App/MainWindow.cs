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
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App;

public sealed partial class MainWindow : Window
{
    private static FilePickerFileType NativeOpticFileType => new(UiLocalization.Text("STAROPT 光学设计项目"))
    {
        Patterns = new[] { "*.staropt" },
        AppleUniformTypeIdentifiers = new[] { "public.data" },
        MimeTypes = new[] { "application/vnd.starlabs.staropt" }
    };

    private static FilePickerFileType OpticJsonFileType => new(UiLocalization.Text("光学数据快照"))
    {
        Patterns = new[] { "*.optic.json" },
        AppleUniformTypeIdentifiers = new[] { "public.json" },
        MimeTypes = new[] { "application/json" }
    };

    private static FilePickerFileType CommercialOpticFileType => new(UiLocalization.Text("序列光学格式"))
    {
        Patterns = new[] { "*.zmx", "*.seq", "*.len" },
        MimeTypes = new[] { "text/plain" }
    };

    private static FilePickerFileType ZemaxOpticFileType => new(UiLocalization.Text("Zemax 光学系统"))
    {
        Patterns = new[] { "*.zmx" },
        MimeTypes = new[] { "text/plain" }
    };

    private static FilePickerFileType PlainSequentialFileType => new(UiLocalization.Text("序列光学文本"))
    {
        Patterns = new[] { "*.lens", "*.dat", "*.txt" },
        MimeTypes = new[] { "text/plain" }
    };

    private static FilePickerFileType StepCadFileType => new(UiLocalization.Text("STEP CAD 模型"))
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

    internal static IReadOnlyList<AnalysisRibbonCommand> NonSequentialAnalysisRibbonCommands =>
        WorkbenchAnalysisCatalog.NonSequentialRibbonCommands;

    internal static IReadOnlyList<AnalysisRibbonMenu> NonSequentialAnalysisRibbonMenus =>
        WorkbenchAnalysisCatalog.NonSequentialRibbonMenus;

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
    private IntegratedLaboratories? _laboratories;
    internal IntegratedLaboratories Laboratories => _laboratories ??= new(this);
    private readonly PanelManager _panels;
    private readonly TextBlock _statusText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _eflText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _fNumberText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _apertureText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _trackText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _ribbonHost = new();
    private readonly CommandSearchControl _commandSearch;
    private bool _closeAfterPersistence;
    private bool _closeInProgress;
    private bool _closed;
    private Services.Updates.PreparedOtaUpdate? _pendingOtaUpdate;
    private bool _startupCompleted;
    private readonly string? _startupDocumentPath;

    internal event EventHandler? StartupCompleted;

    public MainWindow()
    {
        var startup = StartupRequest.Parse(Environment.GetCommandLineArgs().Skip(1));
        _startupDocumentPath = startup.DocumentPath;
        _settings = AppSettings.Load();
        UiLocalization.Apply(_settings.Language);
        ConfigureDisplaySettings();
        _application = WorkbenchApplication.Create(
            startup.Sample,
            UserGlassCatalogDirectory(),
            BundledLensLibraryDirectory());
        if (Enum.TryParse<OpticalWorkbenchMode>(_settings.WorkbenchMode, out var savedMode))
        {
            _application.Modes.SwitchTo(savedMode);
        }

        _panels = new PanelManager(
            _application,
            _settings,
            openProjectAsync: OpenLensLibraryProjectAsync,
            userGuideActions: () => _actions.Actions);
        RegisterActions();
        _commandSearch = new CommandSearchControl(_actions);
        _actions.ExecutionFailed += OnActionExecutionFailed;
        _panels.PersistenceFailed += OnWorkspacePersistenceFailed;

        Title = "Optical System Design";
        Icon = BrandAssets.LoadWindowIcon();
        Width = Math.Clamp(_settings.WindowWidth, 640, 4096);
        Height = Math.Clamp(_settings.WindowHeight, 480, 2160);
        MinWidth = 640;
        MinHeight = 480;
        ApplyTheme(save: false);
        Content = MainWindowTitleBar.Wrap(this, BuildShell(), _commandSearch);
        UiLocalization.Attach(this);
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
            if (!await Services.Updates.OtaShutdownGuard.PrepareAsync(
                () => _pendingOtaUpdate is not null ? TrySaveAllChangesAsync() : ConfirmUnsavedChangesAsync("退出程序"),
                async () => { SaveLayout(); return _laboratories is null || await _laboratories.TryCloseAsync(); },
                async () =>
                {
                    await _panels.SaveCurrentSessionAsync();
                    _recoveryTimer?.Stop();
                    if (_recovery is not null) await _recovery.StopAsync(cleanExit: true);
                })) return;
            _closeAfterPersistence = true;
            Close();
        }
        catch (Exception exception)
        {
            _statusText.Text = $"关闭前保存失败：{exception.Message}";
            _recoveryTimer?.Start();
        }
        finally
        {
            _closeInProgress = false;
            if (!_closed && _pendingOtaUpdate is { } cancelledUpdate)
            {
                _pendingOtaUpdate = null;
                cancelledUpdate.HandedOff = false;
                cancelledUpdate.Dispose();
                _statusText.Text = UiLocalization.Text("更新已取消，未执行安装。");
            }
        }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        _closed = true;
        if (_pendingOtaUpdate is { } update)
        {
            try { Services.Updates.WindowsOtaHandoff.StartAfterClose(update); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception or System.Security.SecurityException)
            {
                update.HandedOff = false;
                update.Dispose();
                WindowsInstallationLifetime.ShowBlockedStartup(UiLocalization.Text("无法准备更新，未执行安装。"));
            }
        }
        _recoveryTimer?.Stop();
        if (_recoveryTimer is not null) _recoveryTimer.Tick -= OnRecoveryTick;
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
