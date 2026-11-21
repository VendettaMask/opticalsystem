using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Tests;

var evidence = System.IO.Path.GetFullPath(args[0]);
Directory.CreateDirectory(evidence);
var capture = System.IO.Path.Combine(evidence, "captures");
Directory.CreateDirectory(capture);
using var session = SafeHeadlessUnitTestSession.StartNew(typeof(OpticalSystemDesign.App.App));
await session.Dispatch(() =>
{
    var chapters = new UserGuidePanel([]).ChaptersForPdf(UserGuidePdfScope.CompleteGuide);
    var calculations = chapters.Where(c => c.Id.StartsWith("calculation-", StringComparison.Ordinal)).ToArray();
    var formulas = calculations.SelectMany(c => c.Sections).Where(s => s.Formula is not null).Select(s => s.Formula!).Distinct().Order().ToArray();
    File.WriteAllText(System.IO.Path.Combine(evidence, "content.json"), JsonSerializer.Serialize(new
    {
        calculationChapters = calculations.Length,
        formulaCount = formulas.Length,
        analysisCount = WorkbenchAnalysisCatalog.Descriptors.Count,
        formulas,
        calculationIndex = calculations.Select(c => new { c.Id, c.Title }),
        updatedChapters = chapters.Where(c => c.Id is "sequential-model" or "analyses" or "calculation-pupil-sampling")
    }, new JsonSerializerOptions { WriteIndented = true }));
    if (args.Length > 1 && args[1] == "audit-only")
    {
        Console.WriteLine($"Retained {calculations.Length} calculation chapters, {formulas.Length} formulas and {WorkbenchAnalysisCatalog.Descriptors.Count} analysis mappings.");
        return;
    }

    foreach (var language in new[] { UiLocalization.Chinese, UiLocalization.English, UiLocalization.Japanese })
    foreach (var width in new[] { 1440, 520 })
    {
        UiLocalization.Apply(language);
        var theme = width == 1440 ? "Light" : "Dark";
        ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
        var panel = new UserGuidePanel([]);
        var window = new Window { Width = width, Height = 920, Content = panel };
        try
        {
            window.Show(); Tick(window);
            Named<ComboBox>(panel, "UserGuideKindFilter").SelectedIndex = 0; Tick(window);
            foreach (var (id, section) in new[] { ("sequential-model", 3), ("analyses", 2), ("calculation-pupil-sampling", 1) })
            {
                Named<TextBox>(panel, "UserGuideSearch").Text = id; Tick(window);
                var tree = Named<TreeView>(panel, "UserGuideTree");
                tree.SelectedItem = tree.ItemsSource!.Cast<TreeViewItem>()
                    .SelectMany(root => root.ItemsSource!.Cast<TreeViewItem>())
                    .Single(item => item.Tag is UserGuidePanel.GuideChapter candidate && candidate.Id == id);
                Tick(window);
                var chapter = chapters.Single(c => c.Id == id);
                if (Named<TextBlock>(panel, "UserGuideTitle").Text != chapter.Title.Value(language))
                    throw new InvalidOperationException("Wrong selected chapter: " + id);
                Named<TextBox>(panel, "UserGuideSearch").Text = ""; Tick(window);
                if (width < 760)
                    Named<Expander>(panel, "UserGuideInlineContents").IsExpanded = true;
                Tick(window);
                var link = Named<StackPanel>(panel, "UserGuideSectionLinks").Children.OfType<Button>().ElementAt(section);
                link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                var scroller = Named<ScrollViewer>(panel, "UserGuideReaderScroll");
                if (scroller.Extent.Width > scroller.Viewport.Width + 1)
                    throw new InvalidOperationException("Horizontal overflow: " + id);
                using var bitmap = new RenderTargetBitmap(new PixelSize(width, 920), new Vector(96, 96));
                bitmap.Render(window);
                using var output = File.Create(System.IO.Path.Combine(capture, $"updated-{id}-{language}-{theme}-{width}.png"));
                bitmap.Save(output, new PngBitmapEncoderOptions());
            }
        }
        finally { window.Close(); }
    }
    UiLocalization.Apply(UiLocalization.Chinese);
    Console.WriteLine($"Retained {calculations.Length} calculation chapters, {formulas.Length} formulas and {WorkbenchAnalysisCatalog.Descriptors.Count} analysis mappings; captured 18 updated reader views.");
}, CancellationToken.None);

static T Named<T>(Control root, string name) where T : Control =>
    root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
static void Tick(Window window)
{
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
}
