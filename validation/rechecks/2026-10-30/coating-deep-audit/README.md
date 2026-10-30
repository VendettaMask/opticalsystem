# 高反/窄带深度诊断，2026-10-30

本批是对固定参考和未达标原因的检查，未修改镀膜产品实现或原验收标准。结论和后续优先级见[分析报告](../../../../docs/COATING_FAILURE_ANALYSIS_2026-10-30.md)。机器可读入口为 [analysis-summary.json](analysis-summary.json)。本目录保留失败终点和理想化控制，不是“全部设计达标”报告。

## 输入、来源与完整性

- 原输入直接读取 `../coating-reference-review/defaults/{HR,NBF1,NBF2}.coating.json`，即上一批实际终点。材料、层数/厚度约束和原目标保留；各输出保存源 `SourceFingerprint`。
- TFStudio 固定 commit `8b942b3f956728a23cea2b35023c8b5d8de17428` 与 tmmcore 0.4.2，来源/许可证见[参考目录](../../../../third_party/coating-reference/README.md)。参考源码只在 `.tmp/coating-review-20261030/` 展开，由 Node 离线调用；不是产品依赖。
- `integrity.json` 记录上批镀膜源码/测试文件及冻结求解器/120 点集/两份归档的哈希比较，原始材料、输入和最终诊断源的哈希，以及本次使用的参考文件与归档字节比较。旧 `source-hashes.json` 不改写；其中诊断 Program.cs 已加入本轮入口，不能宣称旧清单所有文件仍相同。
- 所有变更都在诊断程序和文档。真实材料 k 没有清零；k=0 包装只存在诊断工具，行内有 `Counterfactual`/`Loss`/`Embedded` 标记。

## 输出与已完成工作

| 路径 | 内容和限制 |
|---|---|
| `scans/sweeps.json` | 160 套真实/控制物理扫描：64 高反、60 单腔、36 双腔；层数、膜厚界、介质模型和控制条件逐行标记。均 `ScreenOnly=true` |
| `scans/reference-input.json` | 原输入身份、材料 n/k 在每个实际参考查询波长的值。参考若查询未导出的波长会抛错，不另造色散或外推模型 |
| `scans/core-cases.json` / `tmmcore-comparison.json` | 上述结构的 960 个实际 R/T/A 对照，最大绝对误差 R `1.8652e-14`、T `2.0762e-14`、A `9.5480e-15`，全部在 `2e-11` 判据内 |
| `scans/NBF*-objective-groups.json` | 通过诊断反射调用当前实际目标/网格函数，核对真实 54 行残差布局后分解平方和，没有另写光学或替代目标函数 |
| `reference/reference-results.json` | 实际运行上游 LM/Newton、原型表、耦合构造、1 次有界整数搜索和 vcoat 匹配得到 80 套几何；2 套真实材料高反，78 套明确无吸收窄带控制 |
| `reference/reference-rta.json` | 每套几何 3 个实际参考 R/T/A 点，总计 240 点 |
| `revalidated/reference-candidates-native.json` | 80 套几何分别按设计时条件和原空气/真实材料条件经共享 Core 重评；标出原层数/厚度界。高反用 1200、窄带用 5600 个扫描区间，加中心点；这些固定网格指标不单独证明采样收敛 |
| `revalidated/reference-candidates-rta-check.json` | 上述 240 点经 Core 独立计算，最大误差 R `1.5433e-14`、T `1.4877e-14`、A `6.8834e-15`，全部在 `2e-11` 内 |
| `revalidated/HR-{LM,Newton}.coating.json` | 参考真实材料几何按原产品完整评价保存，均采样收敛、均未达最低 R |
| `reoptimized/{HR,NBF1,NBF2}*` | 从原终点追加最多 80 次正式原优化器迭代的处方、密集验收、实际次数和耗时；均完成且均不达标 |
| `reoptimized/matched-native-HR*` | 与参考同 121 点 R=1 的诊断目标，仍另按原最低 R 完整验收；不等于产品协议 2 或参考含配对 T 的完整 HR 预设 |
| `build.log`, `verification.log` | 最终诊断项目正常依赖构建及定向格式/差异检查结果，不是 App 原生交互或全仓测试 |

960 与 240 是不同来源的比较点，合计 1200；不是新增单元测试数，也不是原冻结 120 组的重跑。本次没有改变或扩大原 Zemax/其他光学基线的认证范围。

## 比较时必须保留的差异

高反配对诊断从相同原终点出发，材料 n/k 和121点目标逐点一致，膜厚界2–2000 nm、步数上限80；本地记录均方，参考记录 RMS，摘要取平方根后比较。参考内部步幅、阻尼、Hessian、停止条件、实际工作数不同，不能用同一步数声称同算力。原产品 R 最差点目标和参考完整 HR 预设均与该诊断不同。

参考窄带向导要求中心波长 H/L 的 k≤1e-5，当前两份膜数据不满足。所以仅在明确的无吸收控制里执行参考原型/匹配/整数搜索，再把全部输出几何放回实际材料。参考 `targetFWHM` 实参15 nm是指定透射水平的通带全宽，分别试边界0.8913和0.5；有界整数搜索用17个通带点（边界T50%、内部100%）、8个截止点，最近在510/590、最远470/630 nm、截止 sigma0.2%。它不同于本地实测峰值半高宽加490–510/590–610 nm全带OD要求。

整数搜索限制镜段1–19、腔阶1–10、重启1、随机种子1234、镜段/腔对称；保存参考返回的3个候选并做匹配。它未在内部施加39层总数或2000nm单层界，原型表还有更高阶候选，所以输出逐个检查。原界内 NBF1/NBF2 分别33/36套几何，本批在理想条件和原空气真实材料下都没有同时满足全部本地数值门槛。不能据此推断完整参考软件失败或原问题全局无解。

## 失败和纠正

首次嵌入式诊断直接使用原 N-BK7 作为入射介质；其550 nm的k约7.235e-9虽很小，但共享 Core 要求透明入射介质，运行被拒绝。没有放宽求解器。本批重新将嵌入式控制的两侧明确设为“原基板 n(λ)、外介质 k=0”，有限膜的 k 按行保留，写入 `ExteriorModel`；该变化只属于反事实控制，原空气/真实基板计算保持。失败尝试不计入160套/960点，旧异常输出未另保存，本段保留发生条件和处理范围。

Node 加载上游 `.js` 模块可能给出 `MODULE_TYPELESS_PACKAGE_JSON` 提示后按 ESM 解析；未修改上游 package.json，也没有通过 npm 安装或运行其界面。第一次汇总查看因序列化全部大型数组导致终端截断，改为独立 `summarize-deep.mjs` 对保存结果做摘要，不影响测量数据。

## 复现

工作目录为仓库根。Node使用本机捆绑运行时，以下 `node` 表示该可执行文件；原参考目录需先由固定归档展开。把 tmmcore 同一归档的 package 内容再展开到 TFStudio 的 `node_modules/tmmcore`，仅满足上游 `src/tmmcore.js` 的相对导出，不改源文件。

```powershell
dotnet build tools/diagnostics/CoatingAudit20261030/CoatingAudit.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -v:minimal
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll deep <new-output>/scans
node tools/diagnostics/CoatingAudit20261030/compare-reference.mjs <new-output>/scans/core-cases.json <new-output>/scans/tmmcore-comparison.json
node tools/diagnostics/CoatingAudit20261030/reference-deep.mjs <new-output>/scans/reference-input.json <new-output>/reference
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll deep <new-output>/revalidated validate-reference <new-output>/reference
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll deep <new-output>/reoptimized refine-HR
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll deep <new-output>/reoptimized refine-NBF1
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll deep <new-output>/reoptimized refine-NBF2
dotnet tools/diagnostics/CoatingAudit20261030/bin/Debug/net10.0/CoatingAudit.dll deep <new-output>/reoptimized matched-hr
node tools/diagnostics/CoatingAudit20261030/summarize-deep.mjs <new-output>
```

不要覆盖本批或前一轮冻结输出；诊断光学计算调用正式共享 Core，所有其他语言只离线执行固定参考或汇总证据。后台执行，没有打开或控制用户桌面，没有提交、推送或发布。
