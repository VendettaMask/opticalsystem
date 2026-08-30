using Avalonia;
using Avalonia.Threading;

namespace OptilandWorkbench.App.Theming;

/// <summary>
/// Owns the complete runtime theme transition. Theme dictionaries remain the
/// source of semantic UI resources; the root compatibility layer is updated in
/// the same UI-thread transaction for Fluent and Dock resources that resolve
/// from Application.Resources.
/// </summary>
internal static class ThemeApplicationService
{
    public static ThemeDefinition Apply(
        global::Avalonia.Application application,
        string? settingsValue)
    {
        Dispatcher.UIThread.VerifyAccess();

        var selection = ThemeRegistry.FromSettings(settingsValue);
        // Prepare Fluent/Dock compatibility resources before publishing the
        // variant change so controls never observe a new theme with old accents.
        selection.AccentApplicator(application.Resources);
        application.RequestedThemeVariant = selection.RequestedVariant;
        return selection;
    }
}
