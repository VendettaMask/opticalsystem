# 普通 Light 左右白色描边一致：2026-11-03

用户反馈白边左右不一致。原因是 `LightGlassMaterial.Rim` 使用斜向渐变，右侧偏蓝；`FrameShadow` 另有一层只在左侧的白色内高光。修复将描边渐变改为垂直方向，并对称设置左右内高光。没有调整左右边距、边框逻辑厚度、圆角、字体或表格内容。

## 实际前后像素

同一 `4d6ac597` 基线、同一 Double Gauss 工程及既有真实窗口测试，修改前后分别捕获。主工作区其他任务的未完成 Core 修改未进入隔离检出。

- [修改前 1600×1000](../chinese-medium-default/workbench-1600x1000-13.png)
- [修改后 1600×1000](after/workbench-1600x1000-13.png)
- [修改后窄窗/大字号](after/workbench-900x700-18.png)
- [原始像素采样](edge-pixels.json)

在 1600×1000 实际截图中，取文档内容框左右边缘 x=266、1594 的同高度像素：y=180/220/350/500。修改前左侧为近白色，右侧偏蓝，例如 y=350 左侧 RGB(249,252,255)、右侧 RGB(206,228,249)；修改后两侧均为 RGB(248,252,255)。四组采样均逐像素相等。此结论限定这些已记录的离屏截图与采样位置，不扩大为任意系统 DPI 下的原生桌面认证。

## 验证

锁定还原与隔离检出的正常 Release 构建零警告/错误。修改前执行原有真实窗口用例 3 项并保留 [before.trx](before/before.trx)；修改后执行：

```powershell
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~LightGradientThemeTests|FullyQualifiedName~DockWorkspaceCornerTests|FullyQualifiedName~ToolDockCornerTests|FullyQualifiedName~ReferenceWorkbenchLayoutTests" --logger 'trx;LogFileName=symmetric-rim.trx' --results-directory <capture-directory>
```

最终 **15 通过、0 失败、0 跳过**，约 57 秒，原始 [symmetric-rim.trx](after/symmetric-rim.trx)。包括 640/900/1600 宽的实际窗口、13/18 字号、重置/停靠边框、输入与滚动、跨主题返回 Light，以及处方恢复状态保持。修改前的 3 项是重复基线，不累计成 18 个不同用例。

捕获使用 `OPTICAL_SYSTEM_DESIGN_LIGHT_GRADIENT_CAPTURE_DIR` 指向对应的 before/after 目录，均由 Avalonia Headless/Skia 后台执行；未操作、关闭或重启用户前台应用。没有重跑光学精度矩阵、安装或发布新包。隔离源码的绘制文件与主工作区修复文件已做 SHA-256 一致性核对，临时检出及编译载荷随后清理。
