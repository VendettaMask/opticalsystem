using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.App.Controls;

public sealed class DrawingPreviewControl : Control, IInteractiveCanvasAutomationSource
{
    private const double ZoomStep = 1.25;
    private readonly SceneViewport _viewport = new();
    private IImage? _source;
    private bool _panning;
    private Point _lastPointer;

    public DrawingPreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
        InteractiveCanvasFocus.Attach(this);
        AutomationProperties.SetName(this, "光学图纸预览");
        AutomationProperties.SetHelpText(this, "使用方向键平移，使用加号或减号缩放，按 Home 重置视图。");
    }

    public IImage? Source
    {
        get => _source;
        set
        {
            if (ReferenceEquals(_source, value))
            {
                return;
            }

            _source = value;
            ResetView();
        }
    }

    public double Zoom => _viewport.Zoom;

    public event EventHandler? ViewChanged;

    public void ZoomIn() => ZoomAtCenter(ZoomStep);

    public void ZoomOut() => ZoomAtCenter(1 / ZoomStep);

    public void ResetView()
    {
        _viewport.Reset();
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new InteractiveCanvasAutomationPeer(this);

    string IInteractiveCanvasAutomationSource.AutomationValue => _source is null
        ? "光学图纸预览；没有可用图纸。"
        : $"光学图纸预览；图像尺寸 {_source.Size.Width:G6} × {_source.Size.Height:G6}；缩放 {Zoom:P0}。";

    void IInteractiveCanvasAutomationSource.InvokeAutomationAction() => ResetView();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!InteractiveCanvasKeyboard.TryGetCommand(e.Key, out var command))
        {
            return;
        }

        switch (command)
        {
            case InteractiveCanvasCommand.Reset:
                ResetView();
                break;
            case InteractiveCanvasCommand.ZoomIn:
                ZoomIn();
                break;
            case InteractiveCanvasCommand.ZoomOut:
                ZoomOut();
                break;
            case InteractiveCanvasCommand.Left:
                PanBy(new Vector(-24, 0));
                break;
            case InteractiveCanvasCommand.Right:
                PanBy(new Vector(24, 0));
                break;
            case InteractiveCanvasCommand.Up:
                PanBy(new Vector(0, -24));
                break;
            case InteractiveCanvasCommand.Down:
                PanBy(new Vector(0, 24));
                break;
        }

        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _viewport.ZoomAt(Math.Pow(1.15, e.Delta.Y), e.GetPosition(this), Bounds.Size);
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            ResetView();
            e.Handled = true;
            return;
        }

        Focus();
        _panning = true;
        _lastPointer = point.Position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_panning)
        {
            return;
        }

        var position = e.GetPosition(this);
        _viewport.PanBy(position - _lastPointer);
        _lastPointer = position;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_panning)
        {
            return;
        }

        _panning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var background = IsekaiTheme.IsDarkLike(ActualThemeVariant)
            ? new SolidColorBrush(Color.FromRgb(14, 17, 21))
            : new SolidColorBrush(Color.FromRgb(220, 223, 228));
        context.DrawRectangle(background, null, Bounds);
        if (_source is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            InteractiveCanvasFocus.Draw(context, this);
            return;
        }

        const double padding = 12;
        var availableWidth = Math.Max(1, Bounds.Width - (padding * 2));
        var availableHeight = Math.Max(1, Bounds.Height - (padding * 2));
        var scale = Math.Min(
            availableWidth / Math.Max(1, _source.Size.Width),
            availableHeight / Math.Max(1, _source.Size.Height));
        var width = _source.Size.Width * scale;
        var height = _source.Size.Height * scale;
        var baseRect = new Rect(
            (Bounds.Width - width) / 2,
            (Bounds.Height - height) / 2,
            width,
            height);
        var topLeft = _viewport.Apply(baseRect.TopLeft, Bounds.Size);
        var destination = new Rect(
            topLeft,
            new Size(baseRect.Width * _viewport.Zoom, baseRect.Height * _viewport.Zoom));
        context.DrawImage(_source, new Rect(_source.Size), destination);
        InteractiveCanvasFocus.Draw(context, this);
    }

    private void ZoomAtCenter(double factor)
    {
        _viewport.ZoomAt(
            factor,
            new Point(Bounds.Width / 2, Bounds.Height / 2),
            Bounds.Size);
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PanBy(Vector delta)
    {
        _viewport.PanBy(delta);
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
