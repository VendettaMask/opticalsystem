# 云打包 NU1004 锁文件修复

日期：2026-11-02。同步基线：`232e878001acf23a1c17799573fcefa4c2929d83`。

[发布运行 41435413433](https://github.com/VendettaMask/opticalsystem/actions/runs/41435413433)的三个平台，以及[普通 CI 41424094991](https://github.com/VendettaMask/opticalsystem/actions/runs/41424094991)，都在还原 `tools/OpticalSystemDesign.ZemaxComparison/OpticalSystemDesign.ZemaxComparison.csproj` 时报告 `NU1004`。项目仅声明 `net10.0`，入库锁文件却多出 `net10.0/win-x64`。这次发布的安装包生成及 publish 均跳过。

重新生成锁文件只删除该平台段，八项公共依赖记录完全一致，包括版本、contentHash 和项目引用。项目和 CI 的声明、锁定还原、安全审计及警告门禁保持。没有修改光学代码、参考、采样或验收阈值。

本轮验证在后台进行：

- 修复前，本地同参数严格还原复现 `NU1004`，退出码 1。
- 修复后，完整 `OpticalSystemDesign.slnx --locked-mode -p:ContinuousIntegrationBuild=true` 还原成功，退出码 0。
- 比较工具及其 Core/Application 依赖的默认 Release 编译成功，零警告、零错误。
- 未新增镜像实现的测试，未运行全测试、原生光学比较或前台界面；未在本机生成安装包。

日志和依赖逐项比较结果保存在[单份证据归档](evidence.zip)及其[校验收据](verification.json)。其中原始云端日志分别保留 Windows、Mac 和普通 CI 的同类错误；不复制整个源码或第三方包载荷。修复后云端重新打包的结果尚待单独记录。
