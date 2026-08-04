using System.Buffers.Binary;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OptilandWorkbench.App.Services;
using SkiaSharp;

namespace OptilandWorkbench.App.Panels;

internal sealed class ImageFileViewerWindow : Window
{
    private readonly Image _image = new()
    {
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Margin = new Thickness(12)
    };
    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(12, 5),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly ZemaxImageData? _zemaxImage;
    private Bitmap? _bitmap;

    public ImageFileViewerWindow(string path, bool zemaxImage)
    {
        Title = zemaxImage ? "IMA和BIM图片浏览器" : "位图文件查看器";
        Width = 900;
        Height = 700;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(10, 8)
        };
        toolbar.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(path),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (zemaxImage)
        {
            _zemaxImage = ZemaxImageFile.Read(path);
            var display = new ComboBox
            {
                MinWidth = 140,
                ItemsSource = BuildDisplayChoices(_zemaxImage),
                SelectedIndex = 0
            };
            display.SelectionChanged += (_, _) => RenderZemaxImage(display.SelectedIndex);
            toolbar.Children.Add(display);
            RenderZemaxImage(0);
            _status.Text = $"{_zemaxImage.Width} × {_zemaxImage.Height} · "
                + $"{_zemaxImage.Channels} 通道 · 输入值已归一化显示";
        }
        else
        {
            using var stream = File.OpenRead(path);
            _bitmap = new Bitmap(stream);
            _image.Source = _bitmap;
            _status.Text = $"{_bitmap.PixelSize.Width} × {_bitmap.PixelSize.Height}";
        }

        var toolbarBorder = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = toolbar
        };
        toolbarBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);

        var imageCanvas = new Border
        {
            Child = _image
        };
        imageCanvas.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.PlotBackground);

        var statusBorder = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = _status
        };
        statusBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        _status.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextSecondary);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                toolbarBorder,
                imageCanvas,
                statusBorder
            }
        };
        Grid.SetRow(root.Children[1], 1);
        Grid.SetRow(root.Children[2], 2);
        Content = root;
        Closed += (_, _) => _bitmap?.Dispose();
    }

    private static IReadOnlyList<string> BuildDisplayChoices(ZemaxImageData image)
    {
        var choices = new List<string> { "伪彩色", "灰度" };
        if (image.Channels >= 3)
        {
            choices.Add("RGB 合成");
        }

        choices.AddRange(Enumerable.Range(1, image.Channels).Select(index => $"通道 {index}"));
        return choices;
    }

    private void RenderZemaxImage(int selection)
    {
        if (_zemaxImage is null)
        {
            return;
        }

        var rgb = _zemaxImage.Channels >= 3 && selection == 2;
        var channelStart = _zemaxImage.Channels >= 3 ? 3 : 2;
        var selectedChannel = selection >= channelStart
            ? Math.Clamp(selection - channelStart, 0, _zemaxImage.Channels - 1)
            : 0;
        var falseColor = selection == 0;
        using var rendered = new SKBitmap(
            _zemaxImage.Width,
            _zemaxImage.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Opaque);
        var maximum = _zemaxImage.Values.DefaultIfEmpty(0).Max();
        var scale = maximum > 0 ? 1 / maximum : 0;
        for (var row = 0; row < _zemaxImage.Height; row++)
        {
            var sourceRow = _zemaxImage.BottomUp
                ? _zemaxImage.Height - 1 - row
                : row;
            for (var column = 0; column < _zemaxImage.Width; column++)
            {
                SKColor color;
                if (rgb)
                {
                    color = new SKColor(
                        Channel(_zemaxImage.Value(0, sourceRow, column) * scale),
                        Channel(_zemaxImage.Value(1, sourceRow, column) * scale),
                        Channel(_zemaxImage.Value(2, sourceRow, column) * scale));
                }
                else
                {
                    var value = Math.Clamp(
                        _zemaxImage.Value(selectedChannel, sourceRow, column) * scale,
                        0,
                        1);
                    color = falseColor ? FalseColor(value) : Gray(value);
                }

                rendered.SetPixel(column, row, color);
            }
        }

        using var encoded = rendered.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(encoded.ToArray());
        var next = new Bitmap(stream);
        _image.Source = next;
        _bitmap?.Dispose();
        _bitmap = next;
    }

    private static byte Channel(double value)
    {
        return (byte)Math.Clamp(Math.Round(Math.Clamp(value, 0, 1) * 255), 0, 255);
    }

    private static SKColor Gray(double value)
    {
        var channel = Channel(value);
        return new SKColor(channel, channel, channel);
    }

    private static SKColor FalseColor(double value)
    {
        value = Math.Clamp(value, 0, 1);
        var red = Math.Clamp(1.5 - Math.Abs((4 * value) - 3), 0, 1);
        var green = Math.Clamp(1.5 - Math.Abs((4 * value) - 2), 0, 1);
        var blue = Math.Clamp(1.5 - Math.Abs((4 * value) - 1), 0, 1);
        return new SKColor(Channel(red), Channel(green), Channel(blue));
    }
}

internal sealed record ZemaxImageData(
    int Width,
    int Height,
    int Channels,
    double[] Values,
    bool BottomUp)
{
    public double Value(int channel, int row, int column)
    {
        return Values[((channel * Height) + row) * Width + column];
    }
}

internal static class ZemaxImageFile
{
    private const int MaximumDimension = 8000;
    private const long MaximumSampleCount = 96_000_000;

    public static ZemaxImageData Read(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".bim", StringComparison.OrdinalIgnoreCase))
        {
            return ReadBim(path);
        }

        if (!extension.Equals(".ima", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("仅支持 .IMA 和 .BIM 文件。");
        }

        var prefix = new byte[2];
        using (var stream = File.OpenRead(path))
        {
            if (stream.Read(prefix, 0, prefix.Length) != prefix.Length)
            {
                throw new InvalidDataException("IMA 文件为空或不完整。");
            }
        }

        return BinaryPrimitives.ReadInt16LittleEndian(prefix) == 0
            ? ReadBinaryIma(path)
            : ReadTextIma(path);
    }

    private static ZemaxImageData ReadTextIma(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0
            || !int.TryParse(lines[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
        {
            throw new InvalidDataException("文本 IMA 缺少有效的图像尺寸。");
        }

        ValidateDimensions(size, size, 1);
        if (lines.Length < size + 1)
        {
            throw new InvalidDataException("文本 IMA 的像素行数不足。");
        }

        var values = new double[size * size];
        for (var row = 0; row < size; row++)
        {
            var pixels = new string(lines[row + 1].Where(character => !char.IsWhiteSpace(character)).ToArray());
            if (pixels.Length != size || pixels.Any(character => character is < '0' or > '9'))
            {
                throw new InvalidDataException($"文本 IMA 第 {row + 1} 行不是 {size} 个强度数字。");
            }

            for (var column = 0; column < size; column++)
            {
                values[(row * size) + column] = pixels[column] - '0';
            }
        }

        return new ZemaxImageData(size, size, 1, values, BottomUp: false);
    }

    private static ZemaxImageData ReadBinaryIma(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var marker = reader.ReadInt16();
        var size = reader.ReadInt16();
        var channels = reader.ReadInt16();
        if (marker != 0)
        {
            throw new InvalidDataException("二进制 IMA 标记无效。");
        }

        ValidateDimensions(size, size, channels);
        var count = checked(size * size * channels);
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
        {
            throw new InvalidDataException("二进制 IMA 像素数据不完整。");
        }

        return new ZemaxImageData(
            size,
            size,
            channels,
            bytes.Select(value => (double)value).ToArray(),
            BottomUp: false);
    }

    private static ZemaxImageData ReadBim(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 8)
        {
            throw new InvalidDataException("BIM 文件头不完整。");
        }

        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        ValidateDimensions(width, height, 1);
        var count = checked(width * height);
        var expectedLength = 8L + (count * sizeof(double));
        if (stream.Length < expectedLength)
        {
            throw new InvalidDataException("BIM 像素数据不完整。");
        }

        var values = new double[count];
        for (var index = 0; index < count; index++)
        {
            var value = reader.ReadDouble();
            values[index] = double.IsFinite(value) && value > 0 ? value : 0;
        }

        return new ZemaxImageData(width, height, 1, values, BottomUp: true);
    }

    private static void ValidateDimensions(int width, int height, int channels)
    {
        if (width is < 1 or > MaximumDimension
            || height is < 1 or > MaximumDimension
            || channels is < 1 or > 256
            || (long)width * height * channels > MaximumSampleCount)
        {
            throw new InvalidDataException("图像尺寸或通道数超出支持范围。");
        }
    }
}
