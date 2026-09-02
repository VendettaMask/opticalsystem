# 系统未完成能力收口计划

## 文档状态

- 复核日期：2026-09-02
- 范围：正式 Workbench、Zemax/Optiland 互操作、非序列模式、优化与公差、Initial Structure Lab、UI 产品表达
- 性质：实施计划，不是当前完成能力清单
- 当前仓库状态：计划文档已建立；Zemax 评价函数真实化已启动，本机 OpticStudio 2026 R1 实测目录已同步为 383 个顺序兼容代码，108 个操作数完成定义级可执行接入；未完成项主要来自明确的兼容边界、实验性模型、Zemax golden 对照缺口和路线图差距

本文把当前项目中“已经显式标注但尚未完成”的能力收拢成可执行计划。所有阶段都必须继续遵守：

1. 不能把兼容保留、只读保存或 Experimental 近似宣传为完整实现；
2. 新能力必须先有数据模型边界、失败语义、测试和文档，再进入公开 UI；
3. 与 Zemax/Optiland 的对齐必须区分导入可见、原样保存、可执行计算和数值等价；
4. 每个完成项都要同步代码、测试、文档和验证结果。

## 总优先级

| 优先级 | 主线 | 目标 |
| --- | --- | --- |
| P1 | Zemax 评价函数与 ZMX 互操作 | 消除“导入看得见但不能执行”的核心差距 |
| P1 | 非序列高级物理与查看链路 | 补齐 Zemax NSC 工作流中最影响使用的查看、筛选、探测器和物理事件 |
| P1 | 优化算法与 Glass Expert | 保持算法命名真实性，逐步加入真实高级优化器和玻璃替换引擎 |
| P2 | 公差系统 | 扩展 Zemax 风格公差、反求、向导和报告能力 |
| P2 | Python Optiland JSON 互操作 | 扩展拾取、求解、自由曲面、偏振和非序列对象互操作 |
| P2 | 物理模型真实性 | 把 Experimental 膜层、散射和 GRIN 近似逐步替换为真实物理模型 |
| P3 | Initial Structure Lab 产品化 | 完成实验室 L5、数据库、恢复、算法和主程序入口边界 |
| P3 | UI 与产品表达边界 | 避免界面暗示尚未完成能力，提升复杂功能可理解性 |

## P1：Zemax 评价函数与 ZMX 互操作

### 当前状态

`ZemaxOperandRegistry` 已按实际运行的 OpticStudio 2026 R1 注册 383 个顺序兼容代码，其中当前 108 个已接计算引擎并标为 `Executable`，其余为 `CompatibilityOnly`。本轮目录校准已完成：移除 10 个非真实或当前顺序 MFE 不可选代码，补入 60 个当前 API 可选代码，展开 `I1`–`I6` 梯度折射率族，并补全 `NPAF/RSNC` 非序列排除。ZMX 导入器能按源顺序保留注册行，但禁用兼容行不参与 Workbench 数值评价。已加入首批描述符槽位语义，并让 `TTHI/TGTH`、`REAR/RANG`、基础数学行、厚度/曲率/圆锥/半口径边界、`WLEN/INDX` 以及若干一阶量通过有序评价或现有近轴/几何引擎执行；`SUMM/PROD/DIFF/DIVI` 采用 Zemax 双行引用语义，三角函数 Flag 采用 Zemax 弧度/角度语义。常见操作数束已完成定义级可执行接入，但仍需 Zemax/ZOS-API golden 数值对照后才能标为完整兼容。

仍需完成：

- 383 项操作数的逐类型参数语义、单位、默认值和验证；
- Merit Function 编辑器按描述符切换列名、单位和可编辑状态；
- 剩余数学约束、控制、质心、MTF、圈入能量、鬼像、POP、GRIN、偏振等操作数执行；
- ZOS-API 行色 `Color1`–`Color16`、逐行无颜色和全局 `Color Rows` 偏好往返；
- ZMX 坐标断点顺序、复杂 toroidal、theodolite field、部分 FNUM/OBNA 子类型。

### 实施阶段

1. **描述符模型**
   - 为每个操作数定义参数槽：名称、类型、单位、默认值、是否引用表面/视场/波长/配置。
   - 将现有 `Int1`、`Int2`、`Data1`–`Data4` 作为原始槽位保存，描述符只提供类型化视图。
   - 增加快照迁移和非法槽位验证。
   - 进度：已为瞳孔光线类、RMS 类、Moore-Elliott 类、`EFFL/TOTR/TTHI/TGTH`、常见厚度/曲率/圆锥/半口径/玻璃折射率/一阶量与基础数学行建立首批槽位描述；本机 ZOS-API 实测已校正 RMS 的 `Ring/Wave/Hx/Hy`、Moore-Elliott 空首列、厚度 Mode 位置、中心范围与半口径范围空列及行边界单列语义。快照校验已能区分范围厚度项的 `Int2` 终止表面、数学行引用、边缘方向代码和常规波长。

2. **编辑器切换**
   - Merit Function 表按操作数描述符显示列标题、单位、范围和只读状态。
   - 兼容行继续可见但不可执行，启用时必须给出明确错误。
   - 行色偏好作为编辑器元数据保存，不影响贡献值。

3. **基础可执行操作数**
   - 已完成真实 ZMX 已出现的首批只读提升：`CTGT`、`OPLT`、`MNCA`、`MNEA`、`MNCG`、`MNEG`、`MXCG`、`MXEG`、`PMAG`、`PETZ`。
   - 已完成：光线/RMS/Moore-Elliott 首批、`TTHI/TGTH`、`REAR/RANG`、基础数学与行约束、`CT*/ET*/FT*/TT*/STHI` 厚度项、`MN*/MX*/X*` 常见范围厚度/曲率/半口径项、`CO*/CV*` 表面标量项、`WLEN/INDX`、`EFFL/EFLX/EFLY/ENPP/EPDI/EXPP/EXPD/ISNA/ISFN/SFNO/WFNO`、`PMAG/PETZ`。
   - `DIMX` 已校正为 `Field/Wave/Absolute`，在指定视场和绝对长度模式完成前保持兼容只读；`EFNO` 已确认是内置有效 F/# 操作数，后续按 `Samp/Wave/Field/Pol?` 接入，不能伪实现为普通 F/# 或用户扩展。

4. **有序评价函数虚拟机**
   - 已支持 `CONS`、`SUMM`、`PROD`、`DIVI`、`DIFF`、`MAXX`、`MINN` 及基础一元数学行按前序行执行，其中 `SUMM/PROD/DIFF/DIVI` 为双行引用，`MAXX/MINN` 为范围引用。
   - 后续支持 `GOTO`、`OOFF`、`SKIN`、`SKIS`、`ENDX` 等控制行。
   - 当前已检测基础数学行的越界引用、未来行引用、非法数学域和除零；循环跳转检测留待控制流阶段完成。

5. **分析型和高级操作数**
   - 接入 MTF、圈入能量、光纤耦合、鬼像、POP、高斯光束、GRIN、镀膜/偏振。
   - 缓存键必须包含完整参数、视场、波长、采样、偏振和配置。
   - 所有长耗时计算支持取消和错误隔离。

6. **ZMX 覆盖扩展**
   - 坐标断点 order flag；
   - toroidal conic / polynomial 参数；
   - theodolite-angle field；
   - paraxial-image F/# 和 object-cone-angle 子类型；
   - 非序列 ZMX 继续明确拒绝，直到非序列导入主线启动。

### 验收门槛

- 每新增一个操作数，至少包含：参数映射、非法参数、数值计算、ZMX 导入、STAROPT 往返和贡献值测试。
- 禁用兼容行仍为零贡献；启用未实现行仍必须失败，不能返回伪零。
- 文档中只有满足九项支持定义的操作数才能标记为完整支持。

## P1：非序列高级物理与查看链路

### 当前状态

非序列模式已具备对象表、原生光源、基础实体、标准镜片、STL、矩形探测器、Fresnel 分支、STARRDB、路径筛选、3D 布局样本和过期会话保护。当前仍未实现 STEP/IGES/SAT 精确 CAD、CAD 面映射、布尔对象、表面/体散射、BSDF、偏振、相干探测、非序列优化/公差、Zemax NSC/ZRD 导入和 GPU/SIMD。

### 实施阶段

1. **查看链路补强**
   - STARRDB 增加按射线 ID 和分块索引的直接跳转。
   - 数据库查看器增加可展开父子树、后台分页和选中路径。
   - 3D 布局按数据库选中项高亮完整父子路径。
   - 探测器导出完整数值报告，包含设置、筛选、统计和单位。

2. **路径语义分类**
   - 自动标记主路径、鬼像、机械杂散光、异常逃逸。
   - 统计行可生成可复用筛选表达式。
   - 路径筛选 AST 在数据库、探测器和 3D 页面间共享。

3. **探测器扩展**
   - Detector Color；
   - Detector Polar；
   - Detector Surface；
   - Detector Volume；
   - 普通对象探测与体吸收切片。

4. **散射、镀膜和偏振**
   - 表面散射与体散射事件；
   - BSDF `Evaluate`、`Sample`、`Pdf`；
   - 偏振态传播和相干探测；
   - 镀膜与非序列分支事件统一到路径数据库。

5. **精确 CAD 与对象扩展**
   - STEP/IGES/SAT 导入；
   - CAD 面属性映射；
   - 布尔对象；
   - IES/LDT 与厂商射线文件；
   - 更多 Zemax NSC 对象的可计算映射。

6. **设计工具和性能**
   - 非序列探测器/路径操作数；
   - 非序列优化向导、公差与 Monte Carlo；
   - SIMD/GPU、BVH 缓存、超大数据库查询和性能基线。

### 验收门槛

- 所有新对象和事件必须可保存到 STAROPT，并能在 STARRDB 中保留完整路径语义。
- 3D、探测器和路径统计读取同一不可变结果，不得互相触发隐式追迹。
- 大结果测试必须覆盖取消、分页、内存上限和过期会话。

## P1：优化算法与 Glass Expert

### 当前状态

公开优化器仅包含真实实现：Damped Least Squares、Nelder-Mead、Coordinate Pattern Search、Momentum Gradient Descent、Greedy Random Perturbation。BFGS、L-BFGS-B、COBYLA、Powell、Differential Evolution、Dual Annealing、Basin Hopping 和 Glass Expert 明确不支持。

### 实施阶段

1. **Wolfe BFGS**
   - 强 Wolfe 线搜索；
   - 曲率更新保护；
   - 梯度范数、函数评价次数和停止原因。

2. **信赖域 Levenberg-Marquardt**
   - 增益比调整半径；
   - 阻尼与约束边界协调；
   - 与现有 DLS 区分命名。

3. **L-BFGS-B**
   - 活动边界；
   - 投影梯度；
   - 有限内存校正对。

4. **COBYLA**
   - 线性近似约束；
   - 信赖区域更新；
   - 约束违反诊断。

5. **全局/群体优化**
   - Differential Evolution；
   - CMA-ES；
   - Dual Annealing；
   - Basin Hopping。

6. **Glass Expert**
   - 作为独立玻璃替换引擎，不伪装成普通优化器。
   - 需要玻璃目录过滤、色散/折射率目标、热/成本/库存约束、替换建议和解释报告。

### 验收门槛

- 每个算法加入公开目录前必须通过标准函数、有界问题、约束问题、固定种子、停止条件和评价次数测试。
- 运行记录保存真实算法名称、版本、停止原因、迭代次数、评价次数、随机种子和兼容警告。

## P2：公差系统

### 当前状态

公差系统已覆盖 TDE 风格编辑器、向导、灵敏度、反向灵敏度、补偿、确定性 Monte Carlo、报告和图表。当前限制包括不支持 Zemax `.TOL`，向导未覆盖面形、条纹、Zernike、多配置和脚本操作数，`TPAR` 覆盖有限，没有 3 项/5 项多项式灵敏度拟合、缓存复用和分视场/分配置反求。

### 实施阶段

1. Zemax `.TOL` 读取和只读预览。
2. `.TOL` 到 Workbench 公差模型的可执行映射。
3. 向导生成面形、条纹、Zernike、不规则度、多配置和脚本操作数。
4. 专用多变量补偿向导。
5. 3 项/5 项多项式灵敏度拟合。
6. 分视场、分配置反求和缓存复用。
7. MTF 等“越大越好”准则的反向方向支持。

### 验收门槛

- 导入的 `.TOL` 不得静默丢失操作数。
- 不能执行的公差行必须只读保留并显示原因。
- 反求、补偿和 Monte Carlo 均须验证变量恢复和活动处方不变。

## P2：Python Optiland JSON 互操作

### 当前状态

Workbench 可读取 Python Optiland 0.5.8 的核心顺序系统子集，但 pickups、solves、偏振状态、完整 BSDF/镀膜、更广自由曲面、非序列对象和第三方扩展仍不支持。桌面端只保留 Python JSON 导入兼容，不再提供 Python JSON 导出入口；原生保存使用 STAROPT。

### 实施阶段

1. pickups/solves 只读导入和 STAROPT 保存。
2. pickups/solves 可编辑模型和依赖刷新。
3. 扩展自由曲面、组合孔径和材料传播模型映射。
4. 完整镀膜、BSDF 和偏振状态。
5. Python Optiland 非序列对象导入。
6. 重新评估是否恢复受限导出入口；若恢复，必须明确不能无损表达的失败边界。

### 验收门槛

- 不能表示的组件必须失败，不能替换为平面、常数材料或空交互。
- 导入后 STAROPT 保存必须无损保留可表示数据和明确阻断数据。
- 与外部 Python 的差异必须在文档中区分：Workbench 字典兼容、外部 Python 再加载、STAROPT 原生保存。

## P2：物理模型真实性

### 当前状态

当前膜层与散射模型是 Experimental 近似：`ApproximateTransmissionRippleCoating`、`MainRayScatterLossApproximation`、`MeanMeasuredScatterLoss`。径向传播是入口方向近似，不是完整 GRIN eikonal/Hamilton 求解器。

### 实施阶段

1. 保持现有 Experimental 标注和兼容入口，不扩大宣传。
2. 实现真实薄膜 S-matrix：
   - 复折射率；
   - 入射角；
   - S/P 偏振；
   - 相位；
   - 吸收。
3. 实现 BSDF：
   - `Evaluate`；
   - `Sample`；
   - `Pdf`；
   - 能量守恒测试。
4. 实现 GRIN 曲线路径：
   - eikonal/Hamilton 积分；
   - 表面事件检测；
   - 连续 OPL；
   - 与顺序/非序列追迹整合。
5. 给薄膜、BSDF、GRIN 建立解析或外部金标准。

### 验收门槛

- Experimental 近似和真实物理模型必须使用不同类型名、UI 文案和序列化 kind。
- 真实模型必须有能量、相位、偏振或路径积分的定量测试。

## P3：Initial Structure Lab 产品化

### 当前状态

Initial Structure Lab 是独立实验室功能，不属于正式产品能力。L1/L2/L3 和 L4 核心桌面工作流已完成，但 L5 评审、跨平台人工 UI 验收、SQLite/内容寻址数据库、CMA-ES、离散玻璃搜索、真正 Pareto 聚类、检索/排序/生成模型和智能体仍未完成。

### 实施阶段

1. 完成跨平台 UI 验收和 L5 评审。
2. 引入 SQLite 索引和内容寻址压缩候选存储。
3. 增加差分进化种群级检查点与恢复。
4. 实现 CMA-ES 和离散玻璃搜索。
5. 实现真正 Pareto 前沿、聚类半径标定和 UI 解释。
6. 增加历史检索、排序模型和条件生成。
7. 若接入主程序，只允许通过开关控制的薄启动器，不建立正式程序集到实验算法的反向依赖。

### 验收门槛

- 实验室功能进入正式产品前必须有独立验收报告。
- 主程序入口必须明确标注实验性质，直到产品化边界完成。
- 候选导出继续坚持写后回读验证和原子替换。

## P3：UI 与产品表达边界

### 当前状态

大部分 UI 风险已有修复记录和契约测试，但仍有表达边界需要打磨：Zemax 行色偏好未往返，多配置面板能力表达偏窄，查看器设置浮层可能遮挡场景，部分小控件语义不够直观。

### 实施阶段

1. Merit Function 行色偏好：
   - 自定义 `Color1`–`Color16`；
   - 逐行无颜色；
   - 全局 `Color Rows`。
2. 多配置面板升级为配置管理视图：
   - 配置变量；
   - 配置操作数；
   - 配置差异；
   - 批量应用。
3. 查看器设置拆分：
   - 追迹输入；
   - 显示偏好；
   - 数据状态。
4. 补充 tooltip、空状态、错误状态和禁用原因。
5. 发布前固定执行窄 Dock、高 DPI、明亮/暗夜/异世界/像素四主题视觉复核。

### 验收门槛

- UI 不能让用户误以为未实现能力已经可执行。
- 禁用入口必须显示原因和下一步。
- 新面板必须通过可访问性、键盘、窄窗口和主题测试。

## 建议执行顺序

### 第 1 阶段：Zemax 评价函数真实化

目标是先解决“导入完整但计算不完整”的最大可信度问题。

交付：

1. Zemax 操作数描述符模型；
2. Merit Function 描述符驱动 UI；
3. `[MS-L7]` 中 `CTGT`、`OPLT`、`MNCA`、`MNEA`、`MNCG`、`MNEG`、`MXCG`、`MXEG`、`PMAG`、`PETZ` 已完成定义级可执行接入，下一步补 Zemax/ZOS-API golden；`DIMX` 在完整 `Field/Absolute` 语义完成前保持兼容只读；
4. 已完成基础数学行最小虚拟机和常见厚度/边界束；下一步扩展控制行、质心类、剩余玻璃/参数约束和 Zemax 特殊贡献语义；
5. 对应 ZMX/STAROPT 往返和贡献测试。

### 第 2 阶段：非序列查看与路径联动

目标是提升现有非序列链路的可用性，而不是先扩物理范围。

交付：

1. STARRDB 直接索引；
2. 数据库树；
3. 3D 路径高亮；
4. 探测器完整报告；
5. 主路径/鬼像/杂散光分类。

### 第 3 阶段：优化与公差补强

目标是让优化/公差进入更接近 Zemax 工作流的状态。

交付：

1. Wolfe BFGS；
2. 信赖域 LM；
3. Zemax `.TOL` 导入；
4. 公差向导扩展；
5. 多变量补偿。

### 第 4 阶段：真实物理模型

目标是替换 Experimental 近似中的高价值部分。

交付：

1. 薄膜 S-matrix；
2. BSDF；
3. 偏振/相干探测；
4. GRIN 曲线传播。

### 第 5 阶段：实验室产品化和高级非序列

目标是把探索性能力变成可维护产品能力。

交付：

1. Initial Structure Lab L5；
2. SQLite/内容寻址数据层；
3. CMA-ES/玻璃搜索/Pareto；
4. STEP/IGES/SAT 与 CAD 面映射；
5. 非序列优化、公差和性能加速。

## 每轮实施的固定检查清单

每个阶段或子任务完成前必须确认：

1. 代码没有把未实现能力伪装成成功；
2. UI 能显示禁用原因或 Experimental 限制；
3. STAROPT/ZMX/Python JSON 的保存边界明确；
4. 单元测试覆盖正常、非法、往返、取消或资源上限；
5. 文档区分已实现、兼容保留和计划；
6. `dotnet build`、相关定向测试、格式检查和空白检查通过；
7. 如果验证基线变化，README 和相关文档同步更新。
