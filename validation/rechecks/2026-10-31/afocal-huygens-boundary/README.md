# 无焦 Huygens 遮挡与快速相位证据

本轮为纯 C#、共享 Core 的显式高阶积分改进。默认原生协议、原输入、原参考、1e-10 独立精度门槛与 100,000,000 累计积分预算保持。认证范围和未完成部分见[报告](../../../../docs/AFOCAL_HUYGENS_BOUNDARY_PHASE_2026-10-31.md)。

`assessment.json` 收录最终按身份去重 221 项测试（150+71）、15 组独立数值控制、应用成功/拒绝记录、全部采样尝试、旧圆瞳逐值复放、八个前阶段依赖 SHA256、当前源码及六处 Core 身份。早期失败运行单独保留，未加进最终通过数。没有新原生采集或正式原生 Pass，不声称任意复杂出瞳误差已全部关闭。

`evidence.zip` 按 SHA256 去重，`entry-map.json` 将每个逻辑路径映射到实际成员、长度和摘要；封存后逐项解压重算 SHA256。`verification.json` 记录压缩包摘要及核验结论。不会覆盖旧封存，不包含专有 Zemax 程序集或运行二进制。

复验入口：`tools/diagnostics/AfocalHuygens20261030/Seal-BoundaryPhase.ps1 -RepositoryRoot D:/Projects/opticalsystem`。封存脚本拒绝覆盖已有证据。全部后台执行，无桌面操作、提交、推送或发布。
