# 晶体一阶量定义与环曲面 FFT 有效性，2026-11-03

接续[上一阶段的五项阻塞](RESIDUAL_CLOSURE_REPAIR_2026-11-03.md)。本轮修复了两类共性问题：**不同一阶量定义被直接比较，以及严重欠采样的 FFT 仍被当作有效数值输出**。晶体显式同口径的 5 组对照全部通过；环曲面四个原 FFT 请求统一返回带原因的 `Unavailable`，另取得 Huygens PSF、MTF、截面的有效收敛控制。没有把原五项写成数值 Pass，也没有修改处方、旧参考或容差。

## 晶体：标量顶点光焦度与含走离的轴上近轴量

125 原请求中，Core 的有效焦距为 100 mm，原生 EFFL 为 -0.0366626470182 mm。Core 使用顶点传递矩阵及普通光标量折射率，不包含主光线的固定晶体走离；原生配置 2 的非常光且 `ParaxIgnore=0`，轴上近轴光线包含固定走离。把它们视作同一定义，产生了假性的光焦度差异。

独立后台原生控制改变孔径 10/20/40 mm、晶体厚度 25/50 mm 和近轴忽略标志。原生轴上 EFFL 随孔径和走离改变；只在自建、未保存的原生工作系统中设置 `ParaxIgnore=1` 后，EFFL 恢复为 100 mm。**60 个真实光线 REAY/REAB/REAC 操作数完全不变**。这不是将晶体真实走离从产品模型中删除。[原生晶体参数说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Birefringent_In_and_Birefringent_Out.html)解释了该近轴标志；非轴对称系统的一阶量应区分其[近轴与傍基定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Paraxial_and_Parabasal_Rays.html)。本例的具体归因由上述实际控制支持。

新协议 `CenteredVertexOrdinaryIndexV1` 明确使用普通折射率、顶点光焦度及不含主光线走离的定义。Core 结果发布约定和介质模型元数据；原生适配器仅在请求明确选择该约定时修改自建系统的近轴标志，并记录修改前后值及未保存证明。归一化层拒绝将缺少该证明的旧参考用于新协议。

原配置 1、原配置 2、两种孔径和不同晶体厚度共 **5/5 Pass，5/5 混用旧协议被拒绝**，原容差保持。原 125 的历史 Difference 保留；这不是完整各向异性仿射近轴模型的新实现。此外修正原生 EFFL 适配器的波长参数位置：波长属于操作数第二整数槽，实际按请求波长取值。原 125 的主波长结果未因此改写。

## 环曲面：名义 FFT 档位不代表实际光瞳已解析

1365 PSF、1367 MTF、1369 FFT PSF 截面、1372 衍射包围能量共有的根因，是近柱面系统在一个方向未聚焦，光瞳未缠绕相位变化极大。小像面间距还会影响有效光瞳覆盖，不能把名义档位当成实际采样密度。原生 FFT 对近焦/远场假设及柱面系统的限制、Huygens 建议和负间距全光瞳设置，见[官方 FFT PSF 说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/FFT_PSF.html)。

上一阶段原生已检查到 2048 档仍无有效参考。本轮另以全光瞳 `ImageDelta=-1` 检查 512/1024，以及截面明确图幅；PSF 仍提示采样不足，截面仍为空数据缓冲。全部失败记录保留，没有继续重复申请同条件的大阵列。

共享 Core 在实际复光瞳建立后检查相邻有效节点的**未缠绕相位差，包含离焦相位**，以 0.5 波为必要 Nyquist 条件；不跨越物理光瞳缺口，采用行缓冲避免另一份稠密网格。PSF、依赖 PSF 的 FFT MTF、截面和能量路径复用该诊断。Workbench 请求启用验证，并将专用异常转为带诊断的 `AnalysisOutcome.Unavailable`，不返回混叠曲线；一般 Core 有限和接口仍保留计算值及诊断，原默认用途保持。

| 原请求 | 相邻节点最大相位差/波 | 最终产品结果 |
|---|---:|---|
| 1365 PSF | 560.991 | Unavailable |
| 1367 MTF | 106.330 | Unavailable |
| 1369 FFT PSF 截面 | 219.092 | Unavailable |
| 1372 衍射包围能量 | 565.493 | Unavailable |

该必要条件不是收敛证书，也不宣称检查了所有独立的稀疏自相关快速内核。原四份 FFT 原生参考仍无效，原 Incomparable 保留；修复的是产品对这些请求的有效性处理。没有从裁切的 Huygens PSF 重新归一化，制造全平面包围能量。

## 有效替代协议与收敛边界

保持原有 Huygens 的完整声明窗口、0.25 µm 间距、物理坐标及原容差，在独立后台实例捕获并回读实际设置：

| 分析 | Core/原生光瞳采样 | 同档、交叉及双方收敛 | 边界 |
|---|---|---:|---|
| Huygens PSF | 256/512 | 7/7 Pass | 完整声明的有限图像窗口 |
| Huygens MTF | 256/512 | 7/7 Pass | 原频率范围及物理轴 |
| Huygens PSF 截面 | 512/1024 | 7/7 Pass | 原 32 点截面，[-4, 3.75] µm |

共 **21/21 Pass**。截面 1024 同档最大绝对差 4.97892e-7、NRMSE 5.42759e-5；原生 512→1024 最大差 1.6e-5、NRMSE 0.00181051。256→512 的截面收敛仍为 Close，不因后续通过而删除。更早的一次截面捕获虽然计划写为 256/512，实际 `PupilSampleSize` 仍为 32；该次记录保留为设置未生效的失败尝试，未纳入认证。诊断器已补齐该设置并强制核对原生实际回读值。

这些结果只认证上述窗口和请求，不代表原四种 FFT 算法已有有效数值参考，也不外推全平面能量。

## 回归与证据

- 同一最终 Core 重放六支原 **252 项仍为 239 Pass / 1 Close / 1 Difference / 11 Incomparable / 0 Error**，结论零变化、239 项原通过全部保持。另 156 项跨系统既有 Pass 全部保持，范围有重叠不相加。
- 上阶段的 38 项晶体 PSF、晶体能量及环形瞳孔 PSF 对照/收敛，以最终 Core 全部重新通过。
- **611 个定向用例 + 134 个不同的既有操作数用例 = 745 个不同用例通过**，零失败/跳过。新用例覆盖相位必要条件、光瞳缺口、四个真实请求的不可用结果、源状态保持、Core 有限和接口以及异步请求范围隔离。中间失败及沙箱 testhost 连接中止另行保留；最终运行完成。
- 正常默认 Release App 及两个内置模块构建零警告/错误；诊断、测试与 App 使用同一 Core SHA256：`1ce484a713e50d89ccb534fc5ed3f7c19f9a3d471d94c7800d0fce4aae41ed20`。实际源文件另与最终 portable PDB 校验，版本标签不能代替源码身份。

见[机器摘要](../validation/rechecks/2026-11-03/definition-reference/summary.json)、[晶体协议对照](../validation/rechecks/2026-11-03/definition-reference/first-order-convention-results.json)、[四项可用性](../validation/rechecks/2026-11-03/definition-reference/fft-availability-results.json)、[Huygens 收敛](../validation/rechecks/2026-11-03/definition-reference/huygens-converged-results.json)及[证据入口](../validation/rechecks/2026-11-03/definition-reference/README.md)。完整读回新旧载荷并核对哈希后，才退役本轮展开记录与中间运行目录；唯一载荷复用既有归档，保留失败历史。

本轮没有新的 95 支全矩阵或全仓测试，没有控制用户应用、提交或远端同步。旧全量统计仍保留其原 Core 身份。后续若需要环曲面这四种 FFT 的数值认证，仍须取得适用且收敛的独立参考；目前按明确不可用处理。
