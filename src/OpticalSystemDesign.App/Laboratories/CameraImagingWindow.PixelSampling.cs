using Avalonia.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Laboratories;

public sealed partial class CameraImagingWindow
{
    private static readonly (int Width, int Height)[] StandardSizes = [(128, 96), (256, 192), (512, 384), (64, 48), (32, 24)];
    private static readonly string[] SizeLabels = ["128 × 96", "256 × 192", "512 × 384", "64 × 48", "32 × 24"];
    private readonly ComboBox _pixelSampling = Choice("camera-pixel-sampling", "像素中心", "面积积分 2 × 2", "面积积分 4 × 4");
    private readonly ComboBox _qeArea = Choice("camera-qe-area", "整像元总 QE", "感光区 QE");

    private Control BuildPixelSamplingCard() => Group("像元面积与 QE", new Control[]
    {
        Field("像元采样", _pixelSampling), Number("fill-factor", "填充因子 (0–1)", 1, .01m, 1, .05m), Field("QE 面积口径", _qeArea),
        LaboratoryPresentation.Hint("三维模式直接在实际方形感光区内作 Gauss 积分；改变填充因子会移动采样点。整像元总 QE 需与感光面积相容；无微透镜模型时总 QE 不可超过填充比例。二维模式当前仅开放满填充，旧实验保留原网格算法供复算。有限采样需检查收敛。")
    });

    private CameraPixelSamplingParameters? ReadPixelSampling() => _pixelSampling.SelectedIndex == 0 ? null
        : new(_pixelSampling.SelectedIndex == 1 ? 2 : 4, V("fill-factor"), _qeArea.SelectedIndex == 0);

    private (int Width, int Height) SelectedSize => _size.SelectedIndex >= StandardSizes.Length && _savedSize is { } saved
        ? saved : StandardSizes[Math.Clamp(_size.SelectedIndex, 0, StandardSizes.Length - 1)];
}
