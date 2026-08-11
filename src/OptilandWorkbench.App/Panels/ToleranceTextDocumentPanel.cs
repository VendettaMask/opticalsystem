using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed class ToleranceTextDocumentPanel : UserControl
{
    private readonly Func<string> _textProvider;
    private readonly string _title;
    private readonly string _suggestedFileName;
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true,
        FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
        IsReadOnly = true,
        TextWrapping = TextWrapping.NoWrap
    };

    public ToleranceTextDocumentPanel(
        string title,
        string suggestedFileName,
        Func<string> textProvider)
    {
        _title = title;
        _suggestedFileName = suggestedFileName;
        _textProvider = textProvider;
        _text.BindThemeResource(TextBox.BackgroundProperty, ThemeResourceBindings.Surface);
        _text.BindThemeResource(TextBox.ForegroundProperty, ThemeResourceBindings.TextPrimary);

        var refresh = CommandButton("refresh-cw", "刷新");
        refresh.Click += (_, _) => Refresh();
        var copy = CommandButton("copy", "复制文本");
        copy.Click += async (_, _) => await CopyAsync();
        var export = CommandButton("download", "导出文本");
        export.Click += async (_, _) => await ExportAsync();

        var toolbar = new Border
        {
            Padding = new Thickness(8, 6),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Children = { refresh, copy, export }
            }
        };
        toolbar.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        toolbar.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);

        var page = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                toolbar,
                new Border
                {
                    Padding = new Thickness(12),
                    Child = _text
                }
            }
        };
        Grid.SetRow(page.Children[1], 1);
        Content = page;
        Refresh();
    }

    public void Refresh()
    {
        _text.Text = _textProvider();
    }

    private async Task CopyAsync()
    {
        Refresh();
        if (TopLevel.GetTopLevel(this)?.Clipboard is IClipboard clipboard)
        {
            await clipboard.SetTextAsync(_text.Text ?? string.Empty);
        }
    }

    private async Task ExportAsync()
    {
        Refresh();
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"导出{_title}",
            SuggestedFileName = _suggestedFileName,
            DefaultExtension = "txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("文本报告") { Patterns = new[] { "*.txt" } }
            }
        });
        if (file is not null)
        {
            await File.WriteAllTextAsync(file.Path.LocalPath, _text.Text ?? string.Empty);
        }
    }

    private static Button CommandButton(string icon, string text) => new()
    {
        Content = new LocalIconLabel(icon, text),
        MinWidth = 94,
        Margin = new Thickness(0, 0, 6, 0)
    };
}
