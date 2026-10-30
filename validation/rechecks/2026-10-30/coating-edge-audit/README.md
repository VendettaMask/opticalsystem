# 镀膜低透射边界审查与对照

2026-10-30。产品范围见[修复和保留差距](../../../../docs/COATING_SPECTRUM_EDGE_REVIEW_2026-10-30.md)。只修改共享 Core 半高宽测量和结果测量版本元数据；无材料名、ID、固定层号或示例签名优化分支。默认设计流程、原材料 n/k、前向求解和验收保持。

## 测试结果

| 记录 | 结果 | 范围 |
| --- | --- | --- |
| [有效修复前反例](before/before-edge-corrected.trx) | 2 通过 / 11 失败 | 13 项：缩放三角形、全反射、强吸收评估/保存重开 |
| [修复后重点组](after/after-edge.trx) | 23/23 | 上述 13 项及原 120 组固定物理对照/解析控制/密集采样检查 |
| [完整模块](final-tests/coating-module-final.trx) | 114/114 | 13 个新增用例及原 101 项，包括协议、材料、导数、固定层、候选、公差、取消、保存和离屏桌面流程 |

后两组有重叠，不能相加。完整模块用时约 3 分 57 秒，无失败/跳过；产品代码在这轮后未再修改。测试和诊断新文件随后只调整空白格式。没有重跑主仓全部测试或操作原生桌面。

首份 `before/before-edge.trx` 有三个测试夹具的重复 Air ID 错误，另一个极小数控制尚未越过半值下溢边界；随后修正夹具和控制值，在产品修复前重跑得到有效 11 个失败。首轮保留，不把夹具失败当产品问题。

## 固定数值对照

最终只以 `comparison-verified/` 为准：

- [八份冻结处方重算及三组可行区优化对照](comparison-verified/summary.json)。全部八份指纹/验收状态保持，六份窄带 FWHM 差为零；真实高反/窄带失败仍在。
- [390 个实际 Core 输出及完整 n/k/膜厚](comparison-verified/core-cases.json)。八份处方源自前轮 `coating-general-fixes/final-runtime/`，追加强吸收和全反射，均有 S/P/非偏振。
- [固定 tmmcore 逐点结果](comparison-verified/tmmcore-comparison.json)：最大差 R `6.328271240363392e-15`、T `7.549516567451064e-15`、A `6.106226635438361e-15`，原阈值 `2e-11` 保持。强吸收/全反射控制未去除，绝对 T 下溢时仍保存 Core 原始 log(T)。
- 各份 `.coating.json` 可直接重开；三个 `feasible-*-general.coating.json` 是标明来源的合成控制，不是实测工艺或新默认示例。

可行区对照的三次协议 4 都保持原膜厚，而协议 2 的最差反射率继续降低。所有六次仍通过同一 10% 目标。实际迭代/评价成本不同，不声称等价总成本或提速；详细数值与保留原因见产品报告。

## 完整性、构建和保留过程

[integrity.json](integrity.json) 核对 `Checks`/`Converged` 原函数、前向求解、导数、通用目标/搜索、原 120 组参考及两份上游归档保持；最终测试/对照的 Core 与 Engine DLL 分别字节一致。[verified-hashes.json](verified-hashes.json) 单列相应源文件、固定数据和二进制身份。

正常 Release App/模块依赖图构建完成，诊断正常构建零警告/错误。诊断首次缺 `project.assets.json`，随后按原锁文件、现有缓存还原后成功。受限环境的格式主机命名管道曾拒绝连接，改用可连接本机管道的后台检查。Core 测量、模型及新增测试/诊断文件格式通过；`DesignService.cs` 原有嵌套 `with` 的格式建议不在本轮算法改动范围，保留原排版。本轮实际 diff 空白检查通过。

原始 `comparison/` 是诊断正常构建后的第一次结果。工作区有其他并行 Core/App 构建，`binary-hashes-before-alignment.json` 保留当时不同的二进制身份；第一次按 App/test 相等条件对齐的检查因 App 已被并行构建更新而中止，没有强行覆盖 App。随后只把本轮模块测试的 Core/Engine 及 PDB 复制到诊断输出，重新运行得到 `comparison-verified/`。不把其他并行光学修改列为本轮验收，不声称整个 App 当前磁盘输出与该测试字节一致。

`baseline/` 保存修复前光谱测量及只读搜索源码。源归档、历史结果和真实材料文件不改。没有发布、提交、推送或占用桌面。

## 复现

```powershell
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj -c Release
dotnet tools/diagnostics/CoatingAudit20261030/bin/Release/net10.0/CoatingAudit.dll spectrum-edges <输出目录>
node tools/diagnostics/CoatingAudit20261030/compare-spectrum-edges.mjs <从原归档展开的package目录> <输出目录>/core-cases.json <输出目录>/tmmcore-comparison.json
node tools/diagnostics/CoatingAudit20261030/verify-spectrum-edges.mjs <完整性输出JSON>
```

对照必须加载同一源状态构建的 Core/Engine；本次命名管道限制只影响运行器连接，不属于薄膜模型支持域。离线 Node/固定参考不进入产品计算。

## Git同步范围

仓库保存源码/基线源码快照、冻结输入、完整结果、测试记录和哈希；本机编译比较运行时 `*-runtime/` 保留本地，由本目录 `.gitignore` 排除。上述旧/新二进制配对命令需要已封存的本地运行时；普通当前引擎重算可从工程重新构建。哈希用于核对已封存二进制，不承诺从部分基线源码逐字节重建旧运行时。
