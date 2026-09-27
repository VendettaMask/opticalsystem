# 光学镀膜设计实验室

状态：2026-09-27 首版设计闭环已实现并完成定向验收。三类真实计算、独立优化、中文界面、保存重开、导出及示例可运行；高反和窄带示例没有达到全部目标。启动与实际数值见 [实验室说明](../labs/CoatingDesign/README.md)，测试范围见 [验收记录](../validation/coating/README.md)。

## 本地审计与复用

本地为 .NET 10 / C#、Avalonia 12.1.0、Dock.Avalonia 12.0.0.2。实验室沿用 `InitialStructureLabLauncher` 的独立进程边界。正式 App 只增加启动入口；实验室不接收当前 Optic，不访问主程序优化事务。正式 Core 共享材料、复数计算所需 .NET BCL、优化器、ComputationCancellation、AnalysisSeries 类型化轴、ComponentSnapshotFactory 与 BoundedFile 原子文件接口。实验室只定义目标、候选、流程及展示。

原 `Coatings/CoatingModels.cs` 是明示实验性的经验起伏模型和兼容别名，不用于本实验室；新 `CoherentThinFilmSolver` 是共享 Core 中唯一的真实膜系计算实现。`IMaterial` 已提供 n/k；`CatalogGlassMaterial` 已支持 tabulated nk 和色散，无须复制材料公式。正式优化器已有独立变量与残差接口，首版使用真实 Damped Least Squares、Nelder-Mead、Coordinate Pattern Search，不迁入 TFStudio 的优化器实现。

## 上游审计（固定版本）

- TFStudio：`8b942b3f956728a23cea2b35023c8b5d8de17428`，package 1.8.1，MIT © 2026 Andrey Achapovsky。
- 锁文件实际依赖：tmmcore **0.4.2**，MIT；发布 tarball 的完整性值记录于 `third_party/coating-reference/README.md`。源码包含 JavaScript 参考实现与 C/WASM 实现。`src/tmmcore.js` 确实重新导出该包；`utils/filter/filterDesign/spectrum.js` 调用 JS 或启用后的 WASM 内核。
- `utils/synthesis/seedGenerator/templates.js`：实际有单层、双层、QHQ 等 AR 角色模板。首版参考低/高材料模板组织。
- `utils/filter/filterDesign/prototypeLayers.js`：实际按镜层数与腔阶数生成结构，内部由基板向外计数后 reverse 为入射侧优先；本地显式使用入射侧优先。
- `utils/optimizers/de.js`：实际 DE 种群、变异、交叉和选择代码；不迁移。其它优化器与针插入/渐进演化在上游存在，本地首版不宣称支持。
- `components/windows/design/designEditor/`：真实层操作、剪贴板、厚度单元格、拖动及键盘处理。借鉴操作范围，使用 Avalonia 重建必要界面。
- `utils/materials/riiDatabase/formulas.js`：实际支持 1–5 公式，6–9 明确拒绝；不据上游介绍推断全公式支持。膜材料采用独立固定来源的 CC0 数据，仍由正式 Core 插值。

完整源归档与许可证保存在 `third_party/coating-reference`，展开目录在 `.tmp/coating-upstream`。它们不属于产品运行依赖。Node 仅用于离线生成固定上游数值参考，不引入 Electron、WASM 或 Python 产品计算。

## 计算契约

nm 为波长/物理厚度单位；角度为入射介质内相对法线的度数；层序为入射介质→膜层→半无限基板。复折射率 n+ik、时间因子 exp(-iωt)，k≥0。首版要求入射介质透明，材料各向同性、均匀、非磁性，全部有限膜层相干。T 为进入基板的功率，A 为有限膜层吸收，不包含半无限基板之后的传播吸收。

Core 使用只含衰减传播因子的向后散射递推；传输振幅在对数域累计。真实 T 与 log(T) 不按显示下限截断。OD=-log(T)/ln(10)，首版验收支持 OD≤12；超过显示范围必须标注饱和，不能把显示值作为计算值。完全全反射的 T=0 与数值下溢分别通过 log(T) 区分。精确临界角奇点当前明确拒绝，不用微扰伪装计算成功。一般接近临界角的有效点仍可计算。

材料快照保存真实公式或表格、有效波段与来源；没有 k 的目录模型明确按 k=0。薄膜示例材料数据来自 Rodríguez-de Marcos et al. (2016), DOI 10.1364/OME.6.003622，反应电子束蒸镀、300°C；不能视为所有工艺的通用膜材料常数。

## 分阶段实施结果

1. 已获取完整固定源归档、审计许可证/真实依赖，参考源码与产品源码分离。源归档哈希、材料来源及离线转换脚本可核对。
2. 已向共享 Core 增加稳定相干薄膜递推、对数传输与类型化光谱接口；120 组固定上游数据和独立解析物理检查通过。原经验兼容模型不参与实验室。
3. 已实现中文参数、可搜索材料、膜层增删/排序/复制/厚度和固定变量编辑、真实 R/T/A 曲线、后台取消与输入代次防过期覆盖。复用共享 Light 资源、密度字号和数值格式。
4. 已实现三类模板生成与独立的正式优化器调用，候选显式采用；窄带独立加密复验、逐项判定、厚度均匀扰动公差。算法版本、停止原因与评价次数来自真实运行记录。
5. 已实现版本化原子保存、保存后复算、CSV 与运行元数据导出，三例全流程重放和保存重开一致性检查通过；完成 Avalonia 界面测试及正式产品定向回归。

## 必要接口与数据隔离

新增共享接口为 `CoherentThinFilmSolver`（含相位厚度生成）和 `ThinFilmSpectrum`（采样/指标/类型化曲线）；没有在实验室复制材料色散、薄膜公式、数值优化或原子文件实现。实验室 Engine 负责模板、目标残差、候选、验收与会话；App 负责交互和调度。正式主程序仅增加 Ribbon 启动动作与 `CoatingDesignLabLauncher`，不引用实验室程序集。

实验格式与 STAROPT 分开；后台深拷贝输入并冻结材料快照，用实验 ID、代次和取消令牌防旧结果写回。参数文字变化同步使结果失效，不依赖延迟的 UI 文本事件。预览、优化、公差和最终复验均走同一 Core 薄膜求解器；采样策略不同但材料和物理设置一致。材料快照重建后核对规范字段与系数，缺失数据拒绝默认补齐。保存结果须匹配输入 SHA-256；重开使用快照复算，缓存结果不能替代物理计算。

验证必须区分上游移植一致性、解析物理正确性、Workbench 回归与 GUI 截图检查。既有 Zemax 捕获只覆盖原文件/原设置，不能充当新增镀膜验证。Optiland 历史比较不新增、不再生成。

## 首版边界

不含生产设备控制、沉积监控、向当前镜头隐式应用、粗糙散射、各向异性、非相干基板背面多反射、多腔自动综合、针插入、完整 TFStudio 文件兼容。实验文件独立，后续光线追迹集成须单独开发和验证。

## 复用清单与实际模板范围

| 分类 | 当前实现 |
| --- | --- |
| 原工程直接复用 | `IMaterial`/`CatalogGlassMaterial` n/k 色散与插值、`ComponentSnapshotFactory`、`OptimizerCatalog`、变量/残差接口、`UniformSampler`、`ComputationCancellation`、`BoundedFile`、类型化 `AnalysisSeries`、数值格式及 Light/字号资源 |
| 上游按需参考 | AR 角色模板、镜层/腔层生成思路、入射侧层序和膜层编辑操作；固定 tmmcore 用于离线数值对照 |
| 新增共享能力 | Core 的相干稳定薄膜递推、相位厚度、光谱采样、峰值和 FWHM、R/T/A/ln(T) 与类型化曲线 |
| 新增实验能力 | 独立目标/状态、结构枚举、优化编排、候选、采样验收、公差、中文 UI、版本化实验保存与导出；主程序只增加进程入口 |

AR 首版枚举单层、双层、三层及最多 12 层的交替模板，受用户层数/厚度限制裁剪；并非枚举上限内的所有膜系。HR 枚举 H/L 双层对；单腔窄带由两侧镜层和半波低折射率腔组成，至少 7 层。优化只调整当前结构中标记为变量的厚度，不自动增层、插针或更换材料。模板与局部优化不能保证任意指标有解。

## 资源和材料限制

实验目标层数为 1–200；窄带截止区 1–8 个，不能与目标半高通带重叠。预览基础间隔 40–10000，优化基础间隔 20–2000；迭代次数 1–1000，公差样本 1–200，厚度均匀扰动幅度 0–20%。实际密集复验点数可能高于基础间隔；每次共享光谱采样调用最多 200001 点。Core 单独求解上限 4096 层不能解读为界面允许 4096 层。

支持完整快照的 air、constant、cauchy、sellmeier、catalog_glass 和 polynomial_dispersion 模型；不支持的模型/缺失字段明确拒绝。目录材料按自身有效范围计算；非目录材料在实验室捕获时保守限定为 300–2500 nm，实际可用波段为参与材料范围的交集。这是当前实验室的输入策略，不是所有材料或 Core 的通用物理范围。没有外部材料文件导入/编辑界面。

保存使用 64 MiB 的共享光学文档上限，结果和公差必须匹配输入哈希。逐文件原子导出不等于整目录事务。完整文件含义、示例和误差阈值见[使用说明](../labs/CoatingDesign/README.md)及[验收记录](../validation/coating/README.md)。
