# GitHub 云端构建与发布

源码、依赖锁文件、Windows 安装器及全部打包脚本均随仓库维护，不需要维护者电脑持续开机。入口是 [Windows EXE build and publish](https://github.com/VendettaMask/opticalsystem/actions/workflows/release.yml)。这是生成完整安装包的流程，不是软件自动安装更新功能。

## 一键更新当前测试包

1. 将要交付的代码同步到 GitHub `main`，在 Actions 打开上述工作流，点击 **Run workflow**。
2. 选择 `main`，`release_tag` 保持 `v1.0.4-test.20261019`，架构选择 `win-x64`。
3. 勾选 `replace_existing`，确认运行。需要仓库写权限；不勾选时不会覆盖已有附件。
4. 等所有步骤成功，再从原测试发布页下载 EXE。失败记录在 Actions 查看，不把失败的流程当成新版交付。

手动刷新旧发布不会移动标签。包内与发布说明中的源码提交才是本包实际来源；该标签的自动 Source code 压缩包仍是标签原始代码。操作入口和权限说明见 [GitHub 手动运行文档](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)。

## 发布新版本

先修改 `ProductVersion.props`，更新 `packaging/windows/RELEASE_NOTES.md` 并完成检查，再推送代码和匹配的版本标签。`v1.0.5` 会构建两个架构并自动公开为正式版；`v1.0.5-test.20261022` 会公开为测试预发布、不标 Latest。标签和产品版本不一致直接失败。推送普通分支只运行通常的 CI，不发布安装包。

首次公开前创建草稿暂存已验证的 EXE，所有所选架构通过后才公开。刷新已有发布时先保存旧附件副本，再以临时名称上传完整新包并核对服务端 SHA-256，随后切换下载名并移除被替换的旧附件。GitHub 没有多附件原子事务；短暂的临时名和切换失败仍可能出现，错误时会尝试恢复旧名称，旧文件和备份不提前删除。发现未知附件或不可变发布时停止，需要维护者检查。

## 下载区与记录

- Releases 只附加 EXE；SHA-256 放在发布正文，构建元数据、测试结果、旧附件备份放在 Actions 工作流产物内，保留 30 天。仓库可见性和 GitHub 访问权限同样适用于这些产物，不应放秘密。
- 自动生成的 Source code ZIP / tar.gz 是 GitHub 固定提供的源码入口，不是额外上传的安装文件。
- 发布用本次运行的短期 `GITHUB_TOKEN`，只在发布任务授予仓库内容写权限，不需要在代码中保存个人令牌。
- 当前包未签名。云端打包不会消除 Windows 的未知发布者提醒，也不会自动下载、安装或关闭用户软件。

## 验证范围

云端构建完整解决方案，但只运行与交付、安装、界面和原生预设相关的定向测试，不把它称为全仓库测试。冻结证据与文档校验保留，x64 包执行内置模块完整性检查，ARM64 只有交叉打包检查。AutoLens 目标预设的存在和界面通过不等于设计达标；此前 24 次固定 CPU 运行仍为 0/24 达标。

流程源码见[工作流](../.github/workflows/release.yml)与[安全替换脚本](../scripts/publish-github-exe.ps1)，实际执行和交付结果以[当前状态](CURRENT_STATUS.md)为准。
