using Avalonia;
using Avalonia.Controls;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Theming;

internal enum AnalysisArtworkKind { Celestial, Alchemy, Arcane, Manuscript }

/// <summary>Decorative vocabulary only; typed analysis metadata selects it, never translated names.</summary>
internal sealed class AnalysisArtwork : AvaloniaObject
{
    internal static readonly AttachedProperty<AnalysisArtworkKind> KindProperty =
        AvaloniaProperty.RegisterAttached<AnalysisArtwork, Control, AnalysisArtworkKind>(
            "Kind", AnalysisArtworkKind.Celestial, inherits: true);
    internal static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<AnalysisArtwork, Control, bool>("Enabled", false, inherits: true);

    internal static AnalysisArtworkKind GetKind(Control control) => control.GetValue(KindProperty);
    internal static void SetKind(Control control, AnalysisArtworkKind kind)
    {
        control.SetValue(KindProperty, kind); control.SetValue(EnabledProperty, true);
    }

    internal static AnalysisArtworkKind For(AnalysisViewDto view) => view.PresentationKind switch
    {
        AnalysisPresentationKind.FftPsf or AnalysisPresentationKind.HuygensPsf or AnalysisPresentationKind.Foucault
            => AnalysisArtworkKind.Alchemy,
        AnalysisPresentationKind.WavefrontMap or AnalysisPresentationKind.OpticalPathDifference
            or AnalysisPresentationKind.Interferogram => AnalysisArtworkKind.Arcane,
        AnalysisPresentationKind.SpotDiagram or AnalysisPresentationKind.ThroughFocusSpot
            or AnalysisPresentationKind.MatrixSpot or AnalysisPresentationKind.ConfigurationMatrixSpot
            or AnalysisPresentationKind.FullFieldSpot or AnalysisPresentationKind.RayFan
            or AnalysisPresentationKind.PupilAberration or AnalysisPresentationKind.FullFieldAberration
            or AnalysisPresentationKind.SeidelDiagram or AnalysisPresentationKind.FieldCurvature
            or AnalysisPresentationKind.FieldCurvatureAndDistortion => AnalysisArtworkKind.Manuscript,
        _ when view.Series.Any(series => series.YQuantity == AnalysisAxisQuantity.EnergyFraction) => AnalysisArtworkKind.Alchemy,
        _ => AnalysisArtworkKind.Celestial
    };
}
