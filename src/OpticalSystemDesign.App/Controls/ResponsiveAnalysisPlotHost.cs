using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Controls;

/// <summary>
/// Selects the outer sheet aspect when automatic, then fits it into the viewport.
/// Actual-size mode uses native scrolling; non-drawing reports keep their readable layout.
/// </summary>
internal sealed class ResponsiveAnalysisPlotHost : ScrollViewer
{
    private readonly PlotPagePresenter _page = new();

    public ResponsiveAnalysisPlotHost()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Content = _page;
    }

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    public Control? Child
    {
        get => _page.Source;
        set { _page.Source = value; Offset = default; }
    }

    public bool FitToViewport
    {
        get => _page.FitToViewport;
        set
        {
            if (_page.FitToViewport == value) return;
            _page.FitToViewport = value; Offset = default; InvalidateMeasure();
        }
    }
    internal double ContentScale => Child is UniformDiagramPage && Child.TransformToVisual(this) is { } transform
        ? Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12) : 1;
    internal static double MinimumReadableWidth(Control control) => PlotPagePresenter.MinimumReadableWidth(control);

    protected override Size MeasureOverride(Size availableSize)
    {
        // Use this layout pass's constraint. The previous ScrollViewer viewport
        // can be smaller after a resize and must not cap the new measurement.
        _page.SetViewport(availableSize);
        return base.MeasureOverride(availableSize);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewportProperty && _page is not null && Viewport.Width > 0 && Viewport.Height > 0)
            _page.SetViewport(Viewport);
    }

    private sealed class PlotPagePresenter : Decorator
    {
        private Size _viewport = new(600, 414);
        private readonly Viewbox _fit = new()
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        private Control? _source;
        private readonly AnalysisImageFrame _frame = new() { IntegrateWithResultBinding = true };
        internal Control? Source
        {
            get => _source;
            set
            {
                if (ReferenceEquals(_source, value)) return;
                Child = null; _fit.Child = null; _frame.Child = null; _source = value;
                _frame.Child = value;
                if (value is UniformDiagramPage) { _fit.Child = _frame; Child = _fit; }
                else Child = value is null ? null : _frame;
                InvalidateMeasure();
            }
        }
        private bool _fitToViewport = true;
        internal bool FitToViewport
        {
            get => _fitToViewport;
            set
            {
                if (_fitToViewport == value) return;
                _fitToViewport = value;
                _fit.Stretch = value ? Stretch.Uniform : Stretch.None;
                InvalidateMeasure();
            }
        }

        internal void SetViewport(Size size)
        {
            var next = new Size(double.IsFinite(size.Width) ? Math.Max(0, size.Width) : 600,
                double.IsFinite(size.Height) ? Math.Max(0, size.Height) : 414);
            if (next == _viewport) return;
            _viewport = next;
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (Child is null) return default;
            if (Source is UniformDiagramPage sheet)
            {
                var inset = _frame.DrawingInset;
                sheet.SetViewport(new Size(Math.Max(0, _viewport.Width - 2 * inset), Math.Max(0, _viewport.Height - 2 * inset)));
                _fit.Measure(FitToViewport ? _viewport : new Size(sheet.Width + 2 * inset, sheet.Height + 2 * inset));
                return _fit.DesiredSize;
            }
            var width = Math.Max(Math.Max(320, _viewport.Width), MinimumReadableWidth(Child));
            Child.Measure(new Size(width, double.PositiveInfinity));
            width = Math.Max(width, Child.DesiredSize.Width);
            Child.Measure(new Size(width, double.PositiveInfinity));
            var height = Math.Max(_viewport.Height, Child.DesiredSize.Height);
            Child.Measure(new Size(width, height));
            return new Size(Math.Max(width, Child.DesiredSize.Width), Math.Max(height, Child.DesiredSize.Height));
        }

        // DesiredSize is clipped to the measure constraint by Avalonia. Keep
        // explicit canvas minima when an ancestor grid has less available width.
        internal static double MinimumReadableWidth(Control control)
        {
            var children = control switch
            {
                Panel panel => panel.Children.AsEnumerable(),
                Decorator { Child: { } child } => new[] { child },
                _ => Enumerable.Empty<Control>()
            };
            var minimum = control is ResponsivePlotLegendGrid legend
                ? MinimumReadableWidth(legend.Children[0]) + legend.LegendWidth
                : children.Select(MinimumReadableWidth).DefaultIfEmpty(0).Max();
            return Math.Max(control.MinWidth, minimum) + control.Margin.Left + control.Margin.Right;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Child?.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
