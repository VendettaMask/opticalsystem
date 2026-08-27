# 精度与页面对比验证（2026-08-04 更新）

默认精度权威是仓库固定的 Zemax OpticStudio 2026 R1 `123456.ZMX` 基线。Optiland 0.5.8 Cooke/Tessar 仅作为辅助回归，不参与默认精度结论。

该基线只证明 `123456.ZMX`、已保存的分析设置和 OpticStudio 2026 R1 捕获结果之间的对标精度。报告中的采样数、视场号、波长号、焦移范围等都是“基准文件捕获设置”，不得称为 Zemax 通用默认值或规格。Core 构造器与 `AnalysisCatalog` 保持通用默认值；Workbench GUI 可以选择产品预设；基准测试必须显式传入捕获设置。

## 当前结论

| 验证项 | 结果 |
| --- | ---: |
| Workbench 分析重算 | 69/69 成功，0 失败 |
| 等价数值映射 | 30 项 |
| 高度一致 | 26 项 |
| 接近 | 4 项 |
| 明显差异 | 0 项 |
| 非等价映射排除 | 2 项 |
| .NET 回归测试 | 截至 2026-08-26 正式产品严格构建 0 警告、0 错误，全量回归测试 793/793；独立实验室定向测试 7/7，不参与精度结论 |
| Python 报告测试 | 14/14 通过 |

“高度一致”的判定为中位 NRMSE ≤ 3% 且 P90 ≤ 10%；“接近”为中位 NRMSE ≤ 10% 且 P90 ≤ 25%。当前 30 项等价映射中没有“明显差异”，但仍有 4 项处于“接近”而不是“高度一致”，因此不能把本结论扩大为所有 Zemax 功能或所有镜头都已逐点等同。

评价函数导入验证与上述 69 页分析精度基线相互独立。`[MS-L7]` 的 103 行评价函数顺序和源参数已由 `6/6` 定向测试锁定，但九类禁用只读兼容操作数尚无 Zemax 等价计算，因此不能计入“高度一致”或“接近”的分析数值结论。

完整逐项数值、30 张数值图和 69 张页面对照图见 [当前全面对比报告](../artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/COMPARISON_REPORT.md)。

本轮回归还锁定了 RMS vs Field 的通用分析契约：Spot 模式按 `Field Density + 1` 和 Orientation 从零连续扫描至最大定义视场，不遍历 Field Editor 离散行；波前 RMS 的 chief reference 仅移除加权 piston，centroid reference 移除加权最佳拟合 piston 与 X/Y tilt。这些规则不依赖 `123456.ZMX`，也没有引入镜头专用比例、偏置或经验系数。

## 本轮关闭的明显差异

所有修正都来自 Zemax 保存设置、官方物理定义或直接光线追迹验证，没有加入倍率、偏置或镜头专用经验系数。

| 分析 | 根因与修正 | 当前结果 |
| --- | --- | ---: |
| Wavefront Map | 使用 `123456.ZMX` 捕获所用的 64 点偶数瞳面坐标、波长/视场选择，并在 RMS 中移除 piston | 中位 NRMSE 约 0.000001% |
| Huygens PSF | 使用 Zemax 自动 Image Delta，偶数网格中心落在索引 `N/2` | NRMSE 0.41% |
| Lateral Color | 改为最长波长主光线截距减最短波长截距，执行系统 ray aiming，并按视场计算工作 F/# 的 Airy 半径 | 中位 NRMSE 0.23% |
| Optical Path Difference | 复色选择共用主波长参考球中心，各波长保留对应参考球半径 | 报告中位/P90/最差均约 0.00% |
| Pupil Aberration | 使用 Zemax 每侧 20 条光线的 41 点采样；开启 `RAIM` 时按瞄准残差计算 | 中位 0.20%，最差 0.44% |
| Contrast Loss Map | 使用保存的 `13×13`、100 cycles/mm、主波长 2、视场 1，并按 Moore–Elliott 定义计算 | NRMSE 0.78% |
| Extended Source Encircled Energy | 使用基准原始 `LETTERF.IMA`、6.3639610306789285 mm 全宽、397 点、10 mm 最大半径和约 200 万光线 | NRMSE 2.00% |
| Huygens MTF | 使用 Zemax 保存的 `32×32` 瞳面/像面、自动 Image Delta、全部波长和全部视场 | 中位 0.19%，最差 1.70% |

Pupil Aberration 在 `RAIM` 开启时是约 `1e-6%` 的近零量。报告器只在误差分母使用 `1e-4%`（瞳孔半径的百万分之一）绝对数值分辨率下限，避免把数值零附近的舍入噪声放大；该下限不会修改任何光线或分析结果。

## 仍为“接近”的项目

| 分析 | 中位 NRMSE | P90 | 最差 |
| --- | ---: | ---: | ---: |
| Geometric Line Edge Spread | 5.60% | 5.82% | 5.88% |
| MTF | 5.15% | 6.52% | 7.24% |
| Encircled Energy | 3.84% | 4.18% | 4.25% |
| Ray Fan | 2.93% | 15.75% | 26.09% |

这些项目已达到当前“可接受”阈值，但后续若继续提高精度，应逐项追查物理定义、采样和参考系，不能用比例因子把曲线强行贴合。

## 非等价映射

`Centroid Sphere Wavefront` 和 `Best Fit Sphere Wavefront` 不映射到 Zemax `WavefrontMap`。前两者使用不同的拟合参考球；Zemax Wavefront Map 使用波长参考球，`Remove Tilt` 只移除线性 X/Y 倾斜。名称相近但物理量不同，因此只做功能与截图审查，不计入精度通过或失败。

## 数据与复现

- `current-manifest.json` 和 `current/*.json` 保存本轮 69 项当前结果、真实设置和耗时。
- `comparison.json` 保存 30 项等价映射的逐序列/逐网格误差，以及 2 项排除说明。
- `images/numeric/` 保存数值对比图；`images/screenshots/` 保存页面并排图。
- Zemax 捕获基线没有在本轮重新生成或覆盖；Workbench 一侧全部由当前代码重算。

从仓库根目录复现：

```powershell
dotnet run --project tools/OptilandWorkbench.AccuracyCapture -- `
  artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/current-manifest.json `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31

python tools/zemax_parity/generate_workbench_comparison.py `
  artifacts/zemax/123456-zemax-2026-r1-baseline `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31 `
  artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-30
```

基线完整性只读验证：

```powershell
python tools/zemax_parity/verify_baseline.py artifacts/zemax/123456-zemax-2026-r1-baseline
```
