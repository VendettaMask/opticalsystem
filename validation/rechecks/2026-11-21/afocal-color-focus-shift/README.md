# 无焦瞄准镜色焦移修复

用户要求增加一个子智能体核查两图差异。子智能体独立计算并负责Core修复与光学回归，主智能体负责运行时、带单位摘要、界面/导出、统一检查及文档。

## 根因与修复

同一内置`Afocal/Afocal Riflescope.ZMX`为无焦像方，主波长0.587 µm。旧分析仍按有限像方的`-y/u`求各波长焦点距离，再相减并显示µm。出射斜率穿零时出现焦距极点，旧曲线变化约`1.321819896e9 µm`，与用户截图异常量级一致。

无焦应先按每个波长的出射状态求有符号屈光度，再减主波长近轴值：

`V(λ) = -1000 u(λ) / y(λ)`，`ΔV(λ) = V(λ) - V(λprimary)`，单位D。

非零光瞳区域先把正负有效真实光线分别转换为屈光度后平均，仍减主波长近轴参考；不能先平均焦距或对焦距差取倒数。平行出射是有效0 D，全部区域光线失效则明确不可用。保留既有轴上Y截线定义，不扩展为任意非轴对称系统的完整二维光焦度求解。有限像方原µm曲线、摘要keys及冻结锚点保持。

运行时沿用D/µm对应的手动范围及步进，0表示自动。图签通过`ColorFocusShiftSummaryDto`传递未舍入数值和实际单位，不再读取已格式化行；无焦不显示长度单位的衍射极限。中英日参数、坐标、摘要及实际本地词库同步。图签的小数位仍遵循共享数值显示设置，完整计算值保存在本目录JSON。

## 结果与边界

- 101点的最大变化为 **0.158470469044 D**，与冻结原生图签 **0.1585 D** 在四位小数内一致。峰值约+0.04204 D，长波端约−0.11643 D，原异常极点消失。
- `native-curve-comparison.json`核对原生JPG中460条像素行：RMS差约0.000343 D、最大差约0.001071 D。原图一个横向像素为0.000506 D；这是声明坐标下的截图数字化，不是原生数值导出或全精度认证。原始JPG/CFG及旧图册保持原样。
- [Light新图](color-focus-Light-zh-CN-export.png)、[Dark新图](color-focus-Dark-en-US-export.png)、[Isekai新图](color-focus-Isekai-en-US-export.png)、[日文新图](color-focus-Light-ja-JP-export.png)。`capture-summary.json`记录3种主题、3种语言的4个真实面板及4张实际`CapturePlotImage`→`SavePlotImage` PNG，共8张画面；已目视核对曲线、单位、图签和英文固定主题。
- `results/color-focus.trx`：主测试工程筛选`ColorFocusShift`，**15项通过、0失败/跳过**。包括9项光学检查与6项三语finite/afocal参数、Runtime/DTO、未舍入摘要及污染显示行的图签独立性检查。
- `default-release-build.log`：默认Release主程序构建成功，0警告、0错误。未运行完整测试工程、95镜头矩阵、报告格式专项导出或新原生窗口捕获；没有操作、关闭或重启用户程序。

独立原始出射高度/斜率、修复前有限系统锚点和计算源码见[independent](independent/README.md)。`fixed-core.json`保存修复后的原始数据与101点曲线，`before-reconstructed.json`根据修复前冻结出射数据复原旧公式的异常曲线。

## 检查过程保留

初次图签检查遗漏了服务层提供的展示类型，测试定位失败，补齐该测试设置后通过。共享测试输出曾被后台testhost短暂占用，等待释放后重试，未终止其他进程；日志保留。离屏程序初次编译遇到并行构建期间App引用暂时缺失，默认构建恢复后通过。

三语扩展检查发现运行时读取的是嵌入词库，只有源码词条不足以更新实际资源。完整`Localization sync`被其他并行功能的25条漏译阻止（见`localization-sync.log`）；本轮仅将11条自有的、已明确三语译文的词条增量写入实际词库，未改无关条目。随后三语实际词库检查及新图通过；未把全仓库本地化检查计为通过。

## 复现

```powershell
$env:DOTNET_PROCESSOR_COUNT='2'
dotnet build src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release --no-restore -m:1 -nr:false
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release -p:BuildProjectReferences=false --filter 'FullyQualifiedName~ColorFocusShift' --no-restore -m:1 -nr:false
dotnet run --project validation/rechecks/2026-11-21/afocal-color-focus-shift/render/Capture.csproj -c Release -p:BuildProjectReferences=false -- validation/rechecks/2026-11-21/afocal-color-focus-shift 'src/OpticalSystemDesign.App/Assets/SequentialExamples/Afocal/Afocal Riflescope.ZMX'
python validation/rechecks/2026-11-21/afocal-color-focus-shift/compare_capture.py
```
