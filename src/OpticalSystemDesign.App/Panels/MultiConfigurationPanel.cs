using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class MultiConfigurationPanel : UserControl, IDisposable
{
    private readonly IMultiConfigurationService _configurations;
    private readonly IWorkspaceEventStream _events;
    private bool _disposed;

    public MultiConfigurationPanel(IPrescriptionService prescription, IMultiConfigurationService configurations, IWorkspaceEventStream events)
        : this(configurations, events) { }

    public MultiConfigurationPanel(IMultiConfigurationService configurations, IWorkspaceEventStream events)
    {
        _configurations = configurations;
        _events = events;
        this.BindThemeResource(BackgroundProperty, ThemeResourceBindings.Surface);
        Padding = new Thickness(12);
        Content = BuildOperandTable();
        _events.Changed += OnWorkspaceChanged;
        RefreshOperandTable();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _operandGrid.LayoutUpdated -= OnOperandGridLayoutUpdated;
        _events.Changed -= OnWorkspaceChanged;
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            if (args.FileSwitched)
            {
                _operandDraft = false;
                _pickupDraft = false;
                _operandMessage.Text = string.Empty;
            }
            RefreshOperandTable();
        });

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 4, 0)
    };
}
