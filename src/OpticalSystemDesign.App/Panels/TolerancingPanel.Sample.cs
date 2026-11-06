using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class TolerancingPanel
{
    private readonly Button _saveWorstSample = CommandButton("file-save", "保存最差样件");

    private static FilePickerFileType SampleFileType() =>
        new("公差样件") { Patterns = ["*.startolsample.json"] };

    private Button CreateOpenSampleButton()
    {
        var button = CommandButton("file-open", "打开样件复算");
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await OpenSampleAsync(); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private async Task SaveWorstSampleAsync()
    {
        if (_lastResult is not { WorstSample: { } sample } result || result.SourceRevision != _events.Revision
            || TopLevel.GetTopLevel(this) is not { } owner) return;
        try
        {
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存最差样件",
                SuggestedFileName = $"trial-{sample.Recorded.Trial:D4}.startolsample.json",
                FileTypeChoices = [SampleFileType()]
            });
            if (file?.TryGetLocalPath() is not { } path || !ReferenceEquals(result, _lastResult)
                || result.SourceRevision != _events.Revision) return;
            await _tolerancing.SaveSampleAsync(sample, path);
            _summary.Text = "已保存公差样件。";
        }
        catch (Exception error)
        {
            _summary.Text = $"无法保存公差样件：{error.Message}";
        }
    }

    private async Task OpenSampleAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        try
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "打开样件复算",
                FileTypeFilter = [SampleFileType()]
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            _summary.Text = UiLocalization.Text("正在复算公差样件…");
            var replay = await _tolerancing.ReplaySampleAsync(path);
            if (_disposed) return;
            _summary.Text = UiLocalization.Text(replay.MatchesRecorded ? "复算与记录一致。" : "复算与记录存在差异。");
            await ShowSampleReplayAsync(owner, replay);
        }
        catch (Exception error)
        {
            _summary.Text = $"无法复算公差样件：{error.Message}";
        }
    }

    private static Task ShowSampleReplayAsync(Window owner, ToleranceSampleReplayDto replay)
    {
        var close = new Button { Content = "关闭", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var text = new TextBox
        {
            Text = BuildSampleReplayText(replay), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(close, Avalonia.Controls.Dock.Bottom);
        var window = new Window
        {
            Title = "公差样件复算", Width = 720, Height = 540, MinWidth = 440, MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Margin = new Thickness(12), Children = { close, text } }
        };
        close.Click += (_, _) => window.Close();
        ThemeChrome.ApplyDialogDecoration(window);
        UiLocalization.Attach(window);
        DisplayTypography.Apply(window);
        return window.ShowDialog(owner);
    }

    internal static string BuildSampleReplayText(ToleranceSampleReplayDto replay)
    {
        var recorded = replay.Sample.Recorded;
        var actual = replay.Replayed;
        var builder = new StringBuilder();
        builder.AppendLine(UiLocalization.Format("试验 {0}，种子 {1}", actual.Trial, replay.Sample.Request.Seed));
        builder.AppendLine(UiLocalization.Text(replay.MatchesRecorded ? "复算与记录一致。" : "复算与记录存在差异。"));
        if (replay.Sample.Request.Criterion == ToleranceCriterion.SystemMerit)
            builder.AppendLine(UiLocalization.Text("最终验收使用系统评价函数，按加权残差上限判定。"));
        if (replay.Sample.Request.MeritTable is { } table)
            builder.AppendLine(UiLocalization.Format("最终评价表 {0}", table.Name));
        builder.AppendLine(UiLocalization.Format("记录的补偿前／后评价值 {0} / {1}", recorded.Merit, recorded.CompensatedMerit));
        builder.AppendLine(UiLocalization.Format("复算的补偿前／后评价值 {0} / {1}", actual.Merit, actual.CompensatedMerit));
        AppendFields(builder, "补偿前", actual.UncompensatedFields);
        AppendFields(builder, "补偿后", actual.Fields);
        AppendStages(builder, actual.Stages);
        AppendConfigurations(builder, "补偿前", actual.UncompensatedConfigurations);
        AppendConfigurations(builder, "补偿后", actual.Configurations);
        AppendCompensators(builder, "最终", actual.Compensators);
        return builder.ToString();
    }
}
