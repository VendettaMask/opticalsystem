# 无焦参考中心与惠更斯离焦修复（2026-10-27）

本轮优先完成参考主光线的追迹设置一致性、无焦惠更斯离焦数值修正，再按冻结 Zemax 2026 R1 参考检查四文件五配置，并重算有焦保护集合。没有放宽容差或修改旧原生捕获，后台计算没有打开、关闭或操作用户桌面应用。

## 修正与依据

几何能量的 `EnergyCurveSupport.ReferencePoint` 原来重追参考主光线时遗漏 `RayAimingEnabled`，样本却启用了真实光阑瞄准。现在参考光线与样本使用相同瞄准设置，并保留非偏振入射权重约定。五配置主光线参考曲线均通过独立原生捕获。

无焦惠更斯原先将归一化入瞳坐标乘以标量出瞳半径进行远场积分，既丢失实际出瞳几何，也没有应用有焦路径已有的有限采样单元足迹。现在波前结果保留主光线法向参考平面的真实原点，积分使用该平面上实际光线位置在像方坐标轴的投影。屈光度相位采用实际半径：

\[
\Delta W_D=-\frac{D\,\lVert\mathbf r\rVert^2}{2000\lambda_{\mathrm{mm}}}.
\]

同时按同一有限瞳孔单元约定计算角域足迹，保留真实折返与非圆对称出瞳；不改有焦惠更斯分支、原 FFT 瞳坐标或频率插值约定。只有改频率插值不能统一关闭残差，该诊断结果保留，未作为产品修复。

[Ansys 无焦说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Afocal_Image_Space.html)规定无焦使用参考平面和屈光度离焦；[Huygens PSF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Huygens_PSF.html)说明实际光线波元、参考与采样窗口；[Through Focus MTF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Huygens_Through_Focus_MTF.html)说明按指定焦面计算再输出 101 点样条。实现及外部数值认证仍限本批条件，不据此宣称任意偏振或采样均认证。

## 原生对比结果

最终统一 Core 的原 150 项重算为 **129 Pass / 3 Close / 3 Difference / 15 Incomparable**。五项原惠更斯频率 MTF、五项原惠更斯离焦 MTF、20 个单光线报告全部 Pass；双筒端口光程与原生路径定义也完成重放。

独立的固定角网格 20 项 **全部 Pass**：FFT PSF、Huygens PSF、Huygens MTF、Huygens Through Focus MTF 各五配置。离焦曲线最高 NRMSE 如下，均使用原调制度门槛：

| 文件 / 配置 | 修正前固定网格 | 修正后最高 NRMSE |
| --- | --- | --- |
| Afocal Riflescope / 1 | Close | 0.0004945000 |
| Beam Expander / 1 | Close | 0.0022945352 |
| Binocular System / 1 | Difference | 0.0000398331 |
| Binocular System / 2 | Difference | 0.0000398331 |
| Apochromat prism / 1 | Difference | 0.0004489919 |

150 项使用旧冻结原生参考和当前 Core 重算，不冒称 150 项原生数据均于今天重新采集。今天另有 25 个后台原生诊断请求，包含五配置参考切换、独立质心操作数、有焦正控制和显式原生光线。150 项中两项 Core 相对照度明确不可用，没有超时；原生相对照度五项均无有效数据，不计为数值通过。

## 角质心与原生图表的区别

新捕获确认，五配置的原生无焦几何能量在 `ReferTo=Centroid` 与 `ReferTo=ChiefRay` 下，API 与文本曲线数值均相同，参考坐标文字却变化；有焦 Double Gauss 正控制曲线最大差 **0.1493099122**，证明捕获器实际应用了两种设置。该发现只针对所列文件、版本与设置，不概括所有 Zemax 无焦行为。

因此原 150 项中 Riflescope 和双筒两配置的 **3 个质心图表 Difference 仍保留**，不能改用主光线结果冒充同定义通过。为核对真正的角质心，另用 ZOS-API 在完全相同的 32 区间圆瞳节点上捕获出射方向，离线独立计算平均角与经验 CDF，不调用产品积分或插值代码。三组 **10361 条原生光线、13 条视场曲线**的原始 CDF 节点全部通过原能量容差；没有改写内置图表。独立 CNAX/CNAY 操作数也保存为诊断，因内部栅格约定不同，不混入该严格逐节点通过数。

## 有焦保护、测试与身份

**1159 个有焦原通过请求和 52 个补充控制全部 Pass，零回退。** 原始请求、镜头文件、冻结参考和全局比较策略全部保持；旧通用 Zernike 保护请求仍按原共享 Core 定义执行，不恢复退休桌面入口。

定向测试分别为 Core **385/385**、比较工具原有/新增参考中心和离焦 **89/89**、原生角质心积分 **3/3**，共 **477 个不同测试**，无失败/跳过。默认 Release App 输出中的 Core 与实际原生比较、有焦保护运行器的 Core 一致：`49487714417954f1f7b69a0aa085e9b5eb81dc294a420b317a8b15e8412fc3bc`。全局策略 SHA-256 保持 `30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`；2668 份冻结证据在 Git HEAD 和工作区均保持原字节。

## 尚未关闭的范围

- 原三项内置质心图表定义差异，以及衍射能量一项、FFT 截面两项 Close，均保留原结论。
- 自动 PSF 网格坐标的八项不可比、五项原生相对照度无效、两项混合 NSC 一阶量不适用，合计 15 项不可比，没有算作 Pass。
- 正式比较入口尚未全面接入所有无焦专项契约；相干无焦偏振、任意几何/采样和全部 95 文件不在本轮认证范围。

证据、全部命令入口与阶段失败见[本轮归档](../validation/rechecks/2026-10-27/afocal-reference-focus/README.md)，机器账目见[验证摘要](validation/AFOCAL_REFERENCE_FOCUS_REPAIR_2026-10-27.json)。此前[10 月 26 日停止点](AFOCAL_ANALYSIS_REPAIR_2026-10-26.md)保留历史结论。本轮不包含 Git 推送、安装包发布或已安装软件验收，其他线程的本地修改保持。
