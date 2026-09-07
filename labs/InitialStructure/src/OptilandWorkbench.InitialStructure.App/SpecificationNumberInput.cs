using Avalonia.Controls;
using Avalonia.Input;

namespace OptilandWorkbench.InitialStructure.App;

/// <summary>Keep invalid edits visible instead of silently reverting to a previous optical target.</summary>
internal sealed class SpecificationNumberInput : NumericUpDown
{
    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        var edited = Text;
        var invalid = edited is not null && (!decimal.TryParse(edited, ParsingNumberStyle, NumberFormat, out var value)
            || value < Minimum || value > Maximum);
        base.OnLostFocus(e);
        if (invalid) Text = edited;
    }
}
