# 构建与发布

## 文档同步规则

每项已完成代码修改必须在同一任务中更新相关文档。文档必须区分已实现、计划和仅兼容行为。测试数量或验证日期变化时，所有引用该基线的文档必须同步；代码、测试、文档和最终报告必须一致。

## 本地构建

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet restore OptilandWorkbench.slnx --locked-mode
AVALONIA_TELEMETRY_OPTOUT=1 dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
```

仓库通过 `Directory.Build.targets` 关闭 Avalonia BuildServices 的构建统计目标；这不是编译能力的一部分，关闭后受限本地环境不需要写入用户 AppData/Home 目录即可构建 App 和测试程序集。

有意修改依赖时再更新锁文件：

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet restore OptilandWorkbench.slnx --force-evaluate
```

## 测试

```bash
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build /m:1 /nr:false
```

VSTest 会打开本地套接字；受限沙箱可能需要额外权限。普通修改优先运行相关定向子集，只有跨模块、高风险或发布验证才要求全量测试。

CI 中正式产品与 Initial Structure Lab 测试均启用 hang 诊断：测试进程长时间无响应时会产生日志/转储线索，而不是无限等待或让后续结果失真。主测试 hang 阈值为 `12m`，Initial Structure Lab 为 `8m`；这个阈值覆盖已知 4 分钟级长测试在较慢 CI 机器上的波动，不把接近完成的慢测试误判成挂死。

## 换行符

仓库文本默认使用 LF；Windows 批处理脚本保留 CRLF。`.editorconfig` 和 `.gitattributes` 必须保持一致。Windows 开发者建议使用仓库属性控制换行，而不是依赖全局 `core.autocrlf=true`。

## 性能基准

```bash
dotnet run -c Release --project tools/OptilandWorkbench.Benchmarks/OptilandWorkbench.Benchmarks.csproj
dotnet run -c Release --project tools/OptilandWorkbench.Benchmarks/OptilandWorkbench.Benchmarks.csproj -- --non-sequential 1000000
```

第一条基准覆盖 10,000 和 100,000 条顺序光线、20 个表面、不同历史保留模式、PSF/MTF 采样和 Monte Carlo。第二条是独立非序列百万光线 STARRDB 流式写入基准，记录吞吐、托管堆、峰值工作集和数据库大小。输出均为 CSV；性能结果用于同机同运行时比较，不是普通 CI 硬阈值。

## 非序列教学样例

```bash
dotnet run --project tools/OptilandWorkbench.NonSequentialSamples/OptilandWorkbench.NonSequentialSamples.csproj -- samples/non-sequential
```

生成器使用固定对象GUID和随机种子，逐个追迹验证场景、能量平衡及建议路径筛选，再分别原子写入12个STAROPT工程、6张光源效果SVG和`index.json`。样例清单、课堂步骤与预期结果见[`samples/non-sequential/README.md`](../samples/non-sequential/README.md)。

## 启动桌面应用

- Windows：`Run-Optiland.cmd`
- macOS：`Run-Optiland.command`

脚本依次执行 `dotnet restore`、`dotnet clean`、`dotnet build --no-restore` 和 `dotnet run --no-build`。还原放在清理之前，因为 `dotnet clean` 同样会解析现有 NuGet 资产文件；全局包缓存缺失时，这个顺序会先补齐依赖，避免清理阶段触发 `NETSDK1064`。清理只涉及项目构建输出，不删除 `%APPDATA%/OptilandWorkbench` 或 macOS 对应用户目录中的工程、主题和会话数据。

终端等价命令：

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet run --project src/OptilandWorkbench.App/OptilandWorkbench.App.csproj
```

## 发布目标

一次发布主要平台：

```bash
bash scripts/publish-cross-platform.sh
```

自包含发布：

```bash
SELF_CONTAINED=true bash scripts/publish-cross-platform.sh
```

手工命令示例：

```bash
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r osx-arm64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r osx-x64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r win-x64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r win-arm64 --self-contained false
```

输出位于：

```text
src/OptilandWorkbench.App/bin/Release/net10.0/<runtime>/publish
```

macOS 脚本还会生成 `Optical System Design.app`，声明 `.staropt` 文档类型并转发 Finder 打开的工程路径。

## 当前验证基线

截至 2026-09-02：

- 2026-08-28变更前历史基线为正式产品严格构建 `0` 警告、`0` 错误、全量回归 `837/837`；该数量已经由下文 2026-09-02 的 `1000/1000` 完整基线取代，不作为当前结果；
- 2026-08-29可靠性加固前，相关桌面可访问性、分析 GUI、镜头编辑器和 Dock 契约子集记录为 `95/95`；本轮修改后未把该旧数量沿用为当前结果；
- 2026-08-29可靠性加固前，非序列定向记录为 `80/80`、独立智能初始结构实验室定向记录为 `9/9`；本轮新增数据库、资源预算和路径表达式回归后，这两个数字只作为历史记录，不代表当前测试总数；
- 最近一次架构、可访问性和响应式布局定向结果为 `14/14`；
- 2 项新增 Avalonia 首帧/主题回归通过相关 16 项定向子集；
- 4 项新增 Dock 空宿主、会话和锁定回归通过 `12/12` 窗口布局子集；
- 配对 fan 布局契约覆盖光线像差图和瞳孔像差图：同一视场的 `P_y/P_x` 位于同一卡片，卡片按视场数平衡排列，结果区不再暴露手动方形开关；
- 方形单图契约覆盖全视场点列图、光迹图和干涉图：X/Y 数据等比例，外层视口默认保持正方形；色焦移保留普通曲线图的自适应布局；
- 平铺/层叠修复复用了现有测试，验证浮动页自动回收到主文档区并进入内部 MDI；合并命令验证回收后恢复标签模式；
- 报告菜单、72 项 Core 分析目录、顺序 70 项/非序列 2 项模式隔离、三类真实报告输出以及独立“畸变”入口退场由对应定向契约测试覆盖；
- 最近一次相关目录子集为 `3/3`，同时覆盖旧 `Distortion`/“畸变”名称迁移、组合页畸变曲线、干涉图方形视口和色焦移非方形契约；
- Zemax 评价函数导入子集为 `6/6`，覆盖实际 `[MS-L7]` 的 103 行源顺序、`TRAR` 参数映射、只读兼容槽位、快照校验和编辑往返不裁剪；
- 最近 App 项目构建结果为 0 警告、0 错误。
- 2026-08-29可靠性加固后，正式解决方案（含 Core、Application、App、测试程序集和工具项目）与独立实验室解决方案均在默认输出目录构建为 `0` 警告、`0` 错误；此前正式产品组合子集为 `14/14`、实验室不可变发布子集为 `3/3`。本轮最终新增/高风险正式产品筛选为 `13/13`，实验室持久化与规格边界筛选为 `4/4`，限定静态分析规则无残余诊断。只运行相关定向测试，不运行正式全量测试，详细边界见[可靠性与资源边界加固](RELIABILITY_HARDENING_2026-08-29.md)。
- 同轮正式产品分别通过可靠性主子集 `11/11`、非序列光源采样 `3/3`、分析 GUI 参数与轴元数据 `3/3`、限额读取/响应式守护/STAROPT/会话 `5/5`、可访问性与响应式布局 `5/5`；这些是独立筛选结果，不是新的全量基线。
- 实验室资源预算最初单项为 `1/1`，预算与持久化往返组合子集为 `2/2`，唯一运行 ID、不可变目录发布和取消清理子集为 `3/3`，本轮防篡改/大小写冲突/发布/规格边界筛选为 `4/4`；筛选重叠，不累计为全量数量。
- 2026-08-29 对正式解决方案执行 NuGet.org 直接与传递依赖漏洞查询，当前未报告已知易受攻击包；后续发布仍应重新查询。
- 2026-08-29 能力真实性修复后，评价函数/优化器目录、ZMX 只读行、快照校验和应用优化入口定向子集通过 `8/8`；未运行正式全量测试。
- CI 现将正式解决方案与独立 Initial Structure Lab 的构建、三平台测试拆成独立 job：主测试失败不会跳过 Lab 测试，Lab 构建成功也不再被解释为 Lab 测试已运行。两套测试分别输出 TRX 并以独立 artifact 上传；仓库质量任务分别验证两套解决方案格式。固定种子格式模糊回归覆盖 STAROPT、STARMESH、STARRDB 和 ZMX，当前定向结果 `6/6`。
- STEP CAD CI 分为“生成 fixture”和“FreeCAD/OpenCascade 第三方导入验证”两个 job。生成出的 STEP fixture 始终上传；验证 job 固定在 `ubuntu-22.04`，安装固定版本 `FreeCAD 0.19.2+dfsg1-3ubuntu1`，通过 GitHub Actions 缓存复用固定 FreeCAD `.deb` 包，并上传 apt、FreeCAD 版本、`.deb` SHA-256 和 OpenCascade 验证日志。若验证环境安装失败，红灯表示第三方验证环境未建立，不等同于 STEP 输出已被证明错误。完全脱离 apt 镜像变化仍需要后续维护预构建容器或随仓库托管的校验运行时。
- 独立 CI 性能烟测覆盖 10,000 条顺序末面追迹、2,000 条几何 MTF、20 次公差 Monte Carlo 和 10,000 条非序列 STARRDB；本机当前约 `0.8 s`、累计分配约 `705 MiB`，闸门上限为 `2 min`、`2 GiB` 和 `128 MiB` 数据库。该宽松闸门用于捕获数量级退化，不替代正式性能基准。
- CI 在提交、拉取请求、手动运行和每周计划任务中执行直接与传递 NuGet 漏洞查询；在线查询结果具有时效性。
- 2026-08-30最终收口重新构建正式产品和独立实验室，均为`0`警告、`0`错误；两套`dotnet format --verify-no-changes`、启动/发布脚本语法和`git diff --check`通过。评价函数/优化器、当时的333代码注册、公差逆向/元件/非球面、非序列探测器及其类型化物理轴、兼容程序集、Application/App分层、可访问性、响应式布局和主题的高风险组合筛选为`56/56`；受限文件与固定种子格式模糊组合为`14/14`；实验室冻结基准、密集验收、断点续算、STAROPT导出和响应式源码关键路径为`5/5`。这些仍是历史定向结果。
- 同日通过 NuGet.org 重新查询正式解决方案和独立实验室的直接与传递依赖，所有项目均未报告已知易受攻击包；性能烟测在本机约`0.79 s`完成，累计分配约`705.5 MB`，10,000射线STARRDB为`877,339 bytes`，均在CI宽松闸门内。
- 2026-08-31 后续高风险加固覆盖非序列材料快照、文档图验证复杂度、不可变网格资产、STL 流式/可取消导入与预计工作集预算，以及优化框架严格有限数/维度校验。正式产品完整主测试 `935/935` 通过，独立 Initial Structure Lab 测试 `21/21` 通过。
- 2026-08-31 文档切换事务、优化迭代/变量回滚、非序列结果代次/探测器重建和运行时字号刷新修复后，正式解决方案默认输出构建为 `0` 警告、`0` 错误；文档与保存 `22/22`、优化相关 `23/23`、非序列文档/追迹/杂散光 `88/88`、主题运行时 `6/6`、分层架构 `15/15` 定向筛选通过。筛选存在交集，不能相加；按用户要求未运行完整测试，不替代上条 `935/935` 完整基线。
- 2026-09-01 设置保存事务、非序列锁外观察者通知与大页码分页、相位/扩展光源输入边界、自绘只读图辅助功能、窄视口约束和 Initial Structure Lab 算法版本 2 修复后，正式解决方案和独立实验室解决方案默认输出构建均为 `0` 警告、`0` 错误。正式产品相关定向测试 `7/7`、实验室定向测试 `4/4` 通过；按用户要求未运行完整测试，不替代 2026-08-31 的正式产品 `935/935` 和实验室 `21/21` 完整基线。
- 2026-09-01 修复 CI 的 STEP 验证 job 在 job 级环境变量中使用不可用 `runner` 上下文的问题；日志目录改用该阶段允许的 `github.workspace`，不改变 STEP 生成、固定 FreeCAD 版本或 OpenCascade 验证门槛。GitHub 上连续零秒失败的运行均未创建任何 job，属于工作流解析失败，不代表这些提交已执行或未通过代码测试。修复后的工作流通过 YAML 解析、`actionlint 1.7.12` 和 shell 脚本语法校验。
- 同日工作流恢复执行后，Linux 主测试实际运行 `972` 项并发现 2 个非序列教学清单仍使用分裂父分支重复发布时期的旧计数。Fresnel 与全反射光管的确定性分支基准分别更新为 `1200` 和 `1195`，12 个教学工程重新载入/追迹子集 `13/13` 通过；STAROPT 工程内容和能量基准未改变。
- 2026-09-02 常见 Zemax 顺序评价函数操作数束扩展、实机目录校准、描述符驱动编辑往返和 `[MS-L7]` MFE golden 接入后，正式主测试 `1000/1000` 通过，解决方案构建 `0` 错误；构建仅有 NuGet 漏洞数据源 SSL 警告。目录为 383 个顺序兼容代码、108 个已连接计算引擎的代码；103 行源哈希/顺序和 400 余个活动参数槽已锁定，10 个代表行通过数值对照并修正 `PETZ` 符号；完整 Zemax 等价仍需继续收敛。

完整发布前仍应重新运行锁定还原、解决方案构建和全量测试，不得把定向验证表述成新的全量基线。

Python 基准夹具只在有意更新固定的 `optiland==0.5.8` 契约时重新生成；生成后必须审核差异并运行全量测试。
