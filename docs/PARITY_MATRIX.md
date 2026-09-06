# 产品能力与格式兼容矩阵

2026-09-06 当前分析比较覆盖 52 项数值契约，MS-L7 当前实测为 44 Pass、6 Close、2 Difference，另有 12 项点列/图像/物理定义审计；详见 [Huygens 后处理修复记录](ZEMAX_HUYGENS_REPAIR_2026-09-06.md)。契约存在不代表通过，API 能力审计不代表精度验证。此前 [十项修复记录](NUMERICAL_REPAIR_2026-09-06.md) 保留为历史证据。以上均不扩展本表的文件格式支持范围。

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
| 传播 | `Propagation` | 均匀介质和“入口方向近似”；后者仅在每段入口修正一次方向，不是 GRIN eikonal/Hamilton 求解器 |
| 镀膜/散射近似 | `Coatings`、`Scattering` | 仅提供标为 Experimental 的经验透过率起伏、主光线散射损耗和测量样本均值损耗；旧 Thin Film/Lambertian/Measured BSDF 名称只作兼容别名，稳定 S-matrix 与 BSDF 方向抽样未完成 |
| 分析 | `Analysis` | 72 个规范分析、Workbench 单一描述符目录和按模式隔离的两套入口；顺序 70 项、非序列 2 项，独立畸变入口已合并到场曲/畸变，报告菜单含 5 个真实入口 |
| 优化 | `Optimization` | 变量、操作数、缩放，以及五种按真实实现命名的本地搜索；没有实现真正的 BFGS、L-BFGS-B、COBYLA、DE/CMA-ES 或信赖域 LM，这些名称明确返回不支持而不映射到其它算法；ZMX 参考评价函数 103 行按源顺序可见，124 个 Zemax 顺序操作数已定义级可执行，覆盖 `TRAR`、`TTHI/TGTH`、`REAR/RANG`、基础数学与行约束（含 `DIVB/PROB/OSUM/QSUM/EQUA` 定义级语义）、常见厚度/边厚/曲率/圆锥/半口径、`WLEN/INDX`、`MNIN/MXIN/MNAB/MXAB`、`POWR`、若干一阶量以及 `CTGT`、`PMAG`、`PETZ`、`MXEG` 和 `GOTO/ENDX/OOFF/SKIN/SKIS/USYM`；注册表按本机 2026 R1 实测包含 383 个顺序兼容代码，`DIMX` 等未完整实现类型禁用只读保留，启用兼容行与未知代码明确失败；当前可见代码/兼容类型不等同 383 项完整数值支持，新增执行路径仍需 Zemax/ZOS-API golden 对照；Glass Expert 未实现且明确返回不支持 |
| 公差 | `Tolerancing` | 向导、验证、灵敏度、补偿和确定性 Monte Carlo |
| 多配置 | `Multiconfig` | 配置复制、激活、属性链接/解链和持久化 |
| 文件 | `Serialization`、`FileIO` | STAROPT schema 4、旧 schema 安全迁移、原生快照、ZMX 与 SEQ/LEN 子集；Zemax 只读评价函数参数往返时不裁剪 |
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

Forbes/NURBS/grid-sag 原生模型和快照广度、衍射效率、完整薄膜 TMM、矢量衍射、完整 NSC 对象/光源/探测器环境与多子光线分裂、更广商业格式、完整 GUI 自动化以及 GPU/自动微分后端仍未完成。

Python Optiland 运行后端、JSON 格式和互操作路线已移除。Optiland 0.5.8 仅作为 `validation/history` 的冻结辅助历史参考；后续外部精度验证以仓库 Zemax OpticStudio 2026 R1 基准为主，并逐项记录镜头、设置、版本、单位、采样和容差，不能扩大为通用默认值。
