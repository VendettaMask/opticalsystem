using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class OpticalAssemblyWindow
{
    private string _statusSource = "";
    private Func<string>? _imageDescription;

    private void SetStatus(string source)
    {
        _statusSource = source;
        _status.Text = TranslateDiagnostic(source);
    }

    private void SetImageInfo(Func<string> description)
    {
        _imageDescription = description;
        _imageInfo.Text = description();
    }

    private void OnLanguageChanged(object? sender, EventArgs args) => OnUi(() =>
    {
        // Refresh display properties in place: selection, ray data and calculations stay intact.
        foreach (var row in _grid.ItemsSource?.Cast<TableRow>() ?? []) row.RefreshLanguage();
        _status.Text = TranslateDiagnostic(_statusSource);
        if (_imageDescription is not null) _imageInfo.Text = _imageDescription();
    });

    private static string KindLabel(AssemblyConjugateKind kind) =>
        UiLocalization.Text(kind == AssemblyConjugateKind.Vertex ? "顶点像" : "球心像");

    private static string ImageDescription(OpticalAssemblyRow row, AssemblyReticleResult image)
    {
        var position = image.HeadPosition is { } head
            ? UiLocalization.Format("附加物镜位置 {0} mm（同一基准）", Format(head))
            : UiLocalization.Format("自准直仪物镜位置 {0} mm（同一基准）", Format(image.CollimatorPosition));
        var scale = UiLocalization.Text(image.MatchesSelectedConjugate ? "近轴倍率" : "主光线比例");
        return UiLocalization.Format("表面 {0} · {1} · {2}\n{3} {4}×；名义像长 {5} mm\n{6}\n返回 {7} / {8}，落入探测面 {9}\n{10}",
            row.SurfaceNumber, row.SurfaceLabel, KindLabel(row.Kind), scale, Format(image.Magnification),
            Format(image.ImageLength), position, image.ReturnedRays.ToString("N0"), image.LaunchedRays.ToString("N0"),
            image.RecordedRays.ToString("N0"), TranslateDiagnostic(image.Message));
    }

    internal static string TranslateDiagnostic(string source)
    {
        // The Core returns these fixed mode prefixes followed by a fixed outcome sentence.
        // Translate only this documented composition, never arbitrary user-name fragments.
        foreach (var prefix in new[]
        {
            "附加测量物镜模式。", "无穷远模式：平行出射，所选共轭为 ∞。",
            "无穷远模式：所选共轭为有限位置，当前未对焦到此共轭；返回像可能模糊或受遮挡。"
        })
            if (source.StartsWith(prefix, StringComparison.Ordinal))
                return UiLocalization.Text(prefix) + (UiLocalization.CurrentLanguage == UiLocalization.English ? " " : "")
                    + UiLocalization.Text(source[prefix.Length..]);
        return UiLocalization.Text(source);
    }
}
