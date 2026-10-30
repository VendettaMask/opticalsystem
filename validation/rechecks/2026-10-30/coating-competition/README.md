# 材料诊断、窄带竞争与匹配，2026-10-30

本批实现 C# 新窄带协议 3，不修改真实材料 k 或原验收。使用与边界见[实现说明](../../../../docs/COATING_NARROWBAND_COMPETITION_2026-10-30.md)。原诊断与历史结果分别保留在相邻 `coating-deep-audit`、`coating-reference-review`，不覆盖其输入、处方、误差阈值或计数。

## 最终结果与工作量

[comparison-summary.json](comparison-summary.json) 包含 11 次完成的物理运行：4 组新旧生成＋优化配对、高反复核、2 组真实材料完整搜索。四组配对逐字段核对目标、材料、层数/膜厚界及完整阶段 24 次上限，均只有设计协议不同。新流程增加离散筛选、中心匹配和短程优化，不能比较为同总算力。

| 最终目录 | 性质 / 流程 | T峰或最低R | FWHM | 原完整验收 |
|---|---|---:|---:|---|
| `final/legacy-real1` | 真实材料单腔旧流程 | T 72.603178% | 27.228174 nm | 失败 |
| `final/modern-real1` | 同输入，新生成＋优化 | T 70.563563% | 15.591383 nm | 带宽通过，峰值和两侧截止失败 |
| `final/legacy-real2` | 真实材料双腔旧流程 | T 68.946148% | 20.297745 nm | 失败 |
| `final/modern-real2` | 同输入，新生成＋优化 | T 62.817031% | 15.995719 nm | 仅峰值失败 |
| `final/legacy-ideal1` | 合成无吸收常折射率单腔 | T 87.122497% | 14.982295 nm | 旧流程已通过 |
| `final/modern-ideal1` | 同一合成控制，新流程 | T 97.352516% | 14.993399 nm | 通过 |
| `final/legacy-ideal2` | 合成无吸收常折射率双腔 | T 74.617834% | 16.256067 nm | 失败 |
| `final/modern-ideal2` | 同一合成控制，新流程 | T 99.884979% | 14.998578 nm | 通过 |
| `final/modern-hr` | 高反原协议 2 与新材料诊断 | 最低R 95.984479929% | 不适用 | 仍未达98% |
| `final/modern-search-real1` | 真实单腔，4完整起点×24次上限 | T 70.845954% | 15.813103 nm | 峰值和两侧截止失败 |
| `final/modern-search-real2` | 真实双腔，4完整起点×24次上限 | T 63.334901% | 15.792581 nm | 峰值和左截止失败 |

全部11次最终采样收敛。真实材料仍使用冻结的 Rodriguez-de Marcos 薄膜数据；理想控制另建 nL=1.45、nH=2.1、nS=1.52 的明确合成材料身份，没有改写真实材料。对照只覆盖这几个规定目标，不能宣称真实材料已达标、全局无解、任意规格成功率提升或完整参考软件复现。

每个子目录保存 `input.coating.json`、可重开 `result.coating.json`、原逐项检查、生成阶段记录、耗时和阶段事件的 `summary.json`、实际膜系的 `core-cases.json`。运行期间有并行计算/构建，墙钟时间不作通用性能保证。单/双腔新生成分别评价475/495个初始结构、累计513/636个离散结构，试32个匹配提议并短程精化12候选；其他合成控制实际次数可能不同，均在原始记录中，不推算为理论数。

## 固定参考与独立核对

- 原 TFStudio `8b942b3f956728a23cea2b35023c8b5d8de17428` 与 tmmcore 0.4.2 归档不变。实际源在 `.tmp/coating-review-20261030` 离线展开，不成为产品依赖。
- `reference-competition.mjs` 实际调用未修改的 `buildPrototypeLayers` 和 `adjustToIncidentMedium`，产生测试夹具 `Fixtures/tfstudio-competition-8b942b3.json`：10个跨1–5腔的奇偶/非均匀镜段、非统一腔阶原型，6个无吸收匹配控制。原8个协议2原型夹具保留。
- 当前测试逐层比较10个新原型。6组匹配均对同一常折射率输入验证原型与参考中心 R/T：原型保持不变、原膜厚/层数界保留、原输入不变；中心R/T的优化对照容差为 `1e-5`（比例），不是物理求解误差阈值。参考匹配R约 `1e-31`，本地通过数值优化逼近，不要求取到同一组非唯一膜厚根。这6组新匹配对照在0°，不认证任意角度或所有腔数的优化收益。
- [core-cases.json](core-cases.json) 合并最终11次输出的546个 S/P/非偏振 R/T/A 点，再实际运行固定 tmmcore。最大绝对差 R `1.02696e-14`、T `8.10463e-15`、A `5.99521e-15`，全部满足原比较判据 `2e-11`，见[tmmcore-comparison.json](tmmcore-comparison.json)。这是计算对照，不将参考不同通带定义/目标函数称为同一优化问题。
- [integrity.json](integrity.json) 保存最终源码/新夹具/材料文件哈希，并核对共享薄膜求解器、原120组参考、两份源归档与前批哈希一致。正式 `Converged` 和 `Checks` 函数尾部与当前HEAD原版本逐字符一致（仅统一换行后比较）；材料诊断不新增或放宽验收项。

## 测试、构建与完整性

按完整当前用例身份合并 **75个不同用例的最新结果全部通过**，不是一次75项全绿运行。最终影响面 **22/22**，含6组参考匹配、新高反损失诊断、完整/固定层搜索、工作量、取消、保存重开、三尺寸离屏工作流及新旧协议保持。原模块首轮68项是67通过、1失败，单一匹配Fact随后修复复验1项通过；该Fact最终扩展成6个参数化当前身份，旧身份不重复计入75，失败记录仍保留。[test-ledger.json](test-ledger.json) 显示每个最新结果的来源、时长及退休身份。

- 最终普通默认依赖构建 `final/build-6.log`：App、必装模块及镀膜测试项目，0警告/错误。最终诊断程序普通依赖构建见 `final/diagnostic-build-2.log`，0警告/错误；更早的成功构建日志保留。
- 最终 `final/test-results/affected.trx`：22通过、0失败/跳过；初轮及中间匹配结果在 `attempt-1`。
- 最终14个本轮C#文件定向格式检查、相关JS语法、文档链接、新增文件空白和差异检查见 `verification.log`。未运行全仓、发布安装包或操作用户桌面。

物理批次使用最终匹配/目标算法完成，随后仅补充超过候选数时的重复起点扰动预算及优化结果的原型元数据继承；本批4起点未触发重复分支，元数据不改变R/T/A。最终源码的固定层多起点和影响面已由22项复验覆盖；不把源码清单冒称为每个旧开发运行的逐字节二进制身份。

## 保留的失败与纠正

1. 首个新增匹配测试失败：同相位三起点加中心强度残差，最佳R仍约0.0164644。改为Core复反射振幅实/虚部，并对两层取独立3×3相位组合后专项通过，最终6组参数化参考控制全部通过。没有降低匹配容差或正式验收门槛。
2. 初始并行构建曾争用 `Core/obj` 输出，CS2012；工具原始输出未另存完整日志，此处保留原因。之后串行构建成功。又一次在自有首轮测试宿主仍运行时重建，测试输出DLL复制被锁，见 `attempt-1/tests-build-2.log`；未关闭用户软件，等待自有测试结束后恢复默认输出重建。
3. 新增高反诊断测试首次漏传0°参数，CS7036，见 `final/build-2.log`；修正后通过。另一次共享工作区的绘图类型在编辑中尚缺定义导致 `AnalysisCurveWidth` 编译错误，见 `final/build-3.log`；本轮未修改该绘图文件，类型完成后正常重建恢复。
4. `attempt-1/real1`、`attempt-1/ideal2` 是强度残差版本开发终点；`attempt-1/real2-amplitude` 是首个复振幅版本终点。只保留为开发证据，不能混入最终11例、最终参考点数或合成控制通过数。旧版无吸收双腔在第一次开发版本虽通过，最终数字以 `final/modern-ideal2` 为准。
5. Node 对上游 `.js` 提示 `MODULE_TYPELESS_PACKAGE_JSON` 后按ESM加载，未编辑上游 package.json 或联网安装依赖。没有将该提示算作数值失败。

## 复现入口

在仓库根，按[前批参考展开说明](../coating-deep-audit/README.md#复现)准备固定源；`node`表示本机捆绑运行时。使用新输出目录，不覆盖本批：

```powershell
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -v:minimal
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll competition <new-output>/final/modern-real1 real1 24 modern
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll competition <new-output>/final/legacy-real1 real1 24 legacy
```

按同样方式运行 `real2`、`ideal1`、`ideal2` 的两个版本；`hr 24 modern` 复核高反；追加参数 `search` 执行4完整起点的窄带搜索。最后用 `summarize-competition.mjs <new-output>` 合并，`compare-reference.mjs <new-output>/core-cases.json <new-output>/tmmcore-comparison.json` 独立对照。参考夹具生成器可输出到新临时文件逐字节比较，不要覆盖冻结夹具。没有提交、推送或发布。
