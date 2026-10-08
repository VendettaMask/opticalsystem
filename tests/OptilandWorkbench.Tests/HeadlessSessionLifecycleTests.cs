namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class HeadlessSessionLifecycleTests
{
    [Fact]
    public async Task RapidSessionsCanCloseBeforeAndAfterDispatchWithoutFilteringFailures()
    {
        for (var index = 0; index < 100; index++)
        {
            using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
            if (index % 2 == 0)
                await session.Dispatch(() => Assert.NotNull(Avalonia.Application.Current), CancellationToken.None);
            session.Dispose();
            session.Dispose();
        }
    }

    [Fact]
    public async Task DispatchedFailuresAreReportedAndNextSessionRemainsIsolated()
    {
        var expected = new NullReferenceException("Application failure must reach the test.");
        using (var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication)))
        {
            var actual = await Assert.ThrowsAsync<NullReferenceException>(() =>
                session.Dispatch((Action)(() => throw expected), CancellationToken.None));
            Assert.Same(expected, actual);
        }
        using var next = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await next.Dispatch(() => Assert.NotNull(Avalonia.Application.Current), CancellationToken.None);
    }
}
