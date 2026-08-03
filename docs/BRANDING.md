# 应用品牌资源

桌面图标采用黑洞与暖色吸积盘视觉，启动画面沿用同一品牌语言。产品名称和加载进度由 Avalonia 在运行时绘制，避免把版本文字固化进图片。

## 资源

资源位于 `src/OptilandWorkbench.App/Assets/Brand`：

- `AppIconArtwork.png`：1024×1024 RGBA 主图；
- `AppIcon.png`：带透明圆角和平台安全留白的运行时图标；
- `AppIcon.ico`：Windows 多尺寸图标；
- `AppIcon.icns`：macOS 应用图标；
- `Splash.png`：1280×720 无文字启动背景。

ICO 通过项目的 `ApplicationIcon` 使用，PNG 作为 Avalonia 资源用于窗口和启动页。macOS 在首个窗口出现前通过平台桥接设置 Dock 图标，避免短暂显示开发默认图标。

## 重新生成

从已批准主图重新处理圆角和平台资源：

```bash
python tools/round_brand_icon.py src/OptilandWorkbench.App/Assets/Brand
```

修改资源后应构建解决方案、运行 `BrandAssetTests`，并人工检查小尺寸图标和完整启动页。

## 启动生命周期

启动窗口先于主工作台显示。主窗口在不可见状态完成工作区恢复并发出就绪信号，随后关闭启动页、显示工作台并转移桌面生命周期。最短显示时间用于避免快速机器上的闪烁。
