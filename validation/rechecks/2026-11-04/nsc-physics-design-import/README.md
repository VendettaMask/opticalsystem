# 非序列物理、设计与导入检查

日期：2026-11-04。状态：本机主链交付，完整 Zemax NSC 兼容未完成。功能、操作、协议和支持矩阵见[当前说明](../../../../docs/NONSEQUENTIAL_PHYSICS_DESIGN_IMPORT_2026-11-04.md)。

## 当前结果

- [最终一次测试](nsc-accepted.trx)：249/249，零失败/跳过。同一轮涵盖全部名称含 NonSequential 的测试，以及 CoherentCoatingTransportTests、PolarizationOperandTests。是定向验收，不是全仓总数。
- [默认桌面构建](app-build.txt)：Release App，包含内置模块，0 警告/错误；没有安装、发布或操作用户桌面。
- [实际 ZMX 检查](actual-zmx-intake.json)：7 对象、1,081 记录，50,824 输入字节全部可还原。5 个对象基本映射，矩形体、对象属性、全局设置、设计定义及缺失依赖阻断，CanApply=false。
- 新增桌面页后台检查 Light/Dark；原 NSC→RAW/ISP 桌面链在中文/英文/日文测试中通过。折叠式表单测试先展开实际祖先分区并检查控件可见；切入 NSC 输入会展开该分区。
- 光学控制包括解析 Brewster、全反射 Jones 相位、Beer–Lambert 吸收、旋转输入、像素中心相位、跨源/波长分组、权重随机抽样、双层膜反向、复场数据库逐值重建。优化使用三种共享算法验证真实落点矩；公差保留未裁剪抽样、失败分母、补偿和总预算。

测试命令：

```text
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1 -nr:false /p:UseSharedCompilation=false '/p:OutDir=D:\optical system design\opticalsystem\local-data\nsc-validation-bin\' --filter "FullyQualifiedName~NonSequential|FullyQualifiedName~CoherentCoatingTransportTests|FullyQualifiedName~PolarizationOperandTests" --logger "trx;LogFileName=nsc-accepted.trx" --results-directory validation/rechecks/2026-11-04/nsc-physics-design-import
```

## 原始材料与外部对照边界

1. 本地 `local-data/references/ansys-nsc/sc_int1.zmx` 来源为 [固定提交的原始文件](https://github.com/yiningli/Project_X/blob/0da5949fe154939ceaefaa6c26119c04f43b85a2/Database_Files/Zemax_Data/Optical_System_Data/Zemax_Files/Short%20course/sc_int1.zmx)，Git blob `2f926017fb0c1223ececfe468c7b5a9afc23cd4f`。本地文本字节 SHA-256 `fe03019648d4d0ff4dde5cd659713fa5d58541038fd05175e74af75dba91b76d`；不将 GitHub 文本落盘身份混称网站原始二进制字节身份。
2. 本地官方 [优化样例 ZIP](https://downloads.zemax.com/zemax-portal/knowledge_articles/KA-01591/Downloads/How_to_optimize_non-sequential_optical_systems_Samples.zip) 为 214,695 字节，SHA-256 `19297cf9369572c531bb101a4674cf92f76c49ee6cee8d1169eb3a078b80be82`。包含 `04027_final system.zar`、`04027_starting point.ZAR`，均为专有归档；未当作普通 ZIP 强行解码或重构内容。
3. 已检查当前环境变量、注册路径和常用安装位置；注册文档路径没有对应可用目录，未发现可运行的 OpticStudio/API 安装。没有原生数值或全量格式认证。后续需真实明文导出、全部外部依赖与等条件独立参考。
4. 工具 `tools/OpticalSystemDesign.NscValidation` 仅做真实文本检查，不调用外部运行引擎。首次包恢复出现 NU1900（NuGet 漏洞服务不可达）；工具编译成功。产品默认构建独立记录为零警告错误。

复查实际文本：

```text
dotnet run --project tools/OpticalSystemDesign.NscValidation/OpticalSystemDesign.NscValidation.csproj -c Release --no-build --no-restore -- local-data/references/ansys-nsc/sc_int1.zmx validation/rechecks/2026-11-04/nsc-physics-design-import/actual-zmx-intake.json
```

## 开发失败保留

`nsc-design-development.trx` 保留无效高斯制造样本未捕获 InvalidDataException 的独特失败，已修复为失败样本计入分母。`nsc-regression.trx` 保留超长分支先验证字段而触发 NullReference 的缺陷，已修复；同轮 Brewster/TIR 控制误用了有真实吸收的 BK7，后改为明确无损常折射率材料隔离界面并新增独立吸收验证，没有放宽公差。`nsc-regression-recheck.trx` 保留把受限内存抽样误当完整发射数的控制失败，现检查 TotalBranchCount；文件同时记录当时尚未适配折叠分区的桌面检查。上述开发失败不能与最终结果叠加计算通过率。

重复的全通过开发记录仅在确认其全部用例被最终同轮覆盖后清理；清理与源码/二进制身份记录见 JSON 清单。只保留一份当前最终测试、唯一有用失败和原始外部材料。任何未支持的商用对象/资产仍明确阻断，不做占位或降采样通过。

最终重检时共享测试目录被另一 testhost 占用，一次构建因 MSB3027/3021 文件锁失败，未开始测试；没有停止该进程。改用上述独立输出目录完成重检。编译临时依赖副本在记录 SHA-256 后清理，保留源码、测试与产品默认输出身份，不将重复包副本作为新验证材料。
