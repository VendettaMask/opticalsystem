# Zernike 通用入口移除

2026-10-26。默认 Release App/测试输出重新编译，最终定向 **16 通过、0 失败、0 跳过**，见 [TRX](zernike-entry-removal.trx)。不是全仓测试，也没有重新做 Zemax 数值认证；三种具名系数分析和 Core 拟合算法未改。

## 本轮改动

- 删除桌面目录中的通用 `Zernike` / “Zernike 系数”及 `analysis-zernike` 命令注册，功能搜索与报告选择列表不再暴露该入口。
- 通过桌面分析服务直接提交旧名称/键时明确拒绝，并提示选择 Fringe、Standard 或 Annular；不将旧六角采样/通用拟合设置悄悄映射到另一种算法。
- 报告导出仅过滤这两个已移除的键。旧设置对象及其参数不被删除，其他未知分析仍按原逻辑报告失败；三种具名系数分析继续导出，原有系数、基底、编号与拟合统计不变。
- 既有波前图与系数随视场入口保留，不新增重建图或柱形图。审阅文字和对照页第 062 项同步修正；原始截图及历史数值证据保留。

## 回归范围

- `ZernikeEntryRemovalTests`：桌面目录、命令注册、三种类型化系数入口保留；中英文遗留键不能通过设置或请求恢复旧热图。
- `MarkdownReportExportTests`：旧中英文选项与三种具名系数混合时，只有三种具名系数被执行，成功 3、失败 0，设置源对象不变；已有报告快照、失败继续、取消、配置、文件命名等测试也通过。该组用占位图片隔离文件编排，不能视作新增系数图像验收。
- `MarkdownReportImageTests.ConfigurationWindowListsEveryAnalysisAndShowsProgressAndDefaultDirectory`：在真实 Headless/Skia 报告设置窗口中确认旧选项消失、三种系数选项存在、原有常用选中项与配置设置保留。实际截图见 [export-window.png](captures/export-window.png)。
- `AnalysisGuiContractTests.ConnectorExposesAndAppliesAnalysisParameters`：既有三种系数参数、结果文本和系数随视场计算契约通过；命令总数调整为 70，实际菜单命令数维持 65。
- `AnalysisResponsivePlotTests.CoefficientReportsKeepFullTextAndScrollInSmallPanes`：三种系数表及原有 Seidel 文本在小窗口中完整、可选中、可滚动。

审阅对照页另用独立无窗口浏览器检查，结果见 [comparison-runtime-verification.json](comparison-runtime-verification.json)：67 张原图均可解码且名称对应，默认第 062 项，11 张内嵌重点图，1400/1024/736/360 像素宽度及内嵌页面无横向溢出，脚本错误 0；原有 Seidel 仅自动量程、辐射强度不分页及 RMS 四曲线说明保留。生成时逐张比对 SHA256 和 PNG→无损 WebP 解码像素，67 张原图均未修改。

## 保留的失败与限制

- [first-build-failure.txt](first-build-failure.txt)：首次编译停在工作区另一处 `Core.Domain.ApertureKind` 类型引用，未执行测试。仅将引用纠正为 `Core.Apertures.ApertureKind` 后继续，不改变微分公式。
- [export-first-failure.trx](export-first-failure.trx)：15 通过、1 失败；[export-diagnostic.trx](export-diagnostic.trx) 确认新增用例误用 `FieldNumber=1`，而该界面的有效选项是 `1 - 轴上视场`。仅修正用例的选项格式，正式归一化和光学算法未改；最终同一组 16 项全过。
- 对照页首次在受限运行环境中启动无窗口浏览器失败；改用允许本地后台渲染的执行环境后，同一检查脚本通过。没有改成可见浏览器，也没有连接用户浏览器。
- 所有 UI 检查使用 Avalonia Headless/Skia 或独立 headless 浏览器；没有物理窗口、键鼠、焦点或剪贴板操作。没有安装/发布应用或声称原生 GUI 验收。

## 复现

仓库根目录 PowerShell：

```powershell
$env:OPTICAL_SYSTEM_DESIGN_REPORT_CAPTURE_DIR = Join-Path $PWD 'validation/rechecks/2026-10-26/zernike-entry-removal/captures'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~ZernikeEntryRemovalTests|FullyQualifiedName~MarkdownReportExportTests|FullyQualifiedName~MarkdownReportImageTests.ConfigurationWindowListsEveryAnalysisAndShowsProgressAndDefaultDirectory|FullyQualifiedName~AnalysisResponsivePlotTests.CoefficientReportsKeepFullTextAndScrollInSmallPanes|FullyQualifiedName~AnalysisGuiContractTests.ConnectorExposesAndAppliesAnalysisParameters' --logger 'trx;LogFileName=zernike-entry-removal.trx' --results-directory validation/rechecks/2026-10-26/zernike-entry-removal
```

源码/程序集与结果哈希见 [verification.json](verification.json)。当前使用说明见[用户指南](../../../../docs/USER_GUIDE.md)。Zemax 对应定义见[官方 Fringe Coefficients 文档](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Zernike_Fringe_Coefficients.html)。
