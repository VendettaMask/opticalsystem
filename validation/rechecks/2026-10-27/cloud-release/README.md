# 2026-10-27 云发布记录

## 首次运行与锁文件修复

用户要求云发布一次，目标为现有测试发布 `v1.0.4-test.20261019`，选择 Windows x64、Windows ARM64 与 Apple Silicon Mac，保留版本/标签。首次 [Actions 40430271074](https://github.com/VendettaMask/opticalsystem/actions/runs/40430271074) 从干净的 `a5086e5cd85ba26da9cf85b168195e27edaa89b0` 启动；三个平台均在 `dotnet restore --locked-mode` 报 NU1004，测试与发布没有执行，原公开附件未改变。

`OpticalSystemDesign.InitialStructure.Persistence.csproj` 已增加 Engine 引用，但其锁文件及五个传递依赖项目仍缺少该引用。重新求值后共六份锁文件改变，第三方非 Project 项的版本、依赖和内容哈希逐项保持相同，机器核对见 [lockfile-repair.json](lockfile-repair.json)。未改产品/测试算法或云门禁。

本地后台复验：

```powershell
dotnet restore OpticalSystemDesign.slnx --force-evaluate
dotnet restore OpticalSystemDesign.slnx --locked-mode
dotnet build OpticalSystemDesign.slnx -c Release --no-restore /m:1 /nr:false -p:ContinuousIntegrationBuild=true
```

锁定还原通过；完整默认 Release 解决方案 19 项目编译成功，0 警告、0 错误。此阶段未运行 .NET 测试，不能记为任何测试用例通过或云端交付完成。后续修复提交将重新触发相同发布流程，最终运行、附件和 OTA 验签结果另行追加，原失败保留。

全程不启动产品图形界面，不操作桌面输入或剪贴板，不安装/替换用户本机软件。云发布操作使用现有 GitHub 认证，凭据只在内存用于认证，不写入日志或仓库。
