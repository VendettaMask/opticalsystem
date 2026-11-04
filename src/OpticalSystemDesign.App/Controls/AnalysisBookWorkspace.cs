using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Controls;

/// <summary>One fixed native tree enclosing the real toolbar, result and navigation.</summary>
internal sealed class AnalysisBookWorkspace : Decorator
{
    private readonly Control _toolbar;
    private readonly BookToolbar _bar;
    private double _bookWidth;
    internal bool UsesBookFrame => ThemeDrawingSurface.Material(this) is IsekaiDrawingMaterial;

    internal AnalysisBookWorkspace(Control toolbar, Control result)
    {
        _toolbar = toolbar;
        _bar = new BookToolbar(this) { Name = "AnalysisBookToolbar", Child = toolbar };
        _bar.Resources.ThemeDictionaries[IsekaiTheme.Variant] = IsekaiDrawingMaterial.ManuscriptCommandResources();
        DockPanel.SetDock(_bar, Avalonia.Controls.Dock.Top);
        var root = new DockPanel { Children = { _bar, result } };
        Child = new AnalysisImageFrame { Name = "AnalysisWorkspaceBookFrame", Child = root, ResultBookOnly = true };
        ActualThemeVariantChanged += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => Refresh();
    }

    private void Refresh()
    {
        _toolbar.Margin = UsesBookFrame ? new Thickness(14, 8, 14, 8) : new Thickness(12, 10, 12, 8);
        _bar.InvalidateVisual(); InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(availableSize);
        _bookWidth = availableSize.Width;
        if (!UsesBookFrame || !double.IsFinite(availableSize.Height) || !double.IsFinite(availableSize.Width))
            return Child?.DesiredSize ?? default;
        var navigation = this.GetVisualDescendants().OfType<AnalysisResultTabs>().FirstOrDefault()?.BookNavigationHeight ?? 66;
        // Fit the unchanged 4:3 sheet after the actual wrapped command row and native tabs.
        for (var pass = 0; pass < 3; pass++)
        {
            _bookWidth = Math.Min(availableSize.Width, Math.Max(Math.Min(350, availableSize.Width),
                Math.Max(0, availableSize.Height - _bar.DesiredSize.Height - navigation - 56) * 1200 / 900 + 56));
            Child?.Measure(new Size(_bookWidth, availableSize.Height));
        }
        return Child?.DesiredSize ?? default;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = UsesBookFrame ? Math.Min(finalSize.Width, _bookWidth) : finalSize.Width;
        Child?.Arrange(new Rect((finalSize.Width - width) / 2, 0, width, finalSize.Height));
        return finalSize;
    }

    private sealed class BookToolbar(AnalysisBookWorkspace workspace) : Decorator
    {
        public override void Render(DrawingContext context)
        {
            if (!workspace.UsesBookFrame) return;
            var bounds = new Rect(Bounds.Size);
            IsekaiDrawingMaterial.Instance.DrawManuscriptBand(context, bounds, footer: false);
            var commands = Child?.GetVisualDescendants().OfType<WrapPanel>().FirstOrDefault();
            var used = (commands?.DesiredSize.Width ?? bounds.Width) + 40;
            if (bounds.Width - used > 130)
                using (context.PushOpacity(.38))
                    IsekaiCraftsmanship.DrawDivider(context, new Rect(bounds.Right - 124, bounds.Center.Y - 7, 104, 14));
        }
    }
}
