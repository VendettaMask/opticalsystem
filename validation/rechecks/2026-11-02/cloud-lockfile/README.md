# 云打包锁文件与分析契约修复

日期：2026-11-02。同步基线：`232e878001acf23a1c17799573fcefa4c2929d83`。

最终[云端发布运行 41447293816](https://github.com/VendettaMask/opticalsystem/actions/runs/41447293816)已成功。实际安装包源码为干净提交 `a20ba98cbe7c0e151327444ab70c099b088e0199`，三个平台完整 Release 编译零警告/错误；Windows x64/ARM64 EXE 与 M 系列 Mac DMG 已在[原测试发布](https://github.com/VendettaMask/opticalsystem/releases/tag/v1.0.4-test.20261019)更新。原标签及测试预发布身份不变，Windows 新文件名含源码后缀 `a20ba98c`。

[发布运行 41435413433](https://github.com/VendettaMask/opticalsystem/actions/runs/41435413433)的三个平台，以及[普通 CI 41424094991](https://github.com/VendettaMask/opticalsystem/actions/runs/41424094991)，都在还原 `tools/OpticalSystemDesign.ZemaxComparison/OpticalSystemDesign.ZemaxComparison.csproj` 时报告 `NU1004`。项目仅声明 `net10.0`，入库锁文件却多出 `net10.0/win-x64`。这次发布的安装包生成及 publish 均跳过。

重新生成锁文件只删除该平台段，八项公共依赖记录完全一致，包括版本、contentHash 和项目引用。项目和 CI 的声明、锁定还原、安全审计及警告门禁保持。没有修改光学代码、参考、采样或验收阈值。

本轮验证在后台进行：

- 修复前，本地同参数严格还原复现 `NU1004`，退出码 1。
- 修复后，完整 `OpticalSystemDesign.slnx --locked-mode -p:ContinuousIntegrationBuild=true` 还原成功，退出码 0。
- 比较工具及其 Core/Application 依赖的默认 Release 编译成功，零警告、零错误。
- 未新增镜像实现的测试，未在本机运行全测试、原生光学比较或前台界面；未在本机生成安装包。

## 还原通过后暴露的契约断言

修复提交 `cf4803ee256a83748407a08c2f11e0b824a9c5e9` 的[发布重试 41443918880](https://github.com/VendettaMask/opticalsystem/actions/runs/41443918880)已在三个平台完成严格还原和完整编译，并通过安装/模块与镜头视图检查。后续分析契约集合各为 199 通过、3 失败，仍未生成或发布新包。普通 CI 的 Linux、Windows、Mac 严格还原和主构建也已通过；这不等于其全测试完成。

两项波前页面用例仍按早期“只拉伸入口瞳孔数据”的桌面行为断言；当前桌面已明确使用投影网格重新取样，[用户指南](../../../../docs/USER_GUIDE.md)和[原修复范围](../../../../docs/EXAMPLE_INCOMPARABLE_REPAIR_2026-11-01.md)均已记录，通用 Core 的默认仍保留。修订后精确比较同一投影网格在伪彩色/等高线下的数据，检查控件保留原系列引用、各点和完整 DTO，及显示比例为 1、不二次拉伸。另一个失败是 FFT MTF 选择列表未包含已实现并验证过的 2048；更新完整精确列表，离焦 MTF 的原列表不变。

本轮仅更改两份测试文件，没有修改产品、参考或容差。发布分析契约组加既有 `ParabasalPupilTests` 和通用 Core 显示投影保护，共 **207/207** 通过、零失败/跳过。后续 PNG 导出预检 **23/23**，本机合计 **230 个不同用例**；真实 Skia 绘图另复跑 **42/42**，不累加这 42 次重复。既有原生网格/波前门槛及通用默认数据保持检查均保留；没有新增测试用例或独立 Cooke 残差调查。

## 最终云端结果与边界

三平台发布任务全部成功，原分析契约组各 **202/202**，独立真实绘图各 **42/42**、PNG 导出各 **23/23**，组间和平台间重叠不求和为全仓通过数。安装/升级/内置模块、文档、冻结证据及平台包检查沿用工作流原门禁；有既定 CUDA 硬件用例和两个未单独启用的 Viewer 像素用例跳过，不计为通过。Windows ARM64 在 x64 runner 交叉打包，不称原生执行验收。Mac 实际生成 arm64 自包含 DMG，并在云端临时副本上通过更新替换和重启检查，不操作维护者的应用、安装或桌面；ad-hoc 签名不等于 Developer ID 或公证。

公开附件均与发布正文的源码、大小和服务端 SHA-256 一致，两个 Windows OTA 信封另用内置公钥验签通过。Mac 的签名发布与实际上传检查由成功工作流承担，本机未完整下载安装包，也未另行认证原生 GUI 或光学精度。旧标签仍为 `9242b31abf38b4d97f23d19e4b00f8df47abfabb`，手动包的实际代码见上方 `a20ba98c`，不将标签源码 ZIP 当作新包源码。

本任务旧提交 `cf4803ee` 的普通 CI `41443819160` 已被替代并取消，记为未完成，不计为通过；其此前完成的严格还原/主构建步骤仅用于对应范围的证明。普通 CI 的全量测试与本次发布工作流的定向结果分别记账。

日志和依赖逐项比较结果保存在[单份证据归档](evidence.zip)及其[校验收据](verification.json)。其中原始云端日志分别保留 Windows、Mac 和普通 CI 的同类错误，并保存重试的三个失败断言、复验 TRX、成功步骤摘要及公开附件身份。图片仅记录自动渲染索引和哈希，完整云端图片/包检查仍保留在 Actions；不重复打包源码、第三方包、安装器或图片载荷。
