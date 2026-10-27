# README 实际主窗口截图

该工具构造产品的真实 `OpticalSystemDesign.App.MainWindow`，使用自带 `Zoom lens.zmx`、真实处方与共享 Core 生成的二维场景。功能区、命令、标题栏、镜头数据、系统选项和 MDI 文档全部来自产品，不另拼界面控件，不修改图片像素。

仅使用 Avalonia Headless + Skia；不创建原生桌面窗口，不发送物理输入，不读取用户当前工程或改变系统剪贴板。子进程通过已有设置目录环境变量使用独立设置；在首次 UI 异步让出前，将实际窗口的工作区存储目录重定向到同一隔离目录。该重定向仅在诊断工具内用反射完成，产品源码不改动。不要指向用户正在使用的配置目录。

在仓库根目录执行，输出路径使用单独的新目录：

```powershell
dotnet restore tools/diagnostics/ReadmeScreenshots20261027/ReadmeScreenshots20261027.csproj --locked-mode
dotnet build tools/diagnostics/ReadmeScreenshots20261027/ReadmeScreenshots20261027.csproj -c Release --no-restore /m:1 /nr:false
dotnet tools/diagnostics/ReadmeScreenshots20261027/bin/Release/net10.0/ReadmeScreenshots20261027.dll '<仓库绝对路径>' '<截图输出目录>' Light zh-CN
```

主题可选 `Light` / `Dark`，语言可选 `zh-CN` / `en-US`。每种组合在独立进程运行；产生 PNG 和不含本机目录的 JSON 元数据，隔离配置留在输出目录的 `isolated-profiles/` 下，不提交该目录。

使用实际平铺命令，再调整真实 MDI 文档宽度为镜头数据 60%、二维布局 40%，等价于用户调整文档边框。客户端为 1760×990 DIP，仅给虚拟窗口设为 2 倍渲染比例；通过 `CaptureRenderedFrame()` 读取真实合成帧，得到 3520×1980 PNG。元数据必须确认 17 个表面、3 个配置、场景修订与工程修订相等，保存文件后正常关闭隔离窗口。

暗色 Fluent 控件包含合成内容，直接 `RenderTargetBitmap.Render(window)` 会漏掉部分工具栏与字段卡内容；最终资产必须读取实际合成帧，不用像素修补替代。四张最终图均经人工视检，精确字节、输入哈希与来源见 [截图清单](../../../docs/assets/readme/manifest.json)。这些图片用于产品展示，不是新的数值精度、原生 GUI 或跨平台认证。
