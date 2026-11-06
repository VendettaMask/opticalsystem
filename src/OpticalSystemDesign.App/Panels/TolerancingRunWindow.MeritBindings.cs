using Avalonia.Controls;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class TolerancingRunWindow
{
    private readonly Func<string, ToleranceMeritTableDto>? _captureMeritTable;
    private MeritBindingControls? _finalMeritBinding;

    private Control FinalMeritSection()
    {
        _finalMeritBinding = CreateMeritBinding("最终验收");
        return _systemMeritNote = Section("最终验收评价表", _finalMeritBinding.View);
    }

    private MeritBindingControls CreateMeritBinding(string defaultName, ToleranceMeritTableDto? initial = null)
    {
        var name = new TextBox { MaxLength = 80 };
        Avalonia.Automation.AutomationProperties.SetName(name, "评价表名称");
        var status = Note("");
        var bind = Button("绑定当前评价表");
        var clear = Button("改用活动评价表");
        var content = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                status, Row("评价表名称", name),
                new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Children = { bind, clear } }
            }
        };
        var controls = new MeritBindingControls(content, name, status, clear, defaultName);
        SetMeritBinding(controls, initial);
        ControlAvailability.Set(bind, _captureMeritTable is not null, "请从公差数据编辑器打开此设置。");
        bind.Click += (_, _) =>
        {
            try
            {
                SetMeritBinding(controls, _captureMeritTable?.Invoke(name.Text?.Trim() ?? ""));
                ShowSettingsError("");
            }
            catch (ArgumentException error) { ShowSettingsError(error.Message); }
        };
        clear.Click += (_, _) => SetMeritBinding(controls, null);
        return controls;
    }

    private static void SetMeritBinding(MeritBindingControls controls, ToleranceMeritTableDto? table)
    {
        controls.Table = table;
        controls.Name.Text = table?.Name ?? UiLocalization.Text(controls.DefaultName);
        controls.Status.Text = table is null
            ? UiLocalization.Text("使用运行时的活动评价表。绑定后可用「应用设置」关闭设置，再准备下一张表。")
            : UiLocalization.Format("已绑定 {0}（{1} 行）", table.Name, table.Rows.Count);
        controls.Clear.IsEnabled = table is not null;
    }

    private static ToleranceMeritTableDto? ReadMeritBinding(MeritBindingControls? controls)
    {
        if (controls?.Table is not { } table) return null;
        var name = controls.Name.Text?.Trim();
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("请输入绑定评价表名称。");
        return table with { Name = name };
    }

    private sealed record MeritBindingControls(Control View, TextBox Name, TextBlock Status, Button Clear, string DefaultName)
    {
        public ToleranceMeritTableDto? Table { get; set; }
    }
}
