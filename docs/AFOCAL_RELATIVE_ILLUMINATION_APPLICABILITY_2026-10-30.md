# 五项无焦相对照度的适用性处理

2026-10-30：五项原请求的适用性已查清。瞄准镜和Apochromat原来的Core异常，以及扩束镜、双筒两配置的缺原生参考，均涉及无焦像空间下不适用的相对照度。共享Core现返回明确的`NotApplicable`，不生成数值曲线；**新增数值Pass为0**。原完整170项账目保持原身份，不把本次分类解释为数值精度通过。

## 定义与本机原生依据

Ansys的[无焦系统设计说明](https://optics.ansys.com/hc/en-us/articles/42661707601683-How-to-design-afocal-systems)明确说明相对照度在无焦系统中没有物理意义。[2026 R1相对照度说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Relative_Illumination.html)将其定义为均匀Lambertian物景在像面单位面积上的照度、以扫描峰值归一化，并要求局部成像共轭及无角空间焦散等条件；不满足条件时不计算。像面照度、原始方向余弦投影面积、透过率和角域能量是不同量，不能互相改名替代。

本轮通过本机OpticStudio 2026 R1 build 260127的独立隐藏ZOS-API实例，重新加载四个原始源文件、五个原配置。输入SHA与原账目一致；原波长、+Y扫描、RayDensity20、FieldDensity20（21点）、去渐晕和无偏振设置均保持，赋值后读取原生设置。没有关闭原请求的无焦像空间、修改处方、保存输入或使用截图。

五项均有一个原生series对象，但XData/YData均为空；原生文本均为“无法计算相对照度...”。消息的ErrorCode枚举却为Success，因此必须同时检查消息、实际缓冲和文本，不能以退出码或枚举单独判Pass。诊断宿主的退出0仅表示诊断采集完成；没有数值缓冲时不写`data.json`。

| 原ID | 配置 | 修复前当前Core复现 | 新原生数值参考 | 现在的Core结果 |
| --- | --- | --- | --- | --- |
| 0016 | 瞄准镜c1，波长2 | 有限瞳孔坐标映射折叠或奇异 | 不可用 | NotApplicable，无曲线 |
| 0046 | 扩束镜c1，波长1 | Success，输出一条曲线 | 不可用 | NotApplicable，无曲线 |
| 0068 | 双筒c1，波长2 | Success，输出一条曲线 | 不可用 | NotApplicable，无曲线 |
| 0098 | 双筒c2，波长2 | Success，输出一条曲线 | 不可用 | NotApplicable，无曲线 |
| 0128 | Apochromat c1，波长3 | 几何主光线未到达像面 | 不可用 | NotApplicable，无曲线 |

冻结旧证据中其实五项原生都已是`NativeInvalidResult`，旧比较顺序先报告两项Core失败，掩盖了它们的原生失败。因此原来的“2项Core不可用+3项缺参考”并非五项待补数值曲线。前三条曾成功的Core曲线也不能视为有效无焦照度。

同一采集链对真正有焦的原`123456.ZMX`正控制正常取得21点、相对照度和有效F/#两列、完整X/Y缓冲，且没有原生消息。它只验证分析接口和缓冲采集正常，不增加正式同输入数值Pass。最终正控制请求仅含实际采集所需选择器和设置，真实光学元数据以`model.json`为准；首版携带其他镜头描述元数据的控制和一次精简时漏版本字段的失败分别保留，不作为最终控制。

## 共享Core的处理边界

`IlluminationMetrics.RelativeIlluminationNotApplicableReason`以声明的`ImageSpaceAfocal`统一判断RI适用性，没有文件名、镜头名、配置号或拟合常量分支。分析入口在积分前返回结构化`NotApplicable`和原因，不填0、1、NaN曲线，也不输出RI/有效F/#数组。原两项不会再落入不适用积分的数值异常；原三项不再误发正常曲线。

RELI的`RelativeToAxis`入口和图像模拟的RI加权复用同一检查。直接数值接口明确抛出带原因的不可用异常；图像模拟保留用户的加权选择并返回不可用，不暗中忽略选项。其提示只建议显式关闭照度加权，不再建议降低视场即可解决无焦不适用。原始`Evaluate/EvaluateFields`投影面积接口保留独立定义和原数值算法；EFNO的独立量及更广辐射度量不由本次获得认证。

没有放松映射秩、折叠判定、物理光线有效性或任何数值验收门槛，没有把角域透过率代作RI，没有第二光学引擎。产品计算继续为纯C#/.NET共享Core，原生API只用于外部验证。

## 验证与保留范围

五份原请求指纹、原源文件SHA、原快照SHA、原设置及原账目的有效容差字段逐项保持。原五项因先前不可比，`effectiveTolerances`原本为空；本次完整保留，不补造已通过的门槛，也不改比较策略。比较侧正常数值归一化继续拒绝非Success结果，不能将NotApplicable送入数值Pass。

最终定向测试按完整身份去重**298/298**通过，零失败/跳过：照度/图像模拟侧237项、原请求与PSF/FFT截面/能量保护61项。其中新增10项覆盖五个冻结原请求、三种积分方式/偏振、RELI共享入口、图像加权不修改像素和原投影面积定义；有焦RI继续通过原捕获门槛。默认App Release构建、六份Core身份和证据字节校验见[机器验证](../validation/rechecks/2026-10-30/afocal-ri/verification.json)及[证据说明](../validation/rechecks/2026-10-30/afocal-ri/README.md)。首次并行构建的输出锁冲突日志保留，随后采用串行构建；没有关闭用户程序。

本轮关闭五项的适用性与错误分类问题，不声称取得五条原生RI数值参考或完成数值对比。完整原生遍历、任意视场瞳孔映射、Huygens收敛及其他历史范围继续按[问题清单](OPEN_ISSUES_2026-10-08.md)声明。全部验证后台执行，未占用桌面、全矩阵重跑、提交或推送。
