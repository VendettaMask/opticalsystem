# 独立数值核查

子智能体先只读核查原代码与内置 `Afocal/Afocal Riflescope.ZMX`，使用修复前已编译的Core记录了这些数据。`results.json`保存101个波长的近轴出射高度`y`（mm）、斜率`u`、原轴向焦距`focus=-y/u`（mm）以及直接计算的`v=-1000*u/y`（D）。`finite-before.json`保存修复前Cooke有限像方在光瞳区域0与0.5时的曲线/摘要锚点，已写入定向回归。

`Program.cs`保留独立核查计算，并打印现有共享`FieldCurvatureMetrics.AfocalVergences`的真实傍轴光线结果。它不提供运行时分析数据。迁移时仅把原`.tmp/afocal-color-independent`中的结果输出位置改为可传参数，项目引用改为从本目录回到仓库根的路径。

## 公式与原始结果

无焦输出的焦移应先将每个波长转为有符号屈光度，再减主波长近轴屈光度：

`V(λ) = -1000 u(λ) / y(λ)`；`ΔV(λ) = V(λ) - V(λprimary)`。

该文件主波长是0.587 µm，`Vprimary=0.0881561849085 D`。例如：

| 波长（µm） | 原始V（D） | 相对主波长ΔV（D） |
| --- | --- | --- |
| 0.486 | 0.0879004628236 | -0.0002557220849 |
| 0.530 | 0.130193538021 | 0.0420373531125 |
| 0.587 | 0.0881561849085 | 0 |
| 0.640 | 0.0025391921249 | -0.0856169927836 |
| 0.656 | -0.0282769103792 | -0.1164330952877 |

101点范围为 **0.158470469044 D**，与用户截图的0.1585 D吻合。共享真实傍轴光线的V与上述近轴值一致至约1e−9 D。0.64 µm附近出射斜率穿零，因此原代码使用的`-y/u`产生焦距极点；正确的`-1000u/y`会平滑穿过零。

非零光瞳区域分别追迹正负真实光线，先求每条光线的`V=-1000 direction.Y/(position.Y*direction.Z)`后平均，再减主波长近轴V。平行输出是有效的0 D；无有效出射样本时应报告不可用。该处保留既有轴上、Y截线的参考定义，没有增加一般非轴对称系统的完整二维光焦度求解。

## 复现

在仓库根、已完成默认Core构建后执行：

```powershell
dotnet run --project validation/rechecks/2026-11-21/afocal-color-focus-shift/independent/Probe.csproj -c Release -p:BuildProjectReferences=false -- validation/rechecks/2026-11-21/afocal-color-focus-shift/independent
```

程序还会打印当前有限系统的既有色焦移数据；`finite-before.json`是修复前冻结锚点，不由程序覆写。仅迁移源代码和证据，没有额外重跑App构建或操作用户桌面。
