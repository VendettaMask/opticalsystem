# 产品能力与格式兼容矩阵

更新：2026-10-14。当前构建、回归与已知失败统一见 [状态页](CURRENT_STATUS.md)。历史 MS-L7 的 44 Pass / 6 Close / 2 Difference 是 2026-09-06 特定阶段，不是当前数值总览；后续问题状态见 [光学问题清单](OPEN_ISSUES_2026-10-08.md)。API 比较契约存在不等于精度通过，也不扩展格式支持范围。

本表跟踪纯 C#/.NET 产品的已实现能力及明确支持的格式。“已实现”只表示列出的边界，不代表完整等价。

| 领域 | .NET 模块 | 当前状态 |
| --- | --- | --- |
| 数值后端 | `Backend` | 标量 `INumericBackend`、批量接口、ManagedCpuBackend 的 CPU/SIMD 路径和注册表 |
| 光学容器 | `Optic` | 孔径、视场、波长、表面、材料、追迹、分析、优化、公差和多配置入口 |
| 表面组合 | `Domain` 等 | 几何、前后材料、镀膜、交互、物理孔径、散射和坐标系组合 |
| 光线生成 | `Raytrace` | 角度/物高/近轴像高/实像高、渐晕、远心、常用光瞳采样和七种变迹 |
| 顺序追迹 | `SequentialRayTracer` | 局部交点、孔径裁剪、折射/反射/衍射/全反射、镀膜/散射钩子 |
| 非序列追迹 | `NonSequentialDocument`、`INonSequentialDocumentService`、`NonSequentialDocumentTracer` | 与顺序处方并存的独立文档、类型化光源/实体/探测器编辑器、GUID 参考/包含链、STAROPT v2、BVH 最近命中、实体介质、Fresnel 反射/透射光线树、像素功率与能量平衡；CAD/布尔、散射、偏振/相干、非序列优化/公差和 Zemax NSC 导入未完成 |
| 几何 | `Geometries` | 平面、标准面、光栅、非球面、双锥面、环曲面、多项式、Chebyshev、Zernike、Forbes Q；其余自由曲面有明确占位边界 |
| 材料 | `Materials` | Air、Vacuum、常数、Cauchy、Sellmeier、Abbe、目录 n/k 及厂商消歧 |
| 传播 | `Propagation` | 均匀介质、旧入口方向近似及 Gradient 1～5 连续积分；正式顺序显式光线、限定共轴连续一阶与瞄准、桌面材料编辑和受支持系数变量已接入。完整分析/偏振覆盖及原生 GRIN 映射仍不完整，见 [编辑与限制](GRIN_MATERIAL_EDITOR_2026-10-04.md) |
| 镀膜/散射近似 | `Coatings`、`Scattering` | 仅提供标为 Experimental 的经验透过率起伏、主光线散射损耗和测量样本均值损耗；旧 Thin Film/Lambertian/Measured BSDF 名称只作兼容别名，这些旧近似不承担真实膜系计算；物理膜层追迹见下一行，BSDF 方向抽样未完成 |
| 真实薄膜与内置镀膜模块 | `CoherentThinFilmSolver`、`ThinFilmSpectrum`、`labs/CoatingDesign` | 同进程必装模块；稳定相干散射递推、真实 n/k/斜入射/S/P/R/T/A、减反/高反/1–5 腔模板、有限结构搜索、公差及独立实验保存。物理膜层模型已接通正式顺序追迹；实验结果显式应用到当前镜头的 UI、任意目标解析多腔综合、各向异性和非相干背面仍未实现。见[计算契约](COATING_DESIGN_LAB.md)与[当前交付](BUILT_IN_MODULES_2026-10-13.md) |
| 分析 | `Analysis` | Core 规范目录、Workbench 单一描述符和按顺序/非序列模式隔离的两套入口；独立畸变入口已合并到场曲/畸变，旧离焦 MTF 名称统一到 Fourier 入口，报告菜单含 5 个真实入口；目录以 `AnalysisCatalog.Names` 为准 |
| 优化 | `Optimization` | 变量、操作数、缩放和五种按真实实现命名的本地搜索；未实现的 BFGS、L-BFGS-B、COBYLA、DE/CMA-ES、信赖域 LM 和 Glass Expert 明确返回不支持，不映射为别的算法。ZMX 评价函数可保留受支持的行与原始参数；执行数量、模式、原生列和数值认证缺口只在 [操作数规范](ZEMAX_OPERAND_SUPPORT.md) 维护，兼容保留不等于完整数值支持 |
| 公差 | `Tolerancing` | 向导、验证、灵敏度、有界补偿、指定频率 FFT/几何 MTF 与逐视场良率、确定性 Monte Carlo；保存兼容与原生数值缺口见 [公差指南](TOLERANCING.md) |
| 多配置 | `Multiconfig` | 配置复制、激活、属性链接/解链和持久化 |
| 文件 | `Serialization`、`FileIO` | STAROPT 完整工程和受支持旧版本有界迁移；版本以 [格式契约](STAROPT_FILE_FORMAT.md) 为准。ZMX 与 SEQ/LEN 是交换子集；只读评价函数参数往返时不裁剪，不支持内容禁止静默降级 |
| 插件 | `Plugins` | 程序集/目录发现，几何、材料、分析注册和失败隔离 |
| 可视化 | `Visualization` | 二维/三维、显式光段方向和交互类型、主题资源 |
| GUI | `OptilandWorkbench.App` | 中文 Avalonia、Dock 分栏/浮动/平铺/层叠、命令面板、四个具体主题和按文件会话 |

## 当前里程碑

- STAROPT 无损保存丰富表面组件和全部配置，并在替换活动状态前完成校验和临时构建；
- ZMX 覆盖已实现的顺序系统和 Workbench 扩展子集；
- 分析结果提供指标、图形、数据和文本导出；
- 公差 GUI 提供 TDE 风格编辑和 CPU 计算；
- 二维/三维查看器使用真实表面弧矢和顺序追迹历史；
- Dock 窗口会过滤空宿主；独立浮动使用原生窗口，平铺/层叠自动回收页面并在主文档区使用内部 MDI；
- 插件失败不会阻止其他插件。

## 主要缺口

Forbes/NURBS/grid-sag 原生模型和快照广度、衍射效率、镀膜实验结果应用到镜头的 UI 桥接、非相干/各向异性扩展、矢量衍射、完整 NSC 对象/光源/探测器环境与多子光线分裂、更广商业格式、完整 GUI 自动化以及 GPU/自动微分后端仍未完成。

Python Optiland 运行后端、JSON 格式和互操作路线已移除。Optiland 0.5.8 仅作为 `validation/history` 的冻结辅助历史参考；后续外部精度验证以仓库 Zemax OpticStudio 2026 R1 基准为主，并逐项记录镜头、设置、版本、单位、采样和容差，不能扩大为通用默认值。
