using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Controls;

/// <summary>The native tab/selection model, with an Isekai-only enclosing book binding.</summary>
internal sealed class AnalysisResultTabs : TabControl
{
    protected override Type StyleKeyOverride => typeof(TabControl);
    internal bool UsesBookFrame => ThemeDrawingSurface.Material(this) is IsekaiDrawingMaterial;
    private bool UsesEnclosingBook => this.FindAncestorOfType<AnalysisBookWorkspace>()?.UsesBookFrame == true;
    internal double BookNavigationHeight { get; private set; } = 56;

    internal AnalysisResultTabs()
    {
        // Keep both native presenters and their selected controls alive through
        // theme changes. Only the binding's paint and layout inset change.
        Template = new FuncControlTemplate<AnalysisResultTabs>((tabs, scope) =>
        {
            var items = new ItemsPresenter { Name = "PART_ItemsPresenter" };
            scope.Register(items.Name, items);
            items.Bind(ItemsPresenter.ItemsPanelProperty, tabs.GetObservable(ItemsPanelProperty), BindingPriority.Template);
            var navigation = new BookNavigation(tabs) { Name = "AnalysisBookNavigation", Child = items };
            var content = new Panel { ClipToBounds = true };
            foreach (var name in new[] { "PART_SelectedContentHost2", "PART_SelectedContentHost" })
            {
                var presenter = new ContentPresenter
                {
                    Name = name, IsVisible = name == "PART_SelectedContentHost",
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch
                };
                scope.Register(name, presenter);
                presenter.Bind(ContentPresenter.HorizontalContentAlignmentProperty, tabs.GetObservable(HorizontalContentAlignmentProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.VerticalContentAlignmentProperty, tabs.GetObservable(VerticalContentAlignmentProperty), BindingPriority.Template);
                content.Children.Add(presenter);
            }
            var grid = new DockPanel { Children = { navigation, content } };
            navigation.Bind(DockPanel.DockProperty, tabs.GetObservable(TabStripPlacementProperty), BindingPriority.Template);
            var frame = new AnalysisImageFrame
            {
                Name = "AnalysisResultBookFrame", Child = grid, ResultBookOnly = true, IntegrateWithWorkspaceBinding = true
            };
            var layout = new BookLayout(tabs, navigation) { Child = frame };
            void Refresh()
            {
                foreach (var presenter in content.Children.OfType<ContentPresenter>())
                    presenter.Margin = tabs.UsesBookFrame ? default : tabs.Padding;
                // Fitted sheets can leave exposed space above/below or at the sides.
                // Continue the existing parchment there rather than a flat light strip.
                grid.Background = tabs.UsesBookFrame ? IsekaiDrawingMaterial.Instance.PaperBackground : tabs.Background;
                navigation.InvalidateMeasure(); navigation.InvalidateVisual(); layout.InvalidateMeasure();
            }
            tabs.ActualThemeVariantChanged += (_, _) => Refresh();
            tabs.PropertyChanged += (_, e) => { if (e.Property == PaddingProperty || e.Property == BackgroundProperty) Refresh(); };
            Refresh(); return layout;
        });
    }

    private sealed class BookLayout(AnalysisResultTabs tabs, Control navigation) : Decorator
    {
        private double _width;
        protected override Size MeasureOverride(Size availableSize)
        {
            Child?.Measure(availableSize);
            if (!tabs.UsesBookFrame || tabs.UsesEnclosingBook) { _width = availableSize.Width; return Child?.DesiredSize ?? default; }
            var inset = IsekaiDrawingMaterial.Instance.FrameInset;
            var sheet = tabs.GetVisualDescendants().OfType<UniformDiagramPage>().FirstOrDefault();
            _width = double.IsFinite(availableSize.Height)
                ? Math.Min(availableSize.Width, Math.Max(0, availableSize.Height - navigation.DesiredSize.Height - 2 * inset)
                    * (sheet?.Width ?? Services.DiagramSheetLayout.Width) / UniformDiagramPage.SheetHeight + 2 * inset)
                : availableSize.Width;
            Child?.Measure(new Size(_width, availableSize.Height));
            return Child?.DesiredSize ?? default;
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            var width = Math.Min(finalSize.Width, _width);
            Child?.Arrange(new Rect((finalSize.Width - width) / 2, 0, width, finalSize.Height)); return finalSize;
        }
    }

    private sealed class BookNavigation(AnalysisResultTabs tabs) : Decorator
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            if (!tabs.UsesBookFrame) return base.MeasureOverride(availableSize);
            Child?.Measure(new Size(Math.Max(0, availableSize.Width - 24), availableSize.Height));
            tabs.BookNavigationHeight = Math.Max(56, (Child?.DesiredSize.Height ?? 0) + 16);
            return new Size((Child?.DesiredSize.Width ?? 0) + 24, tabs.BookNavigationHeight);
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            if (!tabs.UsesBookFrame) return base.ArrangeOverride(finalSize);
            if (Child is { } child)
                child.Arrange(new Rect(12, (finalSize.Height - child.DesiredSize.Height) / 2,
                    Math.Max(0, finalSize.Width - 24), child.DesiredSize.Height));
            return finalSize;
        }
        public override void Render(DrawingContext context)
        {
            if (!tabs.UsesBookFrame) return;
            var used = (Child?.DesiredSize.Width ?? 0) + 36;
            IsekaiDrawingMaterial.Instance.DrawNavigation(context, new Rect(Bounds.Size), used);
        }
    }
}

internal sealed class AnalysisResultTab : TabItem
{
    private bool _bookTemplate;
    protected override Type StyleKeyOverride => typeof(TabItem);
    internal AnalysisResultTab()
    {
        ActualThemeVariantChanged += (_, _) => UpdateTemplate();
        UpdateTemplate();
    }
    private void UpdateTemplate()
    {
        var book = ThemeDrawingSurface.Material(this) is IsekaiDrawingMaterial;
        if (book == _bookTemplate) return;
        _bookTemplate = book;
        if (!book) { ClearValue(TemplateProperty); return; }
        Template = new FuncControlTemplate<AnalysisResultTab>((item, _) =>
        {
            var header = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return new BookTabPlaque(item, header) { Child = header, Padding = new Thickness(24, 9), Margin = new Thickness(2, 0) };
        });
    }

    private sealed class BookTabPlaque(AnalysisResultTab item, TextBlock header) : Decorator
    {
        private static readonly IImmutableBrush Gold = B("#C49B53"), Highlight = B("#E9CB87"), Edge = B("#71502C");
        private static readonly IImmutableBrush Paper = B("#F3E5CC"), Leather = B("#382017"), Hover = B("#53331F");
        protected override Size MeasureOverride(Size availableSize)
        {
            Child?.Measure(new Size(Math.Max(0, availableSize.Width - Padding.Left - Padding.Right),
                Math.Max(0, availableSize.Height - Padding.Top - Padding.Bottom)));
            return new Size((Child?.DesiredSize.Width ?? 0) + Padding.Left + Padding.Right,
                (Child?.DesiredSize.Height ?? 0) + Padding.Top + Padding.Bottom);
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            Child?.Arrange(new Rect(finalSize).Deflate(Padding)); return finalSize;
        }
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e); item.PropertyChanged += ItemChanged; UpdateInk();
        }
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            item.PropertyChanged -= ItemChanged; base.OnDetachedFromVisualTree(e);
        }
        private void ItemChanged(object? sender, AvaloniaPropertyChangedEventArgs e) { UpdateInk(); InvalidateVisual(); }
        private void UpdateInk()
        {
            header.Text = item.Header?.ToString(); header.FontSize = item.FontSize;
            header.FontFamily = item.FontFamily; header.FontWeight = item.FontWeight;
            header.Foreground = !item.IsEnabled ? B("#A78C66") : item.IsSelected ? B("#513723") : B("#806344");
        }
        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            if (bounds.Width < 18 || bounds.Height < 18) return;
            if (item.IsSelected)
            {
                var y = bounds.Bottom - 5;
                context.DrawLine(new ImmutablePen(B("#71502C"), 1.5),
                    new Point(bounds.Left + 22, y), new Point(bounds.Right - 22, y));
                context.DrawEllipse(B("#9D783E"), null, new Point(bounds.Center.X, y), 2, 2);
            }
            else if (item.IsPointerOver)
                context.DrawRectangle(B("#1871502C"), null, bounds.Deflate(4), 4, 4);
            if (item.IsFocused) context.DrawRectangle(null, new ImmutablePen(B("#806344"), 1), bounds.Deflate(5), 4, 4);
            base.Render(context);
        }
        private static StreamGeometry Plaque(Rect b)
        {
            var cut = Math.Min(9, b.Height / 3);
            var geometry = new StreamGeometry();
            using var c = geometry.Open();
            c.BeginFigure(new Point(b.Left + cut, b.Top), true);
            foreach (var p in new[] { new Point(b.Right - cut, b.Top), new Point(b.Right, b.Top + cut),
                new Point(b.Right, b.Bottom - cut), new Point(b.Right - cut, b.Bottom),
                new Point(b.Left + cut, b.Bottom), new Point(b.Left, b.Bottom - cut), new Point(b.Left, b.Top + cut) }) c.LineTo(p);
            c.EndFigure(true); return geometry;
        }
        private static IImmutableBrush B(string value) => new ImmutableSolidColorBrush(Color.Parse(value));
    }
}
