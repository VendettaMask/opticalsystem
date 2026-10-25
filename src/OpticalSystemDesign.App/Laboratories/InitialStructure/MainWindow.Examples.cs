using Avalonia.Controls;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

public sealed partial class MainWindow
{
    private readonly IReadOnlyList<AutoLensExamplePreset> _officialExamples = new[] { AutoLensReferenceExamples.Mobile() }.Concat(AutoLensOfficialExamples.Create()).ToArray();
    private readonly ComboBox _officialExample = Named(new ComboBox { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch },
        "OfficialExample", "AutoLens 官方目标预设");
    private readonly Button _loadOfficialExample = Command("载入预设", "LoadOfficialExample");
    private readonly TextBlock _officialExampleNotes = Named(Text("选择官方目标预设；载入不会自动开始搜索。"), "OfficialExampleNotes", "预设来源与适配说明");

    private Control BuildOfficialExamples()
    {
        _officialExample.ItemsSource = _officialExamples.Select(example => example.Name).ToArray();
        _officialExample.SelectedIndex = 0;
        _officialExample.SelectionChanged += (_, _) =>
        {
            if (_officialExample.SelectedIndex is var index && index >= 0 && index < _officialExamples.Count)
                _officialExampleNotes.Text = _officialExamples[index].Notes;
        };
        _officialExampleNotes.Text = _officialExamples[0].Notes;
        Hook(_loadOfficialExample, () =>
        {
            var index = _officialExample.SelectedIndex;
            if (index < 0 || index >= _officialExamples.Count)
                throw new ArgumentException("请先选择官方目标预设。");
            var example = _officialExamples[index];
            var computeMode = _computeMode.SelectedIndex;
            ClearResults();
            ApplySpecification(example.Specification, example.Options);
            _computeMode.SelectedIndex = computeMode;
            _status.Text = "已载入目标预设；尚未运行，不代表设计达标。";
            return Task.CompletedTask;
        });
        _officialExampleNotes.FontSize = Services.DisplayTypography.CompactBody;
        ToolTip.SetTip(_officialExample, "AutoLens 官方目标预设");
        return Section("从预设开始", "sparkles", _officialExample, _loadOfficialExample,
            Hint("仅载入目标参数，不自动开始搜索。"),
            Disclosure("来源与适配说明", "OfficialExampleSource", _officialExampleNotes,
                Hint("原版默认流程和 README 目标分别保留来源；运行预算见当前参数，不代表设计已经达标。"),
                Hint("https://github.com/AI4Optics/AutoLens/blob/" + AutoLensOfficialExamples.UpstreamCommit + "/1_autolens.py"),
                Hint(AutoLensOfficialExamples.SourceUrl)));
    }
}
