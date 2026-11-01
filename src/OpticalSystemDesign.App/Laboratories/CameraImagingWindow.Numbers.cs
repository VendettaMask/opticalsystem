using Avalonia.Controls;
using Avalonia.Input;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    private static bool IntegerNumber(string key) => key is "black" or "bits" or "seed"
        or "scene3d-rings" or "scene3d-arms" or "scene3d-pupil-grid" or "scene3d-subdivide"
        or "scene3d-psf-radius" or "scene3d-guard" || key.StartsWith("sampling-", StringComparison.Ordinal);

    private bool NumberUsedByRun(string key) => !key.StartsWith("sampling-", StringComparison.Ordinal)
        && (!key.StartsWith("scene3d-", StringComparison.Ordinal) || IsScene3D)
        && (key != "fill-factor" || _pixelSampling.SelectedIndex != 0);

    private static bool ValidNumber(NumericUpDown input, bool integer)
    {
        if (input.Value is not { } value || value < input.Minimum || value > input.Maximum
            || integer && decimal.Truncate(value) != value) return false;
        // Collapsed controls loaded from a saved experiment may not have a text presenter yet.
        if (input.Text is null) return !input.IsLoaded;
        return decimal.TryParse(input.Text, input.ParsingNumberStyle, input.NumberFormat, out var parsed)
            && parsed >= input.Minimum && parsed <= input.Maximum && (!integer || decimal.Truncate(parsed) == parsed)
            && (parsed == value || input.Text == value.ToString(input.FormatString, input.NumberFormat));
    }

    private void OnNumberTextChanged(string key, NumericUpDown input)
    {
        if (_applyingSensor || _closed) return;
        if (key.StartsWith("sampling-", StringComparison.Ordinal)) { UpdateSamplingControls(); return; }
        if (NumberUsedByRun(key) && !ValidNumber(input, IntegerNumber(key)))
            Invalidate("请输入有效数值。");
        else UpdateAvailability();
    }

    private sealed class CameraNumberInput : NumericUpDown
    {
        internal bool IntegerOnly { get; init; }
        protected override Type StyleKeyOverride => typeof(NumericUpDown);

        protected override void OnLostFocus(FocusChangedEventArgs e)
        {
            var text = Text;
            var invalid = !ValidNumber(this, IntegerOnly);
            base.OnLostFocus(e);
            if (invalid && text is not null) Text = text;
        }
    }
}
