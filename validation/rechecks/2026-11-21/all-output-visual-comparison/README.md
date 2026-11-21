# 同文件全部输出图：仅目视浏览

按本次用户更正，只查看输出图，不进行数值误差比较。选择「文件 → 示例」中的 `Objectives/Cooke 40 degree field.zmx`、`Objectives/Double Gauss 28 degree field.zmx` 和 `Objectives/Petzval.zmx`。这是三例的目视范围，不代表全部 95 支镜头的精度验收。

打开 [并排图册](index.html)，可以切换镜头、逐图翻页、查看原图或展开全部缩略图。每例保留 71 个图形入口，共 213 个位置；纯数值和文本报告不进入图册。`contact-sheets/` 保留按顺序排列的完整图形缩略图。

软件侧通过当前产品的真实 `AnalysisPanel`、`ViewerPanel`，在 Avalonia Headless + Skia 中保存界面，没有使用数据重画图代替。三例各 71 张图形界面；其中 Cooke 四页、Double Gauss 三页、Petzval 两页达到本次 75 秒捕获截止，保留当时界面并标为超时。这一截止不证明正常交互一定无法完成。`captured` 只表示界面已保存；部分界面是诊断表，而不是分析成功的图形。

原生侧使用本机 OpticStudio 2026 R1 的 ZOS-API 加载同一源文件，保存独立截图副本和分析 CFG，再用原生 ZPL 导出窗口图片。执行发生在独立 Windows 桌面，没有切换或控制用户桌面。图册收入 Cooke 60 张、Double Gauss 59 张、Petzval 59 张有效原生图片。Cooke 图像输入相关宏停滞后，只终止本任务创建的进程；其零字节 `geometric-image-analysis.JPG` 保留在原目录，并记录到 [无效图清单](invalid-images.json)，不作为可用图片显示。其余缺少对应窗口或未导出的入口保留明确占位。

三例源文件的捕获前后 SHA-256 一致。来源、图像路径及当前状态见 [图册数据](gallery-data.json)；实际输入身份见 [输入清单](inputs.json)；保存的界面设置及逐页状态在各镜头的 `workbench/`，原生设置、宏和运行记录在 `zemax/`。

原始采集保留两边各自默认显示设置，未统一采样、参照、物方图像、范围、归一化、颜色及相机。后续专项修复条目通过下述覆盖清单显示新界面，在图下单独声明输入与验证范围；其历史原图和原生捕获仍保留。Angle vs Image Height 的两个扫描入口共用最近似原生窗口，Jones Pupil 与原生默认偏振图的呈现形式不同。整本图册没有计算误差、相关性或通过率，也未改变原数值验收记录。

目视发现的重点：

- 点列图的视场排布、光点标记、留白与部分离轴轮廓不同；二维结构的镜片排布相近，光线配色与三维视角不同。
- PSF 默认显示为二维热图和三维曲面两种形式；MTF 色图的默认色阶分布明显不同，需先对齐设置。
- 几何线／边缘扩散分别采用叠图和双面板；扩展源能量的默认曲线存在阶梯和平滑的差别。
- Foucault 默认图可见明暗方向差别；衍射包围能量的产品页面为诊断表，Double Gauss 和 Petzval 原生窗口也保留低采样提示。

逐页说明位于图册的图片下方。以上是截图观察，没有将显示差别归因为计算错误。

`capture/` 保存独立采集与图册生成源码。它不修改产品计算。`VisualCapture.csproj` 依赖本仓库主程序及固定版本 Avalonia.Headless；原生宿主通过本机 .NET Framework 编译器引用本机 ZOS-API，不分发专有 DLL。临时编译输出的完整运行时已清理；源代码和图像证据保留。`build-gallery.py` 使用 Pillow 组合缩略图，`verify-gallery.cjs` 检查全部 213 种选择和 391 个本地图像引用，未运行光学数值测试套件。

## 专项修复界面覆盖

[corrections.json](corrections.json) 只记录已经完成并有实际截图与验证说明的条目。覆盖按菜单相对路径、源文件 SHA-256 与分析键匹配；新图可以复用同日其他专项目录中的权威文件，不复制图片。`gallery-data.json` 保留 `originalCurrent`、原状态与专项证据链接，页面提供修复前原图入口。原生图不重采或替换，未覆盖的条目仍显示原始采集。

双高斯扩展光源页使用面积采样修复后的界面，并按原生 CFG 对齐 `LETTERF.IMA`、完整源宽 19.79898987322333°、质心参照及最大距离 50000 µm。请求 100000、实际追迹 99750 条光线。原默认 1.4° 均匀方形源的历史图，以及修复后的默认源画面均可从图下查看。这是专项对齐输入后的新图，不将原默认图或整本图册升级为精度验收。

同一清单还为[六例图册](representative-six/index.html)的无焦瞄准镜接入垂轴色差和轴向像差修复界面。垂轴色差沿用产品 mrad 单位并换算到与原生角分图相同的物理范围；轴向像差使用屈光度 D。两项均链接原始截图及各自专项记录。

只刷新浏览器图册及数据、不重采或重写历史缩略图：

```powershell
python capture/refresh-gallery.py .
node capture/verify-gallery.cjs .
```

重新执行 `build-gallery.py` 也会应用相同清单；加 `--no-contact-sheets` 只生成 HTML、JSON 与引用清单。已有 `contact-sheets/` 在仅刷新时保留原批次缩略图，浏览器的“全部缩略图”使用当前覆盖后的图片。逐项路径、嵌入数据一致性及历史图保留结果见 [覆盖校验记录](corrections-verification.json)。

本地打开 `index.html` 可直接使用共享专项图片；如通过 HTTP 浏览，服务根需包含同日专项图片所在的上级目录。
