using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace OpticalSystemDesign.Tests;

/// <summary>
/// Mirrors Win32's managed-titlebar negotiation over the real headless platform.
/// Window's normal property/theme callbacks create and refresh the decorations;
/// tests must not force TopLevelHost.UpdateDrawnDecorations after a theme change.
/// </summary>
public class ManagedDecorationHeadlessWindow : DispatchProxy
{
    private IWindowImpl _inner = null!;
    private bool _extended;
    private Action<bool>? _extensionChanged;
    private WindowState _windowState;
    private Action<WindowState>? _windowStateChanged;

    internal static Window Create()
    {
        var carrier = new Window();
        var implementation = DispatchProxy.Create<IWindowImpl, ManagedDecorationHeadlessWindow>();
        ((ManagedDecorationHeadlessWindow)(object)implementation)._inner = carrier.PlatformImpl!;
        return new Window(implementation);
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            // Win32 reports its effective state and calls back when it changes.
            // Headless normally relies on Window's legacy cached-state path,
            // which does not request decoration-part updates on restoration.
            case "get_WindowStateGetterIsUsable": return true;
            case "get_WindowState": return _windowState;
            case "set_WindowStateChanged":
                _windowStateChanged = (Action<WindowState>?)args![0];
                break;
            case "get_WindowStateChanged": return _windowStateChanged;
            case "set_WindowState":
                _windowState = (WindowState)args![0]!;
                _windowStateChanged?.Invoke(_windowState);
                return null;
            case "get_NeedsManagedDecorations": return _extended;
            case "get_RequestedDrawnDecorations":
                return Enum.Parse(method.ReturnType, _extended ? "TitleBar" : "None");
            case "get_IsClientAreaExtendedToDecorations": return _extended;
            case "set_ExtendClientAreaToDecorationsChanged":
                _extensionChanged = (Action<bool>?)args![0];
                break;
            case "get_ExtendClientAreaToDecorationsChanged": return _extensionChanged;
            case "SetExtendClientAreaToDecorationsHint":
                _extended = (bool)args![0]!;
                _extensionChanged?.Invoke(_extended);
                return null;
            case "SetExtendClientAreaTitleBarHeightHint":
                _extensionChanged?.Invoke(_extended);
                return null;
        }
        try { return method.Invoke(_inner, args); }
        catch (TargetInvocationException error)
        {
            ExceptionDispatchInfo.Capture(error.InnerException!).Throw();
            throw;
        }
    }
}
