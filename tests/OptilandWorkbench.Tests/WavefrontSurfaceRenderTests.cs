using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class WavefrontSurfaceRenderTests
{
    [Fact]
    public async Task SettingsToggleUsesConfiguredSurfaceInLightTheme()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var button = SettingsPanelChrome.CreateToggleButton();
            var window = new Window
            {
                Width = 160,
                Height = 80,
                Content = button,
                RequestedThemeVariant = ThemeVariant.Light
            };

            try
            {
                window.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                var background = Assert.IsAssignableFrom<ISolidColorBrush>(button.Background);
                var lightResources = ThemePalette.Light.ToResourceDictionary();
                var configuredSurface = Assert.IsAssignableFrom<ISolidColorBrush>(
                    lightResources[ThemeResourceBindings.SettingsSurface]);
                Assert.Equal(configuredSurface.Color, background.Color);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InitialViewDoesNotInvalidateTheActiveRenderPass()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var surface = new WavefrontSurfaceControl
            {
                Series = new AnalysisSeriesDto(
                    "Pupil X",
                    "Pupil Y",
                    new[]
                    {
                        new AnalysisPointDto(-1, -1, Value: 0.1),
                        new AnalysisPointDto(1, -1, Value: 0.2),
                        new AnalysisPointDto(-1, 1, Value: 0.3),
                        new AnalysisPointDto(1, 1, Value: 0.4)
                    },
                    AnalysisSeriesKind.Heatmap),
                DisplayAs = "表面"
            };
            var window = new Window
            {
                Width = 520,
                Height = 420,
                Content = surface
            };

            try
            {
                window.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                Assert.Equal(35, surface.ViewYawDegrees, precision: 10);
                Assert.Equal(28, surface.ViewPitchDegrees, precision: 10);
                Assert.Equal(1, surface.ViewZoom, precision: 10);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeadlessAvaloniaCollection
{
    public const string Name = "Headless Avalonia";
}

public sealed class HeadlessTestApplication : Avalonia.Application
{
    public override void Initialize()
    {
        foreach (var theme in ThemeRegistry.ConcreteThemes)
        {
            Resources.ThemeDictionaries[theme.RequestedVariant] = theme.BuildResources();
        }

        ThemeApplicationService.Apply(this, "Light");
    }
}

public sealed class SafeHeadlessUnitTestSession : IDisposable
{
    private readonly HeadlessUnitTestSession _inner;
    private readonly BlockingCollection<(Action, ExecutionContext?)> _queue;
    private readonly CancellationTokenSource _cancellation;
    private int _disposed;

    private SafeHeadlessUnitTestSession(HeadlessUnitTestSession inner,
        BlockingCollection<(Action, ExecutionContext?)> queue, CancellationTokenSource cancellation)
    {
        _inner = inner;
        _queue = queue;
        _cancellation = cancellation;
    }

    public static SafeHeadlessUnitTestSession StartNew(Type applicationType)
    {
        var cancellation = new CancellationTokenSource();
        var queue = new BlockingCollection<(Action, ExecutionContext?)>();
        var started = new TaskCompletionSource<HeadlessUnitTestSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Avalonia 12.1.0 publishes its session from Task.Run before the captured
        // worker task is necessarily assigned. Construct the task before starting
        // it so Dispose always joins the real worker. Dispatch and application
        // isolation still use Avalonia's implementation; teardown failures propagate.
        Task? worker = null;
        worker = new Task(() =>
        {
            try
            {
                var builder = ConfigureApplication(null, applicationType);
                if (builder.WindowingSubsystemName != "Headless")
                    builder.UseHeadless(new AvaloniaHeadlessPlatformOptions());
                if (builder.TextShapingSubsystemInitializer is null)
                    builder.UseHarfBuzz();
                started.SetResult(CreateSession(builder, cancellation, queue, worker!, true));
                // CompleteAdding can race with a blocked consumer during disposal.
                // The consuming enumerable treats queue completion as normal shutdown.
                foreach (var (action, context) in queue.GetConsumingEnumerable(cancellation.Token))
                {
                    if (context is null) action();
                    else ExecutionContext.Run(context, state => ((Action)state!).Invoke(), action);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                started.TrySetException(exception);
                throw;
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning);
        worker.Start(TaskScheduler.Default);
        try
        {
            return new(started.Task.GetAwaiter().GetResult(), queue, cancellation);
        }
        catch
        {
            try { worker.GetAwaiter().GetResult(); }
            finally { queue.Dispose(); cancellation.Dispose(); }
            throw;
        }
    }

    // Bound to the pinned Avalonia.Headless 12.1.0 constructor. A dependency
    // signature change fails explicitly rather than weakening exception checks.
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern HeadlessUnitTestSession CreateSession(Avalonia.AppBuilder builder,
        CancellationTokenSource cancellation, BlockingCollection<(Action, ExecutionContext?)> queue,
        Task worker, bool isolated);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Configure")]
    private static extern AppBuilder ConfigureApplication(AppBuilder? _, Type applicationType);

    public Task Dispatch(Action action, CancellationToken cancellationToken) =>
        CompleteOffWorker(_inner.Dispatch(() => { action(); return true; }, cancellationToken));

    public Task Dispatch(Func<Task> action, CancellationToken cancellationToken) =>
        CompleteOffWorker(_inner.Dispatch<bool>(async () => { await action(); return true; }, cancellationToken));

    public Task<T> Dispatch<T>(Func<Task<T>> action, CancellationToken cancellationToken) =>
        CompleteOffWorker(_inner.Dispatch<T>(action, cancellationToken));

    private static Task<T> CompleteOffWorker<T>(Task<T> task)
    {
        // A test continuation can close the session. It must not run inline on
        // the worker it will join during Dispose.
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = task.ContinueWith(completed =>
        {
            if (completed.IsCanceled) completion.TrySetCanceled();
            else if (completed.IsFaulted) completion.TrySetException(completed.Exception!.InnerExceptions);
            else completion.TrySetResult(completed.Result);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return completion.Task;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            _inner.Dispose();
        }
        finally
        {
            _queue.Dispose();
            _cancellation.Dispose();
        }
    }
}
