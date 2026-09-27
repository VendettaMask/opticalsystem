using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Dock.Controls.DeferredContentControl;
using Dock.Model.Core;

namespace OptilandWorkbench.App.Services;

public sealed class WorkspaceViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is IDockable { Context: Control control })
        {
            return new WorkspaceContentHost(control);
        }

        return new TextBlock { Text = "页面内容不可用。" };
    }

    public bool Match(object? data) => data is IDockable;
}

internal sealed class WorkspaceContentHost : ContentControl
{
    private readonly Control _workspaceContent;
    private bool _attachedToPresenter;
    private Visual[] _visibilityChain = [];

    public WorkspaceContentHost(Control workspaceContent)
    {
        _workspaceContent = workspaceContent;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _visibilityChain = this.GetVisualAncestors().Prepend(this).ToArray();
        _attachedToPresenter = _visibilityChain.OfType<DeferredContentPresenter>().Any();
        foreach (var visual in _visibilityChain) visual.PropertyChanged += OnAncestorPropertyChanged;
        UpdatePresentation();
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == IsVisibleProperty) UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        // Dock keeps the tabbed and MDI presenters attached simultaneously. The
        // hidden tabbed presenter also changes on every MDI activation; it must
        // never take the cached panel away from the visible child window.
        if (!_attachedToPresenter || _visibilityChain.Any(visual => !visual.IsVisible))
        {
            ReleaseContent();
            return;
        }

        if (_workspaceContent.Parent is ContentControl previousHost &&
            !ReferenceEquals(previousHost, this))
        {
            previousHost.Content = null;
        }

        Content = _workspaceContent;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _attachedToPresenter = false;
        foreach (var visual in _visibilityChain) visual.PropertyChanged -= OnAncestorPropertyChanged;
        _visibilityChain = [];
        ReleaseContent();
    }

    private void ReleaseContent()
    {
        if (ReferenceEquals(Content, _workspaceContent))
        {
            Content = null;
        }
    }
}
