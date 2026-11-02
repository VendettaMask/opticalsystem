using System.Globalization;
using Avalonia.Controls;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private TextBox BuildReferenceInput()
    {
        var reference = new TextBox(); _numbers.Add("reference", reference);
        reference.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty || _loading) return;
            _hasUnsavedChanges = true; _uiRevision++;
            // Reference units are metadata, not a new physical spectrum or a changed target.
            var current = _session.Current;
            if (double.TryParse(reference.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && double.IsFinite(value) && value > 0)
                _session.Replace(current with { ReferenceWavelengthNm = current.ReferenceWavelengthNm is null && value == current.Target.CenterNm ? null : value });
            else _session.Cancel();
            RefreshLayerOpticalThickness(); RefreshStackGeometry(); RecordEditorHistory("reference-wavelength");
        };
        return reference;
    }
}
