# 云打包 NU1004 锁文件修复

日期：2026-11-02。同步基线：`232e878001acf23a1c17799573fcefa4c2929d83`。

[发布运行 41435413433](https://github.com/VendettaMask/opticalsystem/actions/runs/41435413433)的三个平台，以及[普通 CI 41424094991](https://github.com/VendettaMask/opticalsystem/actions/runs/41424094991)，都在还原 `tools/OpticalSystemDesign.ZemaxComparison/OpticalSystemDesign.ZemaxComparison.csproj` 时报告 `NU1004`。项目仅声明 `net10.0`，入库锁文件却多出 `net10.0/win-x64`。这次发布的安装包生成及 publish 均跳过。

重新生成锁文件只删除该平台段，八项公共依赖记录完全一致，包括版本、contentHash 和项目引用。项目和 CI 的声明、锁定还原、安全审计及警告门禁保持。没有修改光学代码、参考、采样或验收阈值。

本轮验证在后台进行：

- 修复前，本地同参数严格还原复现 `NU1004`，退出码 1。
- 修复后，完整 `OpticalSystemDesign.slnx --locked-mode -p:ContinuousIntegrationBuild=true` 还原成功，退出码 0。
- 比较工具及其 Core/Application 依赖的默认 Release 编译成功，零警告、零错误。
- 未新增镜像实现的测试，未运行全测试、原生光学比较或前台界面；未在本机生成安装包。

## 还原通过后暴露的契约断言

修复提交 `cf4803ee256a83748407a08c2f11e0b824a9c5e9` 的[发布重试 41443918880](https://github.com/VendettaMask/opticalsystem/actions/runs/41443918880)已在三个平台完成严格还原和完整编译，并通过安装/模块与镜头视图检查。后续分析契约集合各为 199 通过、3 失败，仍未生成或发布新包。普通 CI 的 Linux、Windows、Mac 严格还原和主构建也已通过；这不等于其全测试完成。

两项波前页面用例仍按早期“只拉伸入口瞳孔数据”的桌面行为断言；当前桌面已明确使用投影网格重新取样，[用户指南](../../../../docs/USER_GUIDE.md)和[原修复范围](../../../../docs/EXAMPLE_INCOMPARABLE_REPAIR_2026-11-01.md)均已记录，通用 Core 的默认仍保留。修订后精确比较同一投影网格在伪彩色/等高线下的数据，检查控件保留原系列引用、各点和完整 DTO，及显示比例为 1、不二次拉伸。另一个失败是 FFT MTF 选择列表未包含已实现并验证过的 2048；更新完整精确列表，离焦 MTF 的原列表不变。

本轮仅更改两份测试文件，没有修改产品、参考或容差。发布分析契约组加既有 `ParabasalPupilTests` 和通用 Core 显示投影保护，共 **207/207** 通过、零失败/跳过。既有原生网格/波前门槛及通用默认数据保持检查均保留；没有新增测试用例或独立 Cooke 残差调查。新提交仍需通过完整云端发布门禁。

日志和依赖逐项比较结果保存在[单份证据归档](evidence.zip)及其[校验收据](verification.json)。其中原始云端日志分别保留 Windows、Mac 和普通 CI 的同类错误，并保存重试的三个失败断言及 207 项复验；不复制整个源码、第三方包或图片载荷。
