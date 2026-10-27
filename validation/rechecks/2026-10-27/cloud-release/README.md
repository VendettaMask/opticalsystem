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

## 第二次云运行与界面测试准备修复

锁文件提交 `4bad6dd0cef00802f86df58ed52625a9a07f9935` 的 [40431071539](https://github.com/VendettaMask/opticalsystem/actions/runs/40431071539) 三平台还原和完整编译均通过。两个 Windows 任务在 202 项分析契约中各有相同的两项失败：`AnalysisFooterUsesOneSharedHeightAcrossRegularAndPupilLayouts` 和 `AngleVsImageHeightFooterOmitsDataWithoutChangingOtherAnalysisSummaries` 没有初始化构造矢量标志所需的 `IPlatformRenderInterface`。Mac 在镜头导航测试中异常退出，7 个已完成用例通过，剩余 2 个未完成，不能算作 9 项通过。三个任务都未进入发布。

仅修正测试准备与执行安排：两项页脚测试使用已有 `SafeHeadlessUnitTestSession` 在真实绘图线程执行，并将所属类纳入既有串行界面集合；所有原断言保持。新增导航执行脚本先发现此类全部用例，再为每个方法开启独立进程；理论用例数量来自发现结果，并核对每份 TRX 的总数、通过数、零失败/跳过和完整结束状态，不硬编码删减某个测试。此阶段只保留 `--blame` 项目测试顺序记录；后续系统报告的明确用户授权与用途见下文。

发布修复在 `4bad6dd0` 的隔离工作副本中完成，以免混入其他任务未提交的自主拓扑界面代码。后台本地结果：

- [分析 GUI 契约](analysis-gui-contracts.trx)：65/65 通过。
- [导航覆盖账目](navigation/lens-navigation-isolation.json)：发现 9 项、五个方法进程、9/9 通过，原有数据/断言/全部语言与主题保留。
- [初次定向准备检查](release-ui-fixtures.trx)：修正的两项页脚测试和云流程守卫 3/3 通过；其中两项已包含在上述 65 项中，不相加。

这些 Windows 本地结果不代表 Mac 原生检查完成。下一次云运行仍执行全部三平台门禁，实际通过、签名和下载核对结果在完成后追加。前次云失败记录保留，未改光学计算、版本、标签、数值阈值或安装器安全替换规则。

本地去重共 75 项通过，见[机器账目](local-gate-repair.json)；原云端项目测试记录及日志保存为[修复前证据](failed-run-40431071539.zip)，包含中止状态，不将 Mac 已完成的部分用例误记为完整运行通过。

## Mac 单独诊断阶段

第三次 `df7bcb26c1e19b5ef4dbf600a62b27d3d48af7f6` 的 [40433380481](https://github.com/VendettaMask/opticalsystem/actions/runs/40433380481) Mac 前三个方法进程共 6 项完成通过，第四个 `ImportedZoomSwitchesThreeRealConfigurationsAndOpensExistingEditor` 单独异常退出。现有进程序列已定位到具体方法，进程隔离没有修复这一异常；没有把部分通过当作 Mac 完整通过。

用户在获知自动审批拒绝额外上传系统诊断的原因后，明确答复“允许上传云端测试崩溃报告”。据此只允许 GitHub 临时 Mac 构建机上测试进程的有界报告进入本仓库 Actions 检查工件，不读取用户电脑，不采集进程内存转储。新增仅验证模式不打包/上传安装器/发布，保留全部所选平台验证；先用 Mac 单平台诊断，修复后仍需完整发布门禁通过。

第四次 Mac 单独验证 [40434695432](https://github.com/VendettaMask/opticalsystem/actions/runs/40434695432) 重复同一方法的原生进程异常，系统诊断目录没有生成匹配的报告；已完成 110 项通过、1 项硬件条件跳过之外，仍有中止套件，不能当作完整通过。后续改用 .NET 原生报告的 `DOTNET_EnableCrashReportOnly=1`，按 runtime 源码确认只输出 JSON 线程/调用栈、不写内存 dump；导航阶段日志只记录阶段名称和时间，不记录处方或凭据。

第五次 [40436369194](https://github.com/VendettaMask/opticalsystem/actions/runs/40436369194) 获得 `navigation.9473.crashreport.json` 及阶段记录，工件 `12704139176`；报告明确标记界面工作线程崩溃，但信号边界后没有有效原始帧。三个配置的数据、光圈和场景断言均执行到完成；末尾标记是多配置编辑器已打开，随后重激活镜头标签并绘制时退出。仅验证模式增加云端 LLDB 观察，不更改用例数量/断言或生成内存转储。新增两项本地预检查通过，原始本机 TRX 仅保留本机，不新增上传。
