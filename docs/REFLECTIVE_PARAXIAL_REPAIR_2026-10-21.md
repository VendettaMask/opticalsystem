# 反射系统一阶参数与非球面顶点曲率修复

日期：2026-10-21。承接全目录基线及广角/Schmidt 的 29 项修复，继续处理剩余差异；本轮不是全目录重新认证。

## 已修复结果

9 支镜头的 **11 个原 Difference 请求**恢复 Pass，使用原配置、主波长、请求及容差。本机许可有效的 Zemax OpticStudio 2026 R1 重新采集全部 11 项，失败为 0；每项 EFL、F 数共 22 个标量与原参考逐位相同。没有修改参考数组、镜头源文件、采样、瞄准限或比较策略。

| 镜头 | 修复请求数 | 结果 |
|---|---:|---|
| Mangin mirror | 1 | 一阶参数 Pass |
| Double-pass null test | 1 | 一阶参数 Pass，EFL 与 F 数误差均为 0 |
| Offner reflecting compensator | 1 | 一阶参数 Pass |
| Offner refracting compensator | 1 | 一阶参数 Pass |
| Flat-field Schmidt | 1 | 一阶参数 Pass；此前原 32 档能量保护通过 |
| Schalcktelescope 10 | 1 | 一阶参数 Pass |
| Offner relay | 1 | 一阶参数 Pass |
| Prism using total internal reflection | 1 | 一阶参数 Pass |
| Scanning mirror | 3 | 配置 1、2、3 的一阶参数 Pass |

表格表示修复的分析请求，不表示这些镜头的全部其他分析都已通过。

## 共享 Core 修正

近轴矩阵的斜率采用原始轴坐标。过去统一用 `-1/C` 报告焦距，漏掉奇数次反射后的传播方向；现由矩阵行列式中的反射奇偶性转换为沿出射传播方向的有符号焦距，保持发散系统负焦距。光焦度、分段焦距及基点报告使用一致的约定；基点的轴向位置仍在原坐标中，主点与节点评估保留折射率比。

偶次非球面的矢高为 `c*r²/2 + A2*r² + O(r⁴)`，其近轴曲率是 `c + 2*A2`。过去只读取基底半径，遗漏 Double-pass null test 中平面基底的二次项。矩阵与近轴光线追迹现在共用这一顶点曲率；高次项不进入一阶曲率。

理想反射薄透镜的矩阵和通用近轴追迹原来使用 `-u-y/f`，与现有薄透镜交互模型 `-u+y/(n*f)` 相反。本轮校正并直接测试两条路径的一致性。原 `EntrancePupilUsesTheSameMatrixRulesForReflectiveThinLenses` 的 `-12.5` 是旧错误矩阵的结果；按已有交互模型，`f=50,t=10` 时 `B/A=-10/1.2`，用该独立解析值替换。没有放宽测试或外部比较容差。

所有改动位于共享纯 C# Core。没有镜头名称分支、原生参考查表或应用模块中的替代光学计算。

## 定向验证

- 原有 **1131/1131** 个通过请求保持：全目录基线 1102 项及已修复的 29 项。新增 11 项与它们不重叠，合计 **1142/1142 Pass**；1142 个完整请求逐项相同，策略 SHA-256 保持 `30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`。
- Core 关联测试 **104/104**；新增/加强解析守卫补跑 **17/17**，两次有重叠，按完整测试名合计 106 个不同用例。覆盖凹凸镜、偶数反射、反射薄透镜、浸没主点/节点、非球面二次项及曲率相消。
- 原生比较定向守卫单次 **64/64**：本轮 12 项、广角/Schmidt 32 项、原 32 档 12 项、PSF/128 档 6 项、Petzval 2 项。包括 Apochromat、200°/210°、Schmidt 原 32 档和 Gregorian。
- 默认 Debug、Release 根构建各 19 项目，0 警告、0 错误；未跑全测试。2668 项旧冻结资产的工作区及 Git 内容保持。
- 执行本轮数值回放及测试的 Core SHA-256 为 `ce0285d57f9253c5cb17229b2a7c22102ed6cf3e4dfdb2e78798af45a63b73d9`。打包后的程序集另按实际文件验证，不能拿这一构建身份代替安装包身份。

一阶隔离回放为 107 项：修复前 47 Pass、16 Difference、44 Error；修复后 58 Pass、5 Difference、44 Error。44 项模型能力/计算门槛没有转成通过。Double-pass 全部 22 个请求另留隔离回放，它原有的其他错误、不可比、MTF/PSF 差异仍保留。

## 剩余问题与边界

本轮未重跑 3024 项矩阵或 251 组逐面光线，不把新增通过数冒充新的全目录认证。剩余一阶差异包括 vacuum cell 的环境/压力处理、Parabolas 与 Yolo 的折转系统，以及 Optimization demo 1、Etalon 的无焦数值契约。球透镜互连的点列图权重隔离表明，简单取消高斯权重或将其系数减半都不能精确复现原参考；该猜测未进入产品。其他分析差异、主光线/瞄准错误、未支持模型能力及准备阻断继续按原基线保留。Cooke/Tessar 的独立残差调查仍遵守既有排除范围。

## 证据

- [机器账目](validation/REFLECTIVE_PARAXIAL_REPAIR_2026-10-21.json)。
- [11 项可移植原请求与原生夹具](../validation/zemax/2026-r1/reflective-paraxial-repair-2026-10-21/cases.json)，带完整字节清单。
- [新增增量档案](../validation/repairs/2026-10-21/reflective-paraxial-repair-evidence.zip)：5744715 字节，2223 个载荷逐项读回，SHA-256 `08649ae1ffc7dc7438cca914d62e7cf6d59c715d025997455a7c0bc50a55e445`。
- 本轮本地原始工作目录 `artifacts/validation/remaining-lenses-20261021`。档案包含全部 1142 项比较/请求记录、原生重采集、修复项数据、测试及失败假设；庞大的已保护项软件数组保留在本地回放，未重复封装。原始全目录参考继续由既有 [全目录档案](../validation/repairs/2026-10-21/sequential-corpus-full-evidence.zip)提供。

源码已同步，原本地包目录和原测试发布的三个同名附件均已覆盖。新包身份 `1.0.4-win-x64-20f20ca93130`，来自干净代码提交 `b37d1b31c9ca6e2b861c776af4d2dc7e943f1d94`；实际包内程序集 64/64、完整性/内置模块 14/14、公开下载 4/4，通过后保留完整前包备份。详见[交付账目](validation/UPDATE_SYNC_REFLECTIVE_PARAXIAL_PACKAGE_2026-10-21.json)。
