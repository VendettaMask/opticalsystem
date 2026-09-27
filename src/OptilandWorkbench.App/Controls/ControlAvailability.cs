using Avalonia.Automation;
using Avalonia.Controls;

namespace OptilandWorkbench.App.Controls;

/// <summary>Explains existing availability rules without dimming the surrounding labels.</summary>
internal static class ControlAvailability
{
    public static void Set(Control control, bool enabled, string disabledReason)
    {
        control.IsEnabled = enabled;
        Explain(control, enabled ? null : disabledReason);
    }

    public static void Explain(Control control, string? reason)
    {
        ToolTip.SetShowOnDisabled(control, true);
        ToolTip.SetTip(control, reason);
        AutomationProperties.SetHelpText(control, reason);
    }
}
