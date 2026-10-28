# Optical System Design 云端迁移验证 — 2026-10-28

本报告记录此次云端实例中实际执行的干净重建，不引用旧 CI 通过结果作为本次证明。

源码来自 GitHub `VendettaMask/opticalsystem` 的 `main`：

- 完整源码提交：`efbd209562de92b04b9f6489a73bb425d5704087`
- 提交说明：`Update signed Mac OTA feed for 1.0.4 (1.32.1) [skip ci]`
- 产品版本：`1.0.4`。提交说明中的 Mac 更新编号不是本次云构建的发布声明。
- 远端 `HEAD` 与 `refs/heads/main` 已通过 Git HTTPS 通道核对，与本地一致。非浅克隆，295 个提交，`git fsck --full` 退出 0。
- 开始与验证完成时的源码工作区均干净；未导入任何工作站未提交修改、私人文件或本地凭据。此证据分支仅新增生成的验证报告和配置副本，不修改原有源码、测试、锁文件、冻结数据或 OTA 文件。

## 当前实例已验证

Debian GNU/Linux 13 x64；5 个逻辑 CPU，cgroup CPU 配额 4 核，内存上限 32 GiB。开始时约 25 GiB 可用磁盘，完整构建后约 18 GiB。完整原始记录见 [baseline.txt](logs/baseline.txt)。

工具链为 .NET SDK `10.0.300`、PowerShell `7.6.9`、Git `2.52.0`。SDK 严格遵守 global.json 的 latestPatch / allowPrerelease=false；官方 SDK SHA-512、PowerShell SHA-256 均校验通过，未关闭 TLS、NuGet 审计或其他完整性校验。系统已有离屏渲染所需库，Skia/HarfBuzz 的 ldd 无缺失依赖。工具详情见 [toolchain.txt](logs/toolchain.txt)。

```bash
source /workspace/opticalsystem-cloud/activate.sh
cd /workspace/opticalsystem
dotnet restore OpticalSystemDesign.slnx --locked-mode -p:ContinuousIntegrationBuild=true
dotnet build OpticalSystemDesign.slnx -c Release --no-restore /m:1 /nr:false -p:ContinuousIntegrationBuild=true
```

锁定恢复成功；安装脚本重复运行成功，恢复显示所有项目已是最新。完整解决方案包含共享 Core、App、两个必装模块、工具及四个测试项目，Release 编译 **0 警告 / 0 错误**，耗时 `00:01:39.58`。见 [构建日志](logs/build.log)、[安装日志](logs/install.log) 和 [重复安装日志](logs/install-repeat.log)。

| 本次实际执行的测试 | 通过 | 失败 | 跳过 | 证据 |
| --- | ---: | ---: | ---: | --- |
| 指定主冒烟：近轴矩阵、单光线、图纸标准切换、GB1991 离屏渲染 | 21 | 0 | 0 | [TRX](test-results/cloud-main-smoke.trx) |
| 便携 Zemax 对比：ComparisonTests、RayAuditContractTests、RmsFieldSamplingContractTests | 65 | 0 | 0 | [TRX](test-results/cloud-portable-contracts.trx) |
| 初始结构：冻结规格、平板根、共享 Core 近轴种子、Adam 梯度/恢复、周期和配置拒绝契约 | 13 | 0 | 0 | [TRX](test-results/cloud-initial-light.trx) |
| 镀膜：ThinFilmPhysicsTests、三类真实例生成/优化、保存重算/导出、确定性公差 | 14 | 0 | 0 | [TRX](test-results/cloud-coating-light.trx) |
| **产品与对比测试合计** | **113** | **0** | **0** | [机器可读汇总](summary.json) |

精确筛选表达式和命令分别保存在 `logs/*.command` 与 [test-focused.sh](setup/test-focused.sh)。选择上述轻量检查是为了覆盖依赖、共享计算、真实离屏渲染、序列化和模块基本流程；没有默认运行全镜头矩阵或长时间搜索。

按 CI frozen-evidence 原命令执行，Git HEAD 和工作区各 **2668/2668** 份冻结文件字节匹配，原 aggregate SHA-256 与 relocation-map 固定哈希均保持。见 [完整性报告](frozen/integrity.json)。检查器回归分别 **28/28** 与 **22/22**，见 [工作区检查器结果](frozen/working-tests.json)、[Git 检查器结果](frozen/git-tests.json)。没有重生成冻结参考，没有云端运行 Zemax。

首次 PowerShell 冻结检查因默认 `/home/agent/.local/share/powershell` 只读而以 70 退出，见 [原始失败日志](logs/frozen-integrity-initial-xdg-failure.log)。通过在仓库外设置标准 XDG 数据/配置/缓存目录解决，随后原检查和所有回归通过；没有修改 HOME、测试断言或冻结数据。

## 可复用配置与尚未完成的步骤

环境名称为 `opticalsystem`，开发目录 `/workspace/opticalsystem`，激活入口为 `/workspace/opticalsystem-cloud/activate.sh`。配置副本见 [安装脚本](setup/install.sh) 与 [启动说明](setup/start.md)。不需要常驻服务器；进程不应被假定跨快照保存。每个任务已有隔离环境，无需另建 worktree。

这份报告不宣称环境已经发布或从新任务复用。用户仍需在环境设置中审阅并保存配置，通过产品界面发布，然后从发布后的 `opticalsystem` 启动新任务并实际重跑就绪检查，才能确认复用。草稿保存与环境发布属于不同状态。

Git HTTPS 读取与独立分支 push dry-run 成功。GitHub API 的 CONNECT 被当前代理规则以 403 拒绝；拟添加的最小自定义允许域名为 `api.github.com`，不需要在聊天中提供 token。此项影响自动创建证据 PR，不影响本次构建和验证。实际推送、草稿保存和 PR 状态以设置会话最终报告为准。

GitHub 继续作为正式代码和版本记录来源。日常云端修改走工作分支和 PR，本地按需 pull，修改后提交回 GitHub；不自动合并 main、发布产品版本、编辑 OTA 或签名。

本次未执行：完整长测试/镜头矩阵、原生 Windows/Mac 图形界面验收、Zemax 许可运行、CUDA/GPU 执行、安装器/DMG 制作或签名、环境发布和新任务恢复验证。
