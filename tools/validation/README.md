# 冻结证据与准备输入验收

Windows 安装升级验收另见 [2026-10-12 报告](../../docs/WINDOWS_INSTALLATION_UPGRADE_2026-10-12.md)和 `scripts/verify-windows-upgrade.ps1`。该脚本仅允许独立测试产品标识，实际安装矩阵 48/48 与正式安装相关定向回归各 32/32 分别记录，不修改冻结光学账本，也不替代光学完整回归。

2026-10-11 新增 Git 字节检查，核对原件及 Git 提交/索引，避免工作区完整但远端 blob 不完整；原 E04 索引与全新检出的结果保留在[阶段记录](../../docs/FROZEN_GIT_INTEGRITY_REPAIR_2026-10-11.md)。2026-10-19 为产品更名后的四项证据增加显式迁移，当前结果以[最新账本](../../docs/validation/FROZEN_EVIDENCE_RELOCATION_REPAIR_2026-10-19.json)为准。10 月 10 日的专用 Collect 脚本保留原验收修订和 2660/2668 条件，不用当前通过改写旧账本。

```powershell
pwsh -NoProfile -File tools/validation/GitFrozenIntegrity.ps1 -RepositoryRoot . -ManifestPath artifacts/validation/n02-layer-diagnosis-20261009/frozen-after.json -ExpectedAggregateSha256 3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10 -RelocationMapPath tools/validation/frozen-evidence-relocations.json -ExpectedRelocationMapSha256 6eefb25cbb1c35a120f73e8bbb761aac16759e7b8491b4606475caec8dfd4f6e
pwsh -NoProfile -File tools/validation/GitFrozenIntegrity.Tests.ps1 -OutputDirectory <全新夹具目录>
```

提交前可用 `-Source Index`；默认 HEAD 检查不接受“索引已修复”替代提交完整。两份脚本回归已覆盖迁移的正负控制，不纳入 C# 正式测试计数，实际结果统一见[当前状态](../../docs/CURRENT_STATUS.md)。CI 在三个平台核对固定聚合身份及迁移表身份；远端执行结果不由本机通过代替。

[迁移表](frozen-evidence-relocations.json)只能绑定原清单内的原 SHA-256，物理目标唯一且不能隐藏重现的旧路径。三份夹具在现目录读取，旧配置在既有原字节归档读取；不根据 JSON 语义、路径前缀或缺失项跳过规则放行。迁移表本身必须有固定字节哈希，并存在于被检的 Git 来源；链接、越界、缺失、内容或换行变化均拒绝。不传迁移表继续按原路径失败，历史聚合仍按原逻辑路径计算。本表不供产品兼容旧名称使用。

`FrozenIntegrity.ps1` 只读取清单和当前文件，不修改参考。`Get-FrozenIntegrityReport` 返回实际差异，`Assert-FrozenIntegrity` 在差异上抛错。历史 `unchanged` 标记不参与判定。聚合采用保留大小写的 ordinal 路径、小写 SHA-256、UTF-8 无 BOM 与带末尾 CRLF 的行序列。

```powershell
pwsh -NoProfile -File tools/validation/FrozenIntegrity.Tests.ps1 -OutputDirectory <全新输出目录>
pwsh -NoProfile -File tools/validation/CollectPreparedPupilRepair20261010.ps1 -RepositoryRoot . -EvidenceRoot artifacts/validation/prepared-pupil-integrity-repair-20261010
```

第一条使用新建的独立测试夹具，不接触冻结参考。第二条要求本轮最终构建日志、完整 Release/比较 TRX、Debug 定向 TRX、修复前失败 TRX 和四组原生准备输入控制已经存在；核对身份、次数、源/二进制哈希、原生文件和实际残差后生成独立账本。它会如实记录当前历史参考差异，绝不将其记作完整性通过或覆盖 10 月 9 日账本。

光学数值计算仍只调用正式 Core。详见[修复报告与剩余边界](../../docs/PREPARED_PUPIL_INTEGRITY_REPAIR_2026-10-10.md)。

## FFT 能量网格后续复验

`CollectFftEnergyGridRepair20261010.ps1` 针对后续零像素几何修复建立新账本，不覆盖来源保护阶段的账本。要求当前正式全量、Debug/Release 定向、完整比较工具、默认构建、四组准备输入控制和独立 Huygens 测量已完成；核对原正式测试身份/次数、唯一比较失败的消息与输出、N02 两条残差、历史字节差异和 165 份原生文件。

```powershell
pwsh -NoProfile -File tools/validation/CollectFftEnergyGridRepair20261010.ps1 -RepositoryRoot . -EvidenceRoot artifacts/validation/fft-energy-grid-integrity-20261010
```

Huygens 的五项独立测试只验证测量能够完成，不证明精度通过，不加入正式全量计数。任何未通过的传播候选都不供应正式计算。详见[积分修复和传播对照](../../docs/FFT_ENERGY_GRID_INTEGRITY_REPAIR_2026-10-10.md)。

## Huygens 偏振修复严格复验

`CollectHuygensPolarizationRepair20261012.ps1` 要求最终默认构建、正式完整 Release、完整比较工具及 Debug 相关 TRX 已经完成；逐身份保留旧正式与工具集合，核对原两个 1024 点非偏振模式的数值和固定判据、306 份既有原生捕获、源码及默认 Core 哈希，以及冻结工作区和 Git 原字节。它另保留修复前两项失败、首次全量失败及相应源码版本，不能用定向通过替代完整验收。

```powershell
pwsh -NoProfile -File tools/validation/CollectHuygensPolarizationRepair20261012.ps1 -RepositoryRoot . -EvidenceRoot artifacts/validation/huygens-polarization-repair-20261012
```

入口只核验既有结果，不运行 Zemax、连接许可或生成新原生参考；已有同阶段账本存在时拒绝覆盖。非偏振平均功率、透明介质相干 XYZ 逐分量传播与完整吸收复电场是不同能力，不因功率或内部数学回归通过而声称获得全部原生矢量认证。

## 三维出瞳与空间刀口扩展复验

`CollectPupilFoucaultExtension20261012.ps1` 核验扩展阶段的默认两配置构建、完整 Release、比较工具、相关 Debug 和实际绘图 TRX。它逐身份和次数保留上一阶段 4697 项正式测试及 182 项工具测试，新增 36 项；Debug 保留原 440 项并另选 78 项既有案例及 36 项新增案例，不将集合相加。另核实宽窗边界修正前的失败和 4731 项完整通过，不能把旧阶段通过替代修正后的 4733 项验收。既有界面参数清单精确补入新增项，原数值断言和容差不变。

```powershell
pwsh -NoProfile -File tools/validation/CollectPupilFoucaultExtension20261012.ps1 -RepositoryRoot . -EvidenceRoot artifacts/validation/pupil-foucault-extension-20261012
```

该入口另核实 306 份原生捕获、冻结工作区/Git 原字节、默认 Core 副本、源码、实际绘图截图以及上一阶段账本的固定身份，保留夹具和界面契约的失败尝试，拒绝覆盖同阶段账本。它不连接 Zemax 或新建原生参考。几何最小二乘光阑像、均匀介质 Maxwell 角谱和理想薄幅度刀口，不等于唯一精确波动出瞳、全系统矢量 POP 或真实刀刃电磁散射；新认证与整体发布检查仍单列。
