using System.Reflection;
using Avalonia.Controls.ApplicationLifetimes;

namespace OpticalSystemDesign.Tests;

public sealed class AppStartupTests
{
    [Fact]
    public void MainWindowStartupEntryPointReturnsTaskSoFailuresAreObservable()
    {
        var method = typeof(global::OpticalSystemDesign.App.App)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(item =>
                item.Name == "OpenMainWindowAsync"
                && item.GetParameters() is
                [
                { ParameterType: var first },
                { ParameterType: var second }
                ]
                && first == typeof(IClassicDesktopStyleApplicationLifetime)
                && second == typeof(global::OpticalSystemDesign.App.SplashWindow));

        Assert.Equal(typeof(Task), method.ReturnType);
    }

    [Fact]
    public async Task StartupCoordinatorTimesOutInsteadOfWaitingForever()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            global::OpticalSystemDesign.App.App.AwaitStartupCompletedAsync(
                pending.Task,
                TimeSpan.FromMilliseconds(1),
                CancellationToken.None));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartupCoordinatorPropagatesCloseCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            global::OpticalSystemDesign.App.App.AwaitStartupCompletedAsync(
                Task.Delay(TimeSpan.FromMinutes(1)),
                TimeSpan.FromSeconds(10),
                cancellation.Token));
    }
}
