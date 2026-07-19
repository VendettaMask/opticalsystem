using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.VisualTree;
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
    private const string DeferredContentPresenterTypeName =
        "Dock.Controls.DeferredContentControl.DeferredContentPresenter";

    private readonly Control _workspaceContent;

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
        if (!this.GetVisualAncestors().Any(IsDeferredContentPresenter))
        {
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
        if (ReferenceEquals(Content, _workspaceContent))
        {
            Content = null;
        }
    }

    private static bool IsDeferredContentPresenter(Visual visual) =>
        string.Equals(
            visual.GetType().FullName,
            DeferredContentPresenterTypeName,
            StringComparison.Ordinal);
}
