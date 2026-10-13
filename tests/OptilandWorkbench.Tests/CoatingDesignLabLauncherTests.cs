using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class IntegratedLaboratoryTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OptilandWorkbench.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Fact]
    public async Task CommandsReuseOwnedInProcessWindowsAndCloseTogether()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(IntegratedLaboratoryTests));
        await session.Dispatch(async () =>
        {
            var owner = new Window();
            owner.Show();
            var modules = new IntegratedLaboratories(owner);
            try
            {
                var initial = modules.OpenInitialStructure();
                var coating = modules.OpenCoatingDesign();
                Assert.Same(initial, modules.OpenInitialStructure());
                Assert.Same(coating, modules.OpenCoatingDesign());
                Assert.Same(owner, initial.Owner);
                Assert.Same(owner, coating.Owner);
                Assert.Equal(2, owner.OwnedWindows.Count);
                await initial.Ready;
                Assert.True(await modules.TryCloseAsync());
                Assert.Empty(owner.OwnedWindows);
                Assert.False(initial.IsVisible);
                Assert.False(coating.IsVisible);
                var reopened = modules.OpenCoatingDesign();
                Assert.NotSame(coating, reopened);
                Assert.True(await modules.TryCloseAsync());
            }
            finally { owner.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OpeningCoatingDoesNotReplaceTheHostThemeOrNumericSettings()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(IntegratedLaboratoryTests));
        await session.Dispatch(async () =>
        {
            var app = Assert.IsType<global::OptilandWorkbench.App.App>(Avalonia.Application.Current);
            app.ApplyTheme("Dark");
            var theme = app.RequestedThemeVariant;
            var owner = new Window(); owner.Show();
            var modules = new IntegratedLaboratories(owner);
            try
            {
                modules.OpenCoatingDesign();
                Assert.Same(theme, app.RequestedThemeVariant);
                Assert.True(await modules.TryCloseAsync());
            }
            finally { owner.Close(); }
        }, CancellationToken.None);
    }
}
