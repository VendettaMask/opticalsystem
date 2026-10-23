# GitHub 云端构建与发布

源码、依赖锁文件、Windows 安装器及全部打包脚本均随仓库维护，不需要维护者电脑持续开机。入口是 [Windows EXE build and publish](https://github.com/VendettaMask/opticalsystem/actions/workflows/release.yml)。这是生成完整安装包的流程，不是软件自动安装更新功能。

## 一键更新当前测试包

1. 将要交付的代码同步到 GitHub `main`，在 Actions 打开上述工作流，点击 **Run workflow**。
2. 选择 `main`，`release_tag` 保持 `v1.0.4-test.20261019`，架构选择 `win-x64`。
3. 勾选 `replace_existing`，确认运行。需要仓库写权限；不勾选时不会覆盖已有附件。
4. 等所有步骤成功，再从原测试发布页下载 EXE。失败记录在 Actions 查看，不把失败的流程当成新版交付。

手动刷新旧发布不会移动标签。包内与发布说明中的源码提交才是本包实际来源；该标签的自动 Source code 压缩包仍是标签原始代码。操作入口和权限说明见 [GitHub 手动运行文档](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)。

手动流程只重建所选架构，已有其他架构的 EXE 会保留，不表示它也来自本轮源码；要求两个架构统一来源时，应使用新版本标签触发双架构构建。

## 发布新版本

先修改 `ProductVersion.props`，更新 `packaging/windows/RELEASE_NOTES.md` 并完成检查，再推送代码和匹配的版本标签。`v1.0.5` 会构建两个架构并自动公开为正式版；`v1.0.5-test.20261022` 会公开为测试预发布、不标 Latest。标签和产品版本不一致直接失败。推送普通分支只运行通常的 CI，不发布安装包。

首次公开前创建草稿暂存已验证的 EXE，所有所选架构通过后才公开。刷新已有发布时先保存旧附件副本，再以临时名称上传完整新包并核对服务端 SHA-256，随后切换下载名并移除被替换的旧附件。GitHub 没有多附件原子事务；短暂的临时名和切换失败仍可能出现，错误时会尝试恢复旧名称，旧文件和备份不提前删除。发现未知附件或不可变发布时停止，需要维护者检查。

## 下载区与记录

### OTA 签名与纯云端密钥备份

正式版与测试版在修改 Release 前都必须验证 RSA-4096 签名密钥与产品内置公钥相同，签名版本、架构、完整 EXE 大小/SHA-256、源码提交和载荷发布 ID；签名元数据放在发布正文的隐藏标记，不增加 Release 附件。正式版保留兼容的 v1 信封，测试版使用 v2，额外签名云端 `GITHUB_RUN_ID` 构建编号；该编号也写入主程序集及打包记录，发布时必须与本次工作流一致。测试预发布仍不标 Latest、不伪装正式版，但新版客户端默认“包含测试版”，可按更高版本或同版本更高构建编号 OTA；未勾选时仍只检查正式版。重跑同一个 Actions run 不产生更高编号，要交付新的测试构建应重新 Run workflow。此签名不是 Windows Authenticode，不能消除未知发布者提醒。

正常签名只读取主仓库的 `OPTICAL_OTA_SIGNING_KEY` Actions Secret。经用户批准建立的私有仓库 `VendettaMask/opticalsystem-ota-vault` 提供独立 Backup/Verify 工作流：恢复密码仅通过该私有仓库的 `OPTICAL_OTA_RECOVERY_PASSWORD` Secret 输入，不使用聊天、普通变量或可见的工作流输入。Backup 生成 AES-256-GCM、PBKDF2-HMAC-SHA256（600000 次）的加密文件并提交到私有仓库，再下载并解密核验；Verify 不读取原始签名 Secret，仅用已提交的加密文件和密码恢复并验证同一签名身份。备份不绑定电脑或 Windows 账户，也不依赖会过期的 Actions 产物。只需维护者保管一个独立强密码；软件用户不需要它。

若主签名 Secret 丢失，可从私有仓库网页复制完整加密 JSON 到主仓库的 `OPTICAL_OTA_ENCRYPTED_BACKUP` Secret，再将同一个密码填入主仓库 `OPTICAL_OTA_RECOVERY_PASSWORD` Secret。发布脚本在原始签名 Secret 缺失时从这两个 Secret 在内存中解密，仍必须匹配同一公钥；原 Secret 存在但损坏时直接拒绝，不静默使用备用路径。不需要个人访问令牌、旧电脑或把明文私钥取出。密码修改不是自动重加密；已有备份密码不匹配时停止并保留原文件。全部基于 GitHub 不代表跨平台灾备：失去账户/私有仓库访问权限时，仅恢复密码不足以找回文件。

签名基础设施、客户端交付与真实安装分别记账；旧稳定通道客户端不会因云端规则改变就认识测试包，须先手动安装新引导包。客户端、真实安装和主仓库恢复发布的实际状态查看[当前状态](CURRENT_STATUS.md)。私有仓库操作说明的公开模板见[维护说明](../packaging/ota-vault/README.md)，早期备份验证见[云端密钥账目](validation/OTA_CLOUD_KEY_VAULT_2026-10-22.json)。

- Releases 只附加 EXE；SHA-256 放在发布正文，构建元数据、测试结果、旧附件备份放在 Actions 工作流产物内，保留 30 天。仓库可见性和 GitHub 访问权限同样适用于这些产物，不应放秘密。
- 自动生成的 Source code ZIP / tar.gz 是 GitHub 固定提供的源码入口，不是额外上传的安装文件。
- 发布用本次运行的短期 `GITHUB_TOKEN`，只在发布任务授予仓库内容写权限，不需要在代码中保存个人令牌。
- 当前包未作 Windows Authenticode 发布者签名；OTA 内置公钥验签不替代 Windows 证书。云端打包不会消除未知发布者提醒，也不会自动下载、安装或关闭用户软件。

## 验证范围

云端构建完整解决方案，但只运行与交付、安装、界面和原生预设相关的定向测试，不把它称为全仓库测试。冻结证据与文档校验保留，x64 包执行内置模块完整性检查，ARM64 只有交叉打包检查。AutoLens 目标预设的存在和界面通过不等于设计达标；此前 24 次固定 CPU 运行仍为 0/24 达标。

流程源码见[工作流](../.github/workflows/release.yml)与[安全替换脚本](../scripts/publish-github-exe.ps1)，实际执行和交付结果以[当前状态](CURRENT_STATUS.md)为准。

2026-10-22 已[实际跑通 x64 手动触发后的全自动构建与发布](https://github.com/VendettaMask/opticalsystem/actions/runs/39715666744)，没有操作用户桌面或本机安装。定向测试 97/97 和 23/23、包检查 14/14；首次发布的列表解析失败及其修复也保存在[交付账目](validation/GITHUB_CLOUD_RELEASE_2026-10-22.json)。本次没有实际触发新版本标签、执行 ARM64 包或重新认证完整光学矩阵。
