# Optiland 兼容矩阵

本表按公开 Optiland 文档跟踪 .NET 实现状态。“已实现”只表示列出的边界，不代表完整等价。

| 领域 | .NET 模块 | 当前状态 |
| --- | --- | --- |
| 数值后端 | `Backend` | 标量 `INumericBackend`、批量接口、CPU/SIMD 后端和注册表 |
| 光学容器 | `Optic` | 孔径、视场、波长、表面、材料、追迹、分析、优化、公差和多配置入口 |
| 表面组合 | `Domain` 等 | 几何、前后材料、镀膜、交互、物理孔径、散射和坐标系组合 |
| 光线生成 | `Raytrace` | 角度/物高/近轴像高/实像高、渐晕、远心、常用光瞳采样和七种变迹 |
| 顺序追迹 | `SequentialRayTracer` | 局部交点、孔径裁剪、折射/反射/衍射/全反射、镀膜/散射钩子 |
| 几何 | `Geometries` | 平面、标准面、光栅、非球面、双锥面、环曲面、多项式、Chebyshev、Zernike、Forbes Q；其余自由曲面有明确占位边界 |
| 材料 | `Materials` | Air、Vacuum、常数、Cauchy、Sellmeier、Abbe、目录 n/k 及厂商消歧 |
| 传播 | `Propagation` | 均匀介质和简化 GRIN |
| 镀膜 | `Coatings` | 镀膜栈和四分之一波合成骨架；完整薄膜 TMM 未完成 |
| 分析 | `Analysis` | 桌面分析目录与 30 个 Python 来源契约，另有 Zemax 捕获基准 |
| 优化 | `Optimization` | 变量、操作数、缩放、局部/全局优化；Glass Expert 未实现且会明确返回不支持，不回退到其他算法 |
| 公差 | `Tolerancing` | 向导、验证、灵敏度、补偿和确定性 Monte Carlo |
| 多配置 | `Multiconfig` | 配置复制、激活、属性链接/解链和持久化 |
| 文件 | `Serialization`、`FileIO` | STAROPT schema 4、旧 schema 安全迁移、Python JSON 子集、ZMX 与 SEQ/LEN 子集 |
| 插件 | `Plugins` | 程序集/目录发现，几何、材料、分析注册和失败隔离 |
| 可视化 | `Visualization` | 二维/三维、显式光段方向和交互类型、主题资源 |
| GUI | `OptilandWorkbench.App` | 中文 Avalonia、Dock 分栏/浮动/平铺/层叠、命令面板、三主题和按文件会话 |

## 当前里程碑

- STAROPT 无损保存丰富表面组件和全部配置，并在替换活动状态前完成校验和临时构建；
- ZMX 覆盖公开 Optiland 0.5.8 顺序边界和 Workbench 扩展子集；
- 分析结果提供指标、图形、数据和文本导出；
- 公差 GUI 提供 TDE 风格编辑和 CPU 计算；
- 二维/三维查看器使用真实表面弧矢和顺序追迹历史；
- Dock 窗口会过滤空宿主；独立浮动使用原生窗口，平铺/层叠自动回收页面并在主文档区使用内部 MDI；
- 插件失败不会阻止其他插件。

## 主要缺口

Forbes/NURBS/grid-sag JSON 广度、衍射效率、完整薄膜 TMM、矢量衍射、非顺序追迹、更广商业格式、完整 GUI 自动化以及 GPU/自动微分后端仍未完成。
