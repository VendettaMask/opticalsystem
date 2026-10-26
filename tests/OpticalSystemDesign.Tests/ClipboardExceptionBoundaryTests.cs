using System.Runtime.InteropServices;
using Avalonia.Controls;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ClipboardExceptionBoundaryTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task FailedReadCancelsPasteAndReportsOnceForSynchronousAndAsynchronousProviders(int errorKind, bool asynchronous)
    {
        Exception failure = errorKind switch
        {
            0 => new COMException("EnumFormatEtc failed", unchecked((int)0x8007000E)),
            1 => new TimeoutException("clipboard busy"),
            _ => new UnauthorizedAccessException("clipboard denied")
        };
        var reports = 0;
        void OnFailure(object? sender, EventArgs args) => reports++;
        ClipboardExceptionBoundary.ReadFailed += OnFailure;
        try
        {
            var result = await ClipboardExceptionBoundary.TryReadTextAsync(() => asynchronous
                ? Task.FromException<string?>(failure) : throw failure);
            Assert.False(result.Succeeded);
            Assert.Null(result.Text);
            Assert.Equal(1, reports);
        }
        finally { ClipboardExceptionBoundary.ReadFailed -= OnFailure; }
    }

    [Fact]
    public async Task ClipboardProgrammingErrorsStillPropagate()
    {
        var failure = new InvalidOperationException("provider programming error");
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => ClipboardExceptionBoundary.TryReadTextAsync(() => throw failure)));
    }

    [Fact]
    public async Task EmptyClipboardRemainsEmptyWithoutWarning()
    {
        var reports = 0;
        void OnFailure(object? sender, EventArgs args) => reports++;
        ClipboardExceptionBoundary.ReadFailed += OnFailure;
        try
        {
            var result = await ClipboardExceptionBoundary.TryReadTextAsync(() => Task.FromResult<string?>(null));
            Assert.True(result.Succeeded);
            Assert.Null(result.Text);
            Assert.Equal(0, reports);
        }
        finally { ClipboardExceptionBoundary.ReadFailed -= OnFailure; }
    }

    [Fact]
    public void GlobalBoundaryDoesNotSwallowUnrelatedComOrUiFailures()
    {
        Assert.False(ClipboardExceptionBoundary.IsNativePasteFailure(new COMException("EnumFormatEtc failed", unchecked((int)0x8007000E))));
        Assert.False(ClipboardExceptionBoundary.IsNativePasteFailure(new InvalidOperationException("TextBox.Paste")));
    }

    [Fact]
    public async Task SuccessfulReadRetainsUnicodeAndLineBreaksForNativeTextBoxProcessing()
    {
        const string text = "二维\r\n视图 🪞";
        var result = await ClipboardExceptionBoundary.TryReadTextAsync(() => Task.FromResult<string?>(text));
        Assert.True(result.Succeeded);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public async Task SearchReadFailurePreservesQuerySelectionAndResults()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CommandSearchTests));
        await session.Dispatch(async () =>
        {
            var actions = new ActionManager();
            var ran = false;
            actions.Register("query", "原搜索词", "测试", () => ran = true);
            var search = new CommandSearchControl(actions);
            var window = new Window { Content = search, Width = 400, Height = 200 };
            window.Show();
            try
            {
                search.SearchBox.Text = "原搜索词";
                search.SearchBox.SelectionStart = 1;
                search.SearchBox.SelectionEnd = 3;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                await search.PasteSafelyAsync(() => throw new COMException("EnumFormatEtc failed", unchecked((int)0x8007000E)));
                Assert.Equal("原搜索词", search.SearchBox.Text);
                Assert.Equal(1, search.SearchBox.SelectionStart);
                Assert.Equal(3, search.SearchBox.SelectionEnd);
                Assert.Equal(1, search.Results.ItemCount);
                await search.RunSelectedAsync();
                Assert.True(ran);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DelayedClipboardReadDoesNotOverwriteNewQueryOrSelection()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CommandSearchTests));
        await session.Dispatch(async () =>
        {
            var search = new CommandSearchControl(new ActionManager());
            var window = new Window { Content = search, Width = 400, Height = 200 };
            window.Show();
            try
            {
                var nativePastes = 0;
                search.SearchBox.PastingFromClipboard += (_, _) => nativePastes++;
                search.SearchBox.Text = "原搜索词";
                var pending = new TaskCompletionSource<string?>();
                var paste = search.PasteSafelyAsync(() => pending.Task);
                search.SearchBox.Text = "新搜索词";
                search.SearchBox.SelectionStart = 1;
                search.SearchBox.SelectionEnd = 2;
                pending.SetResult("剪贴板文本");
                await paste;
                Assert.Equal("新搜索词", search.SearchBox.Text);
                Assert.Equal(1, search.SearchBox.SelectionStart);
                Assert.Equal(2, search.SearchBox.SelectionEnd);
                Assert.Equal(0, nativePastes);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReadOnlySearchDoesNotReadClipboard()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CommandSearchTests));
        await session.Dispatch(async () =>
        {
            var search = new CommandSearchControl(new ActionManager());
            var window = new Window { Content = search, Width = 400, Height = 200 };
            window.Show();
            try
            {
                search.SearchBox.IsReadOnly = true;
                var reads = 0;
                await search.PasteSafelyAsync(() => { reads++; return Task.FromResult<string?>("paste"); });
                Assert.Equal(0, reads);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
