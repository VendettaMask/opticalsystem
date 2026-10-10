# 冻结证据与准备输入验收

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
