using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App;

internal sealed class SplashWindow : Window
{
    private readonly Bitmap _image = BrandAssets.LoadSplashBitmap();
    private readonly ProgressBar _progressBar = new()
    {
        Minimum = 0,
        Maximum = 100,
        Value = 8,
        Height = 5,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)),
        Foreground = new SolidColorBrush(Color.FromRgb(246, 176, 116))
    };
    private readonly TextBlock _statusText = new()
    {
        Text = "正在初始化光学工作区...",
        FontSize = DisplayTypography.BodySmall,
        Foreground = new SolidColorBrush(Color.FromRgb(210, 214, 220))
    };
    private readonly TextBlock _progressText = new()
    {
        Text = "8%",
        FontSize = DisplayTypography.BodySmall,
        Foreground = new SolidColorBrush(Color.FromRgb(246, 176, 116)),
        HorizontalAlignment = HorizontalAlignment.Right
    };
    private readonly DispatcherTimer _progressTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(20)
    };
    private double _targetProgress = 8;

    public SplashWindow()
    {
        Title = "Optical System Design";
        Width = 800;
        Height = 450;
        CanResize = false;
        Topmost = true;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        TransparencyBackgroundFallback = Brushes.Transparent;
        Background = Brushes.Transparent;
        Icon = BrandAssets.LoadWindowIcon();
        Content = BuildContent();
        _progressTimer.Tick += OnProgressTimerTick;
        _progressTimer.Start();
        Closed += OnClosed;
    }

    internal double ProgressValue => _progressBar.Value;

    internal void ReportProgress(double value, string status)
    {
        _targetProgress = Math.Max(_targetProgress, Math.Clamp(value, 0, 100));
        _statusText.Text = status;
    }

    internal void Complete()
    {
        _targetProgress = 100;
        _progressBar.Value = 100;
        _progressText.Text = "100%";
        _statusText.Text = "准备就绪";
    }

    private Control BuildContent()
    {
        var root = new Grid();
        root.Children.Add(new Image
        {
            Source = _image,
            Stretch = Stretch.UniformToFill
        });

        var information = new Grid
        {
            Width = 320,
            Margin = new Thickness(0, 42, 48, 34),
            HorizontalAlignment = HorizontalAlignment.Right,
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        var identity = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 10,
            Children =
            {
                new Border
                {
                    Width = 48,
                    Height = 2,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromRgb(246, 176, 116))
                },
                new TextBlock
                {
                    Text = "Optical System Design",
                    FontSize = DisplayTypography.SplashTitle,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.NoWrap
                },
                new TextBlock
                {
                    Text = "光学系统设计、分析与优化",
                    FontSize = DisplayTypography.SectionTitle,
                    Foreground = new SolidColorBrush(Color.FromRgb(224, 226, 230))
                },
                new TextBlock
                {
                    Text = "Sequential ray tracing · Imaging analysis",
                    FontSize = DisplayTypography.BodySmall,
                    Foreground = new SolidColorBrush(Color.FromRgb(155, 161, 170))
                },
                new TextBlock
                {
                    Text = "S.T.A.R. Labs",
                    Margin = new Thickness(0, 8, 0, 0),
                    FontSize = DisplayTypography.Body,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(246, 176, 116))
                },
                new TextBlock
                {
                    Text = ProductVersion(),
                    FontSize = DisplayTypography.RibbonText,
                    Foreground = new SolidColorBrush(Color.FromRgb(125, 132, 142))
                }
            }
        };
        Grid.SetRow(identity, 0);
        information.Children.Add(identity);

        var statusGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8),
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        Grid.SetColumn(_statusText, 0);
        Grid.SetColumn(_progressText, 1);
        statusGrid.Children.Add(_statusText);
        statusGrid.Children.Add(_progressText);
        var progressArea = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                statusGrid,
                _progressBar
            }
        };
        Grid.SetRow(progressArea, 1);
        information.Children.Add(progressArea);
        root.Children.Add(information);
        return new Border
        {
            CornerRadius = new CornerRadius(20),
            ClipToBounds = true,
            Child = root
        };
    }

    private void OnProgressTimerTick(object? sender, EventArgs args)
    {
        var difference = _targetProgress - _progressBar.Value;
        if (difference <= 0.05)
        {
            return;
        }

        var step = Math.Max(0.35, difference * 0.1);
        _progressBar.Value = Math.Min(_targetProgress, _progressBar.Value + step);
        _progressText.Text = $"{_progressBar.Value:0}%";
    }

    private static string ProductVersion()
    {
        var version = typeof(SplashWindow).Assembly.GetName().Version;
        return version is null
            ? "Version 1.0.0"
            : $"Version {version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        Closed -= OnClosed;
        _progressTimer.Stop();
        _progressTimer.Tick -= OnProgressTimerTick;
        _image.Dispose();
    }
}
