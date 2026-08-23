using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;

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
