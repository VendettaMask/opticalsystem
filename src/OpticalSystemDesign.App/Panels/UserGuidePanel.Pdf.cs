using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class UserGuidePanel
{
    private readonly Button _pdfExport = new() { Name = "UserGuideExportPdf", Content = "PDF" };
    private readonly Button _pdfCancel = new() { Name = "UserGuideCancelPdf", IsVisible = false };
    private readonly TextBlock _pdfStatus = new() { Name = "UserGuidePdfStatus", TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 250, FontSize = 12 };
    private CancellationTokenSource? _pdfCancellation;
    private bool _pdfPicking;
    private Control PdfControls()
    {
        _pdfExport.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            foreach (var scope in Enum.GetValues<UserGuidePdfScope>())
            {
                var item = new MenuItem { Header = PdfScopeText(scope), IsEnabled = scope != UserGuidePdfScope.CurrentChapter || _selectedChapterId is not null };
                item.Click += async (_, _) => await ExportPdfAsync(scope);
                menu.Items.Add(item);
            }
            menu.Open(_pdfExport);
        };
        _pdfCancel.Click += (_, _) => _pdfCancellation?.Cancel();
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _pdfExport, _pdfCancel, _pdfStatus }
        };
    }
    private string PdfScopeText(UserGuidePdfScope scope) => Current(scope switch
    {
        UserGuidePdfScope.CurrentChapter => T("导出当前章节", "Export current chapter", "現在の章を出力"),
        UserGuidePdfScope.Calculations => T("导出全部计算原理", "Export all calculation chapters", "計算原理をすべて出力"),
        _ => T("导出完整指南", "Export the complete guide", "ガイド全体を出力")
    });
    private async Task ExportPdfAsync(UserGuidePdfScope scope)
    {
        if (_pdfCancellation is not null || _pdfPicking || TopLevel.GetTopLevel(this) is not { } top) return;
        var chapters = ChaptersForPdf(scope);
        var language = CurrentLanguage();
        IStorageFile? target;
        _pdfPicking = true;
        try
        {
            target = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = PdfScopeText(scope),
                SuggestedFileName = $"Optical-System-Design-Guide-{language}.pdf",
                DefaultExtension = "pdf",
                FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"], MimeTypes = ["application/pdf"] }]
            });
        }
        catch (Exception exception)
        {
            _pdfStatus.Text = Current(T("无法选择 PDF 保存位置：", "Unable to select PDF destination: ", "PDF 保存先を選択できません：")) + exception.Message;
            return;
        }
        finally { _pdfPicking = false; }
        if (target is null) return;
        using var cancellation = new CancellationTokenSource(); _pdfCancellation = cancellation;
        _pdfExport.IsEnabled = false; _pdfCancel.IsVisible = true;
        ToolTip.SetTip(_pdfExport, Current(T("PDF 正在生成，可取消后重试", "PDF export is running; cancel to retry", "PDF 作成中です。取消後に再試行できます")));
        _pdfStatus.Text = Current(T("正在生成 PDF…", "Generating PDF…", "PDF を作成中…"));
        try
        {
            var result = await Task.Run(() => UserGuidePdfExporter.Export(target.Path.LocalPath, chapters, language, cancellation.Token));
            _pdfStatus.Text = Current(T($"已导出 {result.Pages} 页", $"Exported {result.Pages} pages", $"{result.Pages} ページを出力しました"));
            ToolTip.SetTip(_pdfStatus, target.Path.LocalPath);
        }
        catch (OperationCanceledException) { _pdfStatus.Text = Current(T("PDF 导出已取消", "PDF export canceled", "PDF 出力を取り消しました")); }
        catch (Exception exception) { _pdfStatus.Text = Current(T("PDF 导出失败：", "PDF export failed: ", "PDF 出力失敗：")) + exception.Message; }
        finally { _pdfCancellation = null; _pdfExport.IsEnabled = true; _pdfCancel.IsVisible = false; ToolTip.SetTip(_pdfExport, null); }
    }
    internal IReadOnlyList<GuideChapter> ChaptersForPdf(UserGuidePdfScope scope) => _chapters.Where(chapter => scope switch
    {
        UserGuidePdfScope.CurrentChapter => chapter.Id == _selectedChapterId,
        UserGuidePdfScope.Calculations => chapter.Id.StartsWith("calculation-", StringComparison.Ordinal),
        _ => true
    }).ToArray();
}
