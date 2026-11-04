# 异世界分析页亮度：检查与修复

## 结论

用户提供的光程差、瞳孔像差、FFT PSF截面三张截图中，第二张确实变暗。相同配置的独立AnalysisPanel稳定画面中，三种分析的工具栏取样RGB完全相同；差异发生在实际停靠文档的上层透明呈现阶段。

修复前记录到`Dock.Controls.DeferredContentControl.DeferredContentPresenter`的实际不透明度分别为0.85637、0.85066、0.85；前两页随后恢复1，FFT截面本轮16次连续观测仍为0.85。后者包含截图编码、调度及20ms间隔，不能当作精确动画时长基准，也不把离屏宿主中的卡住现象宣称为所有原生机器都必现。用户第二张的色值与此约85%混合相符，且显示出同类整页变暗。

`IsekaiThemeStyles`现仅对实际Isekai的原生`DocumentControl`设置独立的`DeferredContentPresentationTimeline`，`RevealDuration=0`。保留延迟创建与原生命令/回收，首个可见帧即全不透明。原纸纹、配色、光学系列与全局默认时间线不变；其他主题撤销该局部设置。官方公开源码说明[时间线及淡入配置](https://raw.githubusercontent.com/wieslawsoltes/Dock/master/src/Dock.Controls.DeferredContentControl/DeferredContentControl.cs)，本机固定12.0.0.2的实际值以本报告中的运行记录为依据，不用master替代固定依赖身份。

## 截图测量

[只读Python检查](inspect-screenshots.py)只读取三张原截图，不编辑它们；[原始测量](screenshot-measurements.json)保留原尺寸、SHA256及取样定义。统计去除各区域最暗25%的像素以降低文字/曲线干扰，亮度为sRGB通道加权编码值，不是显示器物理亮度。

| 原截图 | 工具栏亮度／255 | 主要纸面亮度／255 |
| --- | ---: | ---: |
| 光程差 | 226.48 | 223.73 |
| 瞳孔像差 | 193.98 | 220.56 |
| FFT PSF截面 | 226.68 | 224.31 |

第二张工具栏约为另两张的86%；不同分析内容和纹理裁切会影响整页统计，不能仅凭主要纸面均值判定色板不同。

## 运行核查与验证

修复前[诊断源码](Program.cs)和[项目定义](BrightnessProbe.csproj)使用当时已有默认Release App，不重编产品，独立Headless/Skia进程运行，程序集身份见[runtime/measurements.json](runtime/measurements.json)。独立页面在相同1440×1080、200%条件下的取样RGB均为`243.1633,221.58,182.1633`，系列Opacity均为1。实际PanelManager原生停靠的[逐帧祖先透明度](runtime/dock-measurements.json)指向DeferredContentPresenter。诊断程序用于复现，不注册为产品模块或自动化测试。

修复后默认Release [App增量构建](build-app.log)和[最终测试增量构建](build-after-audit.log)均零警告/错误，使用`BuildProjectReferences=false`复用已有Core及内置模块；没有宣称本轮重编了整个解决方案。默认App随后合入并行自动图幅更新，因此收尾以当前产物重验同一集合，[最终结果](brightness-final.trx) **7通过、0失败、0跳过，1分10秒**，不累加前置同范围结果。收尾编译曾读取到4条缓存NU1900漏洞信息访问警告，随后[锁定restore及审计](restore-final.log)完成，审计未关闭；最终无警告编译前后被测程序集SHA256保持一致，身份见[tested-identities.json](tested-identities.json)。

- IsekaiDocumentBrightnessTests 2项：每项实际打开光程差、瞳孔像差、FFT PSF截面，检查首个可见帧及随后帧的完整祖先Opacity=1，实际工具栏三通道均值之差≤1；覆盖13字号/150%与18字号/200%，并验证四个其他主题没有该局部时间线、全局默认值和工程/系列数据不变。
- IsekaiCraftedWorkbenchTests 2项：保留真实菜单、设置、原生关闭和对比度检查。
- DockWorkspaceCornerTests 3项：保留调整尺寸、复位和各主题往返检查。

亮度用例只认证宿主不透明度与同位纸面像素。18字号六分图截图中可见源图溢出提示，该状态不属于本次亮度通过结论；自动图幅、布局容纳能力与光学数值仍按各自记录解释。

实际修复截图：

| 分析 | 150%／13字号 | 200%／18字号 |
| --- | --- | --- |
| 光程差 | [实际页面](after/Optical-Path-Difference-13-1.5.png) | [实际页面](after/Optical-Path-Difference-18-2.png) |
| 瞳孔像差 | [实际页面](after/Pupil-Aberration-13-1.5.png) | [实际页面](after/Pupil-Aberration-18-2.png) |
| FFT PSF截面 | [实际页面](after/FFT-PSF-Cross-Section-13-1.5.png) | [实际页面](after/FFT-PSF-Cross-Section-18-2.png) |

均为真实C#控件离屏渲染；没有生成素材、修改光学算法、控制桌面、重启用户程序、提交、推送或打包。此7项为本轮范围，不与先前尺寸修复的18项相加，不构成全仓或光学精度认证。原生多显示器与安装包仍需分别核验。重复诊断帧经SHA256核对后复用同一保留文件，收据见[诊断帧去重](runtime/frame-deduplication.json)；临时bin/obj依赖副本清理。
