using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
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
    private static readonly ConditionalWeakTable<global::Avalonia.Application, AppliedResources> Applied = new();

    public static ThemeDefinition Apply(
        global::Avalonia.Application application,
        string? settingsValue)
    {
        Dispatcher.UIThread.VerifyAccess();

        var selection = ThemeRegistry.FromSettings(settingsValue);
        var state = Applied.GetOrCreateValue(application);
        state.Selection = selection;
        if (!state.Subscribed)
        {
            state.Subscribed = true;
            application.ActualThemeVariantChanged += (_, _) =>
            {
                if (state.Selection?.FollowsSystem == true)
                    ApplyResources(application, state, state.Selection.ResolveVisual(application.ActualThemeVariant));
            };
        }
        if (selection.FollowsSystem) application.RequestedThemeVariant = selection.RequestedVariant;
        ApplyResources(application, state, selection.ResolveVisual(application.ActualThemeVariant));
        application.RequestedThemeVariant = selection.RequestedVariant;
        return selection;
    }

    private static void ApplyResources(global::Avalonia.Application application, AppliedResources state, ThemeDefinition visual)
    {
        // Prepare Fluent/Dock compatibility resources before publishing the
        // variant change so controls never observe a new theme with old accents.
        var next = new ResourceDictionary();
        visual.AccentApplicator(next);
        var originals = state.Originals;
        foreach (var key in originals.Keys.Where(key => !next.ContainsKey(key)).ToArray())
        {
            var original = originals[key];
            if (original.Exists) application.Resources[key] = original.Value;
            else application.Resources.Remove(key);
            originals.Remove(key);
        }
        foreach (var (key, value) in next)
        {
            if (!originals.ContainsKey(key))
            {
                var exists = application.Resources.ContainsKey(key);
                originals[key] = (exists, exists ? application.Resources[key] : null);
            }
            application.Resources[key] = value;
        }
    }

    private sealed class AppliedResources
    {
        public ThemeDefinition? Selection { get; set; }
        public bool Subscribed { get; set; }
        public Dictionary<object, (bool Exists, object? Value)> Originals { get; } = new();
    }
}
