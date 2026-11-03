# 产品能力与格式兼容矩阵

核对：2026-11-02。当前构建、回归与已知失败统一见 [状态页](CURRENT_STATUS.md)。历史 MS-L7 的 44 Pass / 6 Close / 2 Difference 是 2026-09-06 特定阶段，不是当前数值总览；示例镜头后续分范围修复见 [交接清单](EXAMPLE_ACCURACY_HANDOFF_2026-11-01.md)，未关闭项见 [问题清单](OPEN_ISSUES_2026-10-08.md)。API 比较契约存在不等于精度通过，也不扩展格式支持范围。

本表跟踪纯 C#/.NET 产品的已实现能力及明确支持的格式。“已实现”只表示列出的边界，不代表完整等价。

| 领域 | .NET 模块 | 当前状态 |
| --- | --- | --- |
| 数值后端 | `Backend`、`Differentiation` | 标量/批量 CPU、SIMD、可微追迹及受支持 AutoLens CUDA 路径；GPU 仍调用共享 Core，实际设备和回退单列，不能推定全部分析均可 GPU 执行 |
| 光学容器 | `Optic` | 孔径、视场、波长、表面、材料、追迹、分析、优化、公差和多配置入口 |
| 表面组合 | `Domain` 等 | 几何、前后材料、镀膜、交互、物理孔径、散射和坐标系组合 |
| 光线生成 | `Raytrace` | 角度/物高/近轴像高/实像高、渐晕、远心、常用光瞳采样和七种变迹 |
| 顺序追迹 | `SequentialRayTracer` | 局部交点、孔径裁剪、折射/反射/衍射/全反射、镀膜/散射钩子 |
| 非序列追迹 | `NonSequentialDocument`、`INonSequentialDocumentService`、`NonSequentialDocumentTracer` | 独立场景、编辑器、参考坐标链、BVH、实体介质、Fresnel 分支、方向散射、数据库/路径/探测器与能量账目；支持声明范围的网格和顺序镜组转换，含胶合/浸没、旋转非球面和逐面姿态。版本按所含模型选择，见 [工程格式](STAROPT_FILE_FORMAT.md)。一般 CAD/布尔、偏振/相干、NSC 优化/公差和完整 Zemax NSC 导入仍未完成 |
| 几何 | `Geometries` | 平面、标准面、光栅、非球面、双锥面、环曲面、多项式、Chebyshev、Zernike、Forbes Q；其余自由曲面有明确占位边界 |
| 材料 | `Materials` | Air、Vacuum、常数、Cauchy、Sellmeier、Abbe、目录 n/k 及厂商消歧 |
| 传播 | `Propagation` | 均匀介质、旧入口方向近似及 Gradient 1～5 连续积分；正式顺序显式光线、限定共轴连续一阶与瞄准、桌面材料编辑和受支持系数变量已接入。完整分析/偏振覆盖及原生 GRIN 映射仍不完整，见 [编辑与限制](GRIN_MATERIAL_EDITOR_2026-10-04.md) |
| 镀膜/散射 | `Coatings`、`Scattering`、`NonSequential` | 旧 Experimental 经验损耗与物理薄膜、正式 BSDF 分开。NSC 已有 Lambertian/实测 BRDF/BTDF 的 Evaluate、Sample、Pdf、固定材料轴及多次方向散射；Speos/Zemax 格式与 XYZ 校准仅限各自声明版本、条件和支持域，见 [NSC 指南](NON_SEQUENTIAL_TRACING.md) |
| 真实薄膜与内置镀膜模块 | `CoherentThinFilmSolver`、`ThinFilmSpectrum`、`labs/CoatingDesign` | 同进程必装；真实 n/k、角度/S/P/R/T/A、模板、有限结构搜索、公差、实验回读及四类面板停靠。全板显示已计基板吸收、裸背面和非相干内反射，前面设计验收保持独立。物理膜层接入正式追迹；实验显式应用到当前镜头的 UI、任意目标解析综合及各向异性仍未完成。见 [计算契约](COATING_DESIGN_LAB.md) |
| 相机与 SFR | `Analysis/Imaging`、相机应用服务 | 三维直射/有界标量衍射及 NSC 功率进入传感器/RAW/ISP，冻结 A/B、分级采样和回放；局部单色/镜头多波长斜边与完整瞳孔对照另有原协议。[物方棋盘](SCENE_SLANTED_EDGE_SFR.md)支持预算内完整传感器逐源谱、浮点/Gray16 和原图多 ROI 水平/竖直斜边、质量/法线与回放，不经过 QE/ISP。原例完整幅面、独立工具、正式对照及 ROM/黑盒仍按[独立计划](ANSYS_SCENE_SFR_IMPLEMENTATION_PLAN_2026-11-02.md)验收 |
| 初始结构 | 内置模块、共享 Core | 托管 AutoLens、自主拓扑、一阶准备、兼容镜头检索、受约束补偿、SPC 单层分支/自动准备、自主胶合调度、检查点/方案库/报告已接入。自动口径、递归 SPC、条件生成及更广结构支持仍开放，见 [当前范围](INITIAL_STRUCTURE_LAB_PLAN.md) |
| 分析 | `Analysis` | Core 规范目录、Workbench 单一描述符和按顺序/非序列模式隔离的两套入口；独立畸变入口已合并到场曲/畸变，旧离焦 MTF 名称统一到 Fourier 入口，报告菜单含 5 个真实入口；目录以 `AnalysisCatalog.Names` 为准 |
| 优化 | `Optimization` | 变量、操作数、缩放和五种按真实实现命名的本地搜索；未实现的 BFGS、L-BFGS-B、COBYLA、DE/CMA-ES、信赖域 LM 和 Glass Expert 明确返回不支持，不映射为别的算法。ZMX 评价函数可保留受支持的行与原始参数；执行数量、模式、原生列和数值认证缺口只在 [操作数规范](ZEMAX_OPERAND_SUPPORT.md) 维护，兼容保留不等于完整数值支持 |
| 公差 | `Tolerancing` | 向导、验证、灵敏度、有界补偿、指定频率 FFT/几何 MTF 与逐视场良率、确定性 Monte Carlo；保存兼容与原生数值缺口见 [公差指南](TOLERANCING.md) |
| 多配置 | `Multiconfig` | 配置复制、激活、属性链接/解链和持久化 |
| 文件 | `Serialization`、`FileIO` | STAROPT 完整工程和受支持旧版本有界迁移；版本以 [格式契约](STAROPT_FILE_FORMAT.md) 为准。ZMX 与 SEQ/LEN 是交换子集；只读评价函数参数往返时不裁剪，不支持内容禁止静默降级 |
| 插件 | `Plugins` | 程序集/目录发现，几何、材料、分析注册和失败隔离 |
| 可视化 | `Visualization` | 二维/三维、显式光段方向和交互类型、主题资源 |
| GUI | `OpticalSystemDesign.App` | 中英日 Avalonia、Dock 分栏/浮动/平铺/层叠、功能搜索、五个具体主题及 System 跟随代理；镀膜具有独立布局身份与保存恢复 |

## 当前里程碑

- STAROPT 无损保存丰富表面组件和全部配置，并在替换活动状态前完成校验和临时构建；
- ZMX 覆盖已实现的顺序系统和 Workbench 扩展子集；
- 分析结果提供指标、图形、数据和文本导出；
- 公差 GUI 提供 TDE 风格编辑和 CPU 计算；
- 二维/三维查看器使用真实表面弧矢和顺序追迹历史；
- Dock 窗口会过滤空宿主；独立浮动使用原生窗口，平铺/层叠自动回收页面并在主文档区使用内部 MDI；
- 插件失败不会阻止其他插件。

## 主要缺口

仍开放的范围包括更广原生自由曲面/快照、衍射效率、镀膜实验应用到镜头的 UI 桥接、各向异性/矢量场、一般 CAD/布尔、全条件实测 BSDF/XYZ 认证、相机角度 QE/微透镜/动态快门、完整场景 SFR/ROM 与更广 GPU 覆盖。已有 NSC 分裂/散射、全板非相干显示、自动微分和限定 CUDA 不再列作“尚未实现”；各功能是否满足数值门槛仍依各自证据判定。

Python Optiland 运行后端、JSON 格式和互操作路线已移除。Optiland 0.5.8 仅作为 `validation/history` 的冻结辅助历史参考；后续外部精度验证以仓库 Zemax OpticStudio 2026 R1 基准为主，并逐项记录镜头、设置、版本、单位、采样和容差，不能扩大为通用默认值。
