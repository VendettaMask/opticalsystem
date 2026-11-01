using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private string? _plotDefaultsWarning;
    private Flyout EvaluationSettingsFlyout()
    {
        var panel = new StackPanel { Width = 420, Spacing = 10 };
        var initializing = false; Button? resetToFactory = null;
        var angles = new WrapPanel(); var angleBoxes = new List<TextBox>(); var angleSequence = 0;
        var add = new Button { Content = "+", Width = 28, Padding = new Thickness(2) };
        var minimum = new TextBox { MinWidth = 82 }; var maximum = new TextBox { MinWidth = 82 }; var step = new TextBox { MinWidth = 82 };
        var yMinimum = new TextBox { MinWidth = 68 }; var yMaximum = new TextBox { MinWidth = 68 };
        var waveUnit = new ComboBox { ItemsSource = new[] { "nm", "µm" }, SelectedIndex = 0, MinWidth = 75 };
        var valueUnit = new ComboBox { ItemsSource = new[] { "%", "1", "OD" }, SelectedIndex = 0, MinWidth = 75 };
        var auto = new CheckBox { Content = UiLocalization.Text("自动"), IsChecked = false };
        var grid = new CheckBox { Content = "网格", IsChecked = true };
        void Edited()
        {
            if (initializing || resetToFactory is null) return;
            var isFactory = false;
            try
            {
                var settings = Read(); var factory = CoatingPlotDefaults.Factory;
                isFactory = settings with { Sampling = factory.Sampling } == factory
                    && settings.Sampling.MinimumNm == factory.Sampling.MinimumNm
                    && settings.Sampling.MaximumNm == factory.Sampling.MaximumNm
                    && settings.Sampling.StepNm == factory.Sampling.StepNm
                    && settings.Sampling.AnglesDegrees.SequenceEqual(factory.Sampling.AnglesDegrees);
            }
            catch (ArgumentException) { }
            resetToFactory.IsEnabled = !isFactory;
            ToolTip.SetTip(resetToFactory, UiLocalization.Text(isFactory ? "当前已是出厂评估设置" : "恢复出厂评估设置"));
        }
        AutomationProperties.SetName(add, "添加评估角度");
        void AddAngle(double value)
        {
            var box = new TextBox { Text = value.ToString("R", CultureInfo.CurrentCulture), Width = 56, Margin = new Thickness(0, 0, 4, 3) };
            AutomationProperties.SetName(box, "评估角度 " + (++angleSequence)); angleBoxes.Add(box);
            box.TextChanged += (_, _) => Edited();
            var entry = new StackPanel { Orientation = Orientation.Horizontal, Children = { box, new TextBlock { Text = "°", VerticalAlignment = VerticalAlignment.Center } } };
            if (angleBoxes.Count > 1)
            {
                var remove = new Button { Content = "×", Padding = new Thickness(4), Margin = new Thickness(2, 0, 4, 0) };
                remove.Click += (_, _) => { angleBoxes.Remove(box); angles.Children.Remove(entry); add.IsEnabled = angleBoxes.Count < 16; Edited(); };
                entry.Children.Add(remove);
            }
            angles.Children.Insert(angles.Children.Count - 1, entry); add.IsEnabled = angleBoxes.Count < 16; Edited();
        }
        angles.Children.Add(add); add.Click += (_, _) => AddAngle(Math.Min(85, Parse(angleBoxes.LastOrDefault()?.Text) + 5));
        foreach (var (control, name) in new (Control, string)[] { (minimum, "评估波长下限"), (maximum, "评估波长上限"), (step, "评估步长"),
                     (yMinimum, "评估纵轴下限"), (yMaximum, "评估纵轴上限"), (waveUnit, "评估波长单位"), (valueUnit, "评估纵轴单位"), (auto, "评估纵轴自动") })
            AutomationProperties.SetName(control, name);
        Control Row(string label, params Control[] fields)
        {
            var row = new Grid { ColumnDefinitions = new("85,*"), ColumnSpacing = 10 };
            row.Children.Add(new TextBlock { Text = UiLocalization.Text(label), Tag = label, VerticalAlignment = VerticalAlignment.Center });
            var content = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var field in fields) { field.Margin = new Thickness(0, 0, 6, 0); content.Children.Add(field); }
            Grid.SetColumn(content, 1); row.Children.Add(content); return row;
        }
        panel.Children.Add(Row("入射角", angles)); panel.Children.Add(new Separator());
        panel.Children.Add(Row("λ", minimum, new TextBlock { Text = "–", VerticalAlignment = VerticalAlignment.Center }, maximum, waveUnit));
        var stepRow = (Grid)Row("步长", step); var stepLabel = (TextBlock)stepRow.Children[0]; stepLabel.Tag = null; panel.Children.Add(stepRow);
        panel.Children.Add(Row("Y", auto, yMinimum, new TextBlock { Text = "–", VerticalAlignment = VerticalAlignment.Center }, yMaximum, valueUnit));
        AutomationProperties.SetName(grid, "光谱网格");
        grid.IsCheckedChanged += (_, _) => { _plot.SetGrid(grid.IsChecked == true); Edited(); }; panel.Children.Add(grid);
        panel.Children.Add(new Separator());
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap }; message.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextError); panel.Children.Add(message);
        var buttons = new WrapPanel();
        var apply = new Button { Content = UiLocalization.Text("应用"), Margin = new Thickness(0, 0, 6, 0) };
        var save = new Button { Content = UiLocalization.Text("另存为默认"), Margin = new Thickness(0, 0, 6, 0) };
        var restore = new Button { Content = UiLocalization.Text("恢复默认") };
        resetToFactory = restore;
        foreach (var (button, name) in new[] { (apply, "应用评估设置"), (save, "另存为评估默认"), (restore, "恢复评估默认") })
        { AutomationProperties.SetName(button, name); buttons.Children.Add(button); }
        panel.Children.Add(buttons);
        var flyout = new Flyout { Content = panel };
        foreach (var box in new[] { minimum, maximum, step, yMinimum, yMaximum }) box.TextChanged += (_, _) => Edited();
        CoatingPlotDefaults Read()
        {
            var multiplier = waveUnit.SelectedIndex == 1 ? 1000 : 1;
            var options = new AnalysisSpectrumOptions(Parse(minimum.Text) * multiplier, Parse(maximum.Text) * multiplier,
                Parse(step.Text) * multiplier, angleBoxes.Select(b => Parse(b.Text)).ToArray());
            var settings = new CoatingPlotDefaults(options, (CoatingWavelengthUnit)waveUnit.SelectedIndex, (CoatingValueUnit)valueUnit.SelectedIndex,
                auto.IsChecked == true, Parse(yMinimum.Text), Parse(yMaximum.Text), grid.IsChecked == true); settings.Validate(); return settings;
        }
        void Populate(CoatingPlotDefaults settings)
        {
            initializing = true; angleSequence = 0; angleBoxes.Clear(); angles.Children.Clear(); angles.Children.Add(add);
            foreach (var a in settings.Sampling.AnglesDegrees) AddAngle(a);
            waveUnit.SelectedIndex = (int)settings.WavelengthUnit; valueUnit.SelectedIndex = (int)settings.ValueUnit;
            var divisor = waveUnit.SelectedIndex == 1 ? 1000 : 1;
            minimum.Text = (settings.Sampling.MinimumNm / divisor).ToString("R", CultureInfo.CurrentCulture);
            maximum.Text = (settings.Sampling.MaximumNm / divisor).ToString("R", CultureInfo.CurrentCulture);
            step.Text = (settings.Sampling.StepNm / divisor).ToString("R", CultureInfo.CurrentCulture);
            yMinimum.Text = settings.MinimumY.ToString("R", CultureInfo.CurrentCulture); yMaximum.Text = settings.MaximumY.ToString("R", CultureInfo.CurrentCulture);
            auto.IsChecked = settings.AutomaticY; grid.IsChecked = settings.ShowGrid; yMinimum.IsEnabled = yMaximum.IsEnabled = !settings.AutomaticY;
            stepLabel.Text = UiLocalization.Text("步长") + (waveUnit.SelectedIndex == 1 ? " (µm)" : " (nm)");
            initializing = false; Edited();
        }
        waveUnit.SelectionChanged += (_, _) =>
        {
            if (initializing) return;
            var factor = waveUnit.SelectedIndex == 1 ? .001 : 1000;
            foreach (var box in new[] { minimum, maximum, step }) box.Text = (Parse(box.Text) * factor).ToString("R", CultureInfo.CurrentCulture);
            stepLabel.Text = UiLocalization.Text("步长") + (waveUnit.SelectedIndex == 1 ? " (µm)" : " (nm)"); Edited();
        };
        valueUnit.SelectionChanged += (_, _) => { if (!initializing) { yMinimum.Text = "0"; yMaximum.Text = valueUnit.SelectedIndex == 0 ? "100" : valueUnit.SelectedIndex == 1 ? "1" : "12"; Edited(); } };
        auto.IsCheckedChanged += (_, _) => { yMinimum.IsEnabled = yMaximum.IsEnabled = auto.IsChecked != true; Edited(); };
        async Task Commit(bool persist, CoatingPlotDefaults? replacement = null)
        {
            try
            {
                var settings = replacement ?? Read(); settings.Apply(_plot);
                if (persist) { settings.Save(CoatingPlotDefaults.DefaultPath); _plotDefaultsWarning = null; }
                Populate(settings); message.Text = ""; await _plot.PendingEvaluation;
                if (_plot.DisplayError is { } error) message.Text = error;
                else flyout.Hide();
            }
            catch (Exception error) when (error is ArgumentException or System.IO.IOException or UnauthorizedAccessException) { message.Text = error.Message; }
        }
        apply.Click += async (_, _) => await Commit(false);
        save.Click += async (_, _) => await Commit(true);
        restore.Click += async (_, _) => await Commit(true, CoatingPlotDefaults.Factory);
        flyout.Opening += (_, _) =>
        {
            var range = _plot.Range;
            Populate(new(_plot.AnalysisOptions, _plot.WavelengthUnit, _plot.ValueUnit, _plot.AutomaticY, range.MinimumY, range.MaximumY, _plot.ShowGrid));
            message.Text = _plotDefaultsWarning ?? "";
            foreach (var label in panel.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Tag is string)) label.Text = UiLocalization.Text((string)label.Tag!);
            auto.Content = UiLocalization.Text("自动"); grid.Content = UiLocalization.Text("网格"); grid.IsChecked = _plot.ShowGrid;
            apply.Content = UiLocalization.Text("应用"); save.Content = UiLocalization.Text("另存为默认"); restore.Content = UiLocalization.Text("恢复默认");
        };
        return flyout;
    }
    private static double Parse(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ? value : double.NaN;
}
