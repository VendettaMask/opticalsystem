using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.LogicalTree;

namespace OpticalSystemDesign.App.Services;

internal static partial class UiLocalization
{
    internal static readonly AttachedProperty<bool> PreserveTextProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, bool>("PreserveUiDataText", false, inherits: true);
    private static readonly AttachedProperty<bool> TranslateBoundTextProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, bool>("TranslateBoundUiText", false);

    /// <summary>Mark material names, paths, formulas and user labels at the presentation boundary.</summary>
    internal static T Preserve<T>(T control) where T : Control
    {
        control.SetValue(PreserveTextProperty, true);
        foreach (var item in control.GetLogicalDescendants().OfType<Control>().Prepend(control))
        {
            if (!States.TryGetValue(item, out var state)) continue;
            try
            {
                state.Applying = true;
                foreach (var (property, value) in state.Properties)
                    if (Equals(item.GetValue(property), value.LastApplied))
                    {
                        item.SetCurrentValue(property, value.Source);
                        value.LastApplied = value.Source;
                    }
            }
            finally { state.Applying = false; }
        }
        return control;
    }

    private static bool IsDataText(Control control, AvaloniaProperty property)
    {
        if (control.GetValue(PreserveTextProperty)) return true;
        if (property != TextBlock.TextProperty && property != ContentControl.ContentProperty) return false;
        if (!control.GetValue(TranslateBoundTextProperty) && BindingOperations.GetBindingExpressionBase(control, property) is not null) return true;
        return control.Classes.Contains(ProductFonts.NumericClass)
            || control.GetLogicalAncestors().OfType<Control>().Any(parent => parent is TextBox or NumericUpDown
                || parent.Classes.Contains(ProductFonts.NumericClass));
    }

    /// <summary>Translate the template only; arguments are opaque data and keep invariant numeric formatting.</summary>
    public static string Format(string template, params object?[] arguments) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, Text(template), arguments);
}
