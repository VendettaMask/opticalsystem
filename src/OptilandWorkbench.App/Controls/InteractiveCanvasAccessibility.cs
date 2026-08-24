using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace OptilandWorkbench.App.Controls;

internal enum InteractiveCanvasCommand
{
    Reset,
    ZoomIn,
    ZoomOut,
    Left,
    Right,
    Up,
    Down
}

internal static class InteractiveCanvasKeyboard
{
    internal static bool TryGetCommand(Key key, out InteractiveCanvasCommand command)
    {
        command = key switch
        {
            Key.Home => InteractiveCanvasCommand.Reset,
            Key.Add or Key.OemPlus => InteractiveCanvasCommand.ZoomIn,
            Key.Subtract or Key.OemMinus => InteractiveCanvasCommand.ZoomOut,
            Key.Left => InteractiveCanvasCommand.Left,
            Key.Right => InteractiveCanvasCommand.Right,
            Key.Up => InteractiveCanvasCommand.Up,
            Key.Down => InteractiveCanvasCommand.Down,
            _ => default
        };
        return key is Key.Home
            or Key.Add or Key.OemPlus
            or Key.Subtract or Key.OemMinus
            or Key.Left or Key.Right or Key.Up or Key.Down;
    }
}

internal sealed class InteractiveCanvasAutomationPeer(Control owner) : ControlAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() =>
        AutomationControlType.Custom;
}

internal static class InteractiveCanvasFocus
{
    internal static void Attach(Control control)
    {
        control.GotFocus += (_, _) => control.InvalidateVisual();
        control.LostFocus += (_, _) => control.InvalidateVisual();
    }

    internal static void Draw(DrawingContext context, Control control)
    {
        if (!control.IsKeyboardFocusWithin || control.Bounds.Width < 6 || control.Bounds.Height < 6)
        {
            return;
        }

        var brush = control.TryFindResource(
            "AccentFillColorDefaultBrush",
            control.ActualThemeVariant,
            out var value) && value is IBrush resourceBrush
                ? resourceBrush
                : Brushes.DodgerBlue;
        context.DrawRectangle(
            null,
            new Pen(brush, 2),
            new Avalonia.Rect(2, 2, control.Bounds.Width - 4, control.Bounds.Height - 4));
    }
}
