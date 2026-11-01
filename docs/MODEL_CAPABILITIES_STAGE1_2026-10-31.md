# 模型能力补齐第一批，2026-10-31

> 接续提示：下文是第一批历史范围；原 1,098/751 项模型阻塞此后已全部解除，不能继续作为当前未支持数量。当前分析差异和参考缺口见[中文分类交接](EXAMPLE_ACCURACY_HANDOFF_2026-11-01.md)。

本批接入 **TILTSURF、FZERNSAG、CHEBYSHV、GRINSUR1、GRINSUR9，以及明确的 US_MEMS 示例契约**。计算均在纯 C# 共享 Core。原 1,098 项中 **347 项解除原面型/材料能力阻塞，751 项仍被剩余模型阻塞**；这不是全部实现完成，也不代表解除阻塞的项目全部精度通过。

相关 **12 支示例、392 项原请求**按同一冻结 Core 重算，结果为 **148 Pass、18 Close、59 Difference、72 Incomparable、95 Error**。Close 未通过，参考缺失和执行失败不计通过。原 95 支总体账目未覆盖写入；本批没有重新运行其余镜头或重采全部原生分析图。

完整[逐镜头表](../validation/rechecks/2026-10-31/model-capabilities-stage1/BY_LENS.md)、[逐请求结果](../validation/rechecks/2026-10-31/model-capabilities-stage1/results.json)、[原 1,098 项逐项能力进度](../validation/rechecks/2026-10-31/model-capabilities-stage1/capability-progress.json)、[原生直接控制及排除原因](../validation/rechecks/2026-10-31/model-capabilities-stage1/native-controls.json)已保存。

## 实现范围

| 能力 | 当前实现及边界 |
|---|---|
| 倾斜平面 | 真实平面求交、解析法线；斜率为无量纲，后续坐标框架不随面倾斜而旋转 |
| Fringe Sag | 基础圆锥/偶次非球面、37 项 Fringe 序列、偏心、归一化半径及外推标志；解析多项式法线；共享原有 Fringe 项号表 |
| Chebyshev | 球面基础、最高 14×14 阶、原点参考、归一化及矩形定义域；解析法线 |
| Gradient 1 | 接入原有连续积分、空间折射率及一阶传递；当前导入非零轴上不可微径向一次项仍保留为未支持 |
| Gradient 9 | 真实 B/C/K0/K1/K2 色散和空间梯度；完整系数随 STAROPT 保存；相邻 GRADIENT_9.DAT 优先，否则使用带源哈希的完整九材料目录；缺失材料明确拒绝，不使用 GLAS 中的均匀折射率代替；非零面斜率导入仍未接入 |
| GRIN 返回路径 | 有符号一阶传播、反射矩阵、正反向追迹；反射保留原材料坐标框架；材料局部精确交点随不可变路径状态传递，避免大坐标量化导致重合面错过交点 |
| MEMS | 仅 US_MEMS.DLL 已知示例契约：平面交会、三态像素法线、行/列/逐像素寻址和每字 15 个三进制状态；产品计算不调用 DLL；未知 DLL 仍为不可计算负载 |
| 文件与身份 | 新几何的 STAROPT 和对应 ZMX 参数往返；GRIN 外部文本导出仍明确拒绝；缺少完整列的文本格式拒绝把新几何压平成标准面；未知 TYPE 参数及 XDAT 保留 |

定义依据为 Ansys 官方的[倾斜面](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Tilted.html)、[Fringe Sag](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Zernike_Fringe_Sag.html)、[Chebyshev](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Chebyshev_Polynomial.html)、[Gradient 1](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Gradient_1.html)及[Gradient 9](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Gradient_9.html)。MEMS 对照绑定本机示例 DLL 哈希与随安装提供的源契约；DLL 和 C 源码未成为产品运行依赖。

## 验证与未关闭项

- 118 项针对性测试通过，默认 App Release 构建通过。闭式 GRIN 轴上传递、真实小光线极限、反向追迹、轴向梯度反射坐标框架，以及原无效处方拒绝均有检查。
- 同一 Core 的新原生标量控制覆盖非零 Fringe/偏心、Chebyshev、MEMS 三态、SELFOC 三波长及双程 GRIN。原生采用独立 CreateNewApplication、截图关闭和拥有者进程隔离，未操作用户桌面。
- Gradient 9 的 0.1→0.05→0.025 步长控制保留全部原记录。SELFOC 的 765 个控制值满足既有显式光线界限；双程系统以 0.025 步长为收敛控制，577 个已映射值满足原界限。双程较粗两档合计 14 个超限值仍保存为 Difference。不能据这些点控制升级全部旧分析图的收敛认证。
- 双程镜头表面 3 的零厚度镜面/GRIN 重入中间方向定义仍未闭合；原差异保留，54 个中间方向/每步长不计通过。其后表面 4、5、6 的实际方向分别核验，不覆盖该中间记录。
- 一轮探索性请求误含原生声明范围之外的波长 2、3；即使原生替换成主波长，这些值也全部排除。准备脚本已改为仅采用声明波长。另保留缺少显式 CURV 的早期 Chebyshev 控制，其原生缺省与 Core 输入不同，不用作等价处方证据。
- 尚未通过的分析、主光线/像面缺失、采样与参考缺口，以及无焦 Huygens 历史收敛工作仍开放。GRIN 偏振连续运输及布局曲线节点等原边界仍明确拒绝；本批没有桌面 GUI 验收。

剩余能力优先级：**PARAXIAL/PARAX_XY 的真实作用及 OPD 规则 → BIRE__IN/BIRE_OUT → 衍射/二元/全息相位 → 网格面与 Grid GRIN → Irregular**。不能将既有二次薄透镜 OPD 直接当作完整近轴面支持；原生近轴面有依赖共轭的模式及逐光线相位积分，需补共享 Core 的相位路径与控制。[原生近轴面定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Paraxial_sequential_surfaces_lens_data_editor.html)

本批 Core：`31a2956910768cc0f39751fa7412f5cedbccf69b40a531a85bc63d8147ba3b6d`。原请求、容差文件和旧失败记录保持，旧不透明快照不被静默转换；新能力来自重新导入源文件。所有统计均为请求数，剩余面型涉及数存在重叠，不能求和。
