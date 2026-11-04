# 中间分析面与波前参考修复，2026-11-04

接续[定义核查](ZEMAX_DEFINITION_REVIEW_2026-11-04.md)，本轮修复三处设置未进入实际计算的问题：**中间面准备、焦系统 Absolute/Absolute2 OPD，以及波前图 Reference To Primary**。679个不同定向用例通过；原默认数值与已有比较结论保持。没有新增原生数值认证或全95支重跑。

## 共享中间面准备

OPD、波前图、FFT PSF/MTF与干涉图统一使用共享Core的分析副本准备，保留原处方和选定面的实际光学作用，再建立同介质临时像面。无焦系统不移动到近轴焦点；有焦系统按主波长边缘光线的零高度求解临时近轴像距，允许可表示的实焦和虚焦。像高视场由原系统主波长主光线转换到角度/物高；光阑前的非瞄准请求使用原入瞳及独立参考光阑。副本保留运行时材料、后端和实际裁光定义。

处理依据是官方[中间面规则](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optical_Path_Difference.html)及[主波长近轴高度求解](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Thickness_Solves.html)。原来OPD/波前图只改变标签、999也Success，现四类999请求全部拒绝；PSF/MTF保留既有0代表像面的API约定。

示例2的面12现实际计算OPD和波前分布，与最终面不同；FFT在正确临时像面仍因0.892565波局部步进而Unavailable，采样保护保持。原面2在光阑前且启用瞄准：四类请求均明确拒绝，未虚构其有效光瞳或原生支持。该限制及没有有限近轴焦点的情况仍有明确边界。

## OPD显示参考与物理衍射相位

共享波前入口现在对有焦Absolute/Absolute2使用含相位像面光程差，不减参考球回溯项；不再无条件计算出瞳参考球。按[官方Absolute定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Absolute_Absolute_2.html)，示例2的原诊断点由错误的 **3.685951913949262波**改为 **−15.081888459581972波**，与相同物理输入的共享追迹器像面光程定义严格一致，原18.767840373531232波定义误差清零。这是独立定义控制，**不是新Zemax捕获值**。

FFT、Huygens、采样MTF、Foucault等物理消费者明确保留出瞳相位；Absolute显示结果不能冒充预备FFT光瞳输入。三种参考设置下的FFT/MTF/Huygens同输入保持测试通过。Infinity所需角度修正本轮未实现，改为明确Unsupported，避免再静默使用ExitPupil。无焦参考分支保持既有行为，不由本轮外推认证。

## 波前图参考主波长

新增独立的`ReferenceToPrimary`设置并传入实际波前计算，选择主波长参考球；不再将旧`ReferenceChiefRay`标注当成此开关。[官方波前图说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Wavefront_Map.html)定义了该选项的横向色差含义。

三个波长的六份完整图控制显示：选主波长时开关前后数组完全相同；选其他波长时图和RMS按参考球改变。例如0.4861µm在示例2第三视场的RMS由0.9643650874变为0.8430589411波，参考波长为0.5876µm。原系统和输入设置未被分析改写。新选项尚无新增原生勾选/不勾选的认证。

## 验证与保留问题

- **679/679不同定向用例通过**，零失败/跳过，含27个新控制、既有干涉图原生网格与MTF设置用例。源状态、实/虚焦、两类视场、光阑前处理及相位边界均有检查。
- 16份真实中间面请求、4组参考模式、6份主波长参考图完成。4份默认OPD、波前、PSF和MTF的完整物理数值数组与修复前逐字节一致。
- 示例2/5的60份原请求重导入和比较仍为 **52 Pass/4 Close/2 Difference/2 Incomparable/0 Error**；跨系统156项旧Pass全部保持，范围不相加。六项旧离轴OPD残差、19项FFT采样候选及无效原生能量参考均未升级。
- 正常默认Release App与两个内置模块构建零警告/错误。额外主测试程序集曾被另一testhost锁住，该次未完成、不计通过，未结束其他进程；独立定向程序集验证完成。

最终Core SHA256为`8821d3a2e315641238275f0343832f19e1dd337bcdf36b4a0cad136801136920`，诊断、定向测试及App一致；实际Core/Application/诊断/测试源码与portable PDB另行核对。中间编译和测试失败保留，最终通过记录单列。原处方、原生参考和容差保持，不进行镜头名特判、残差拟合、采样放宽或默认加密替换。

见[摘要与逐项证据](../validation/rechecks/2026-11-04/analysis-surface-reference/README.md)和[复算工具](../tools/diagnostics/AnalysisDefinitionRepair20261104/README.md)。新旧载荷完整读回验证后才退役本轮展开证据，未复制整套参考。其他工作区改动保留；本轮未控制用户桌面、提交或推送。
