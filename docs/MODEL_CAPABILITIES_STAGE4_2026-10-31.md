# 模型能力补齐第四批，2026-10-31

> 接续提示：本批 237 项剩余模型阻塞已在第五批解除。强非空气 Paraxial XY 相位等扩展控制仍开放，原比较记录保持；当前分类与范围见[中文交接](EXAMPLE_ACCURACY_HANDOFF_2026-11-01.md)。

上批剩余 302 项中，**65 项解除模型能力阻塞，237 项仍未支持**。用户指定的 751 项累计解除 514 项；原始 1,098 项累计解除 861 项。解除阻塞不代表精度通过。

纯 C# 共享 Core 新增 Binary 3 的双区圆锥/偶次非球面、独立归一化衍射相位、级次及恒定相位偏移。两区分别求交，保留断差和边界内区归属；连续模式仅平移外区矢高，断差模式不平移。外区相位在两种模式均连续。完整 STAROPT/ZMX 往返及真实长度单位转换已核验，现代 PARM 13+ 与旧 XDAT 均可导入；原生控制使用实际可载入的 XDAT 保存格式。

Paraxial XY 共享理想近轴交互和物方光束积分，独立 X/Y 空气光焦度单位为 mm^-1。一阶标量结果沿原有 Y 子午定义，不把 Y 功率复用为真实 X 光线功率。非对称导出保留 PARAX_XY，未使用单焦距代替。

相关 2 支示例、66 项原请求后台重算：**56 Pass、2 Close、3 Difference、5 Incomparable**，无执行错误。原门槛和旧参考保持，Close 不计通过，也未认证旧分析参考均已收敛。双区直接原生点控制覆盖两种断差、两波长及空气/玻璃/反射，最大误差约 6.97e-11 mm。XY 控制中，强光焦度、非空气、偏轴斜光线仍有最大约 1.21e-7 mm 的相位差，保留 Difference；方向和坐标已对齐。首轮未载入现代扩展系数的原生控制和受沙箱 IPC 限制的失败捕获均保留，不计精度通过。

172 项定向回归通过，默认 Release App 构建零警告、零错误，测试、App 与冻结运行器 Core 哈希相同：21430f056a9ecb3ae0f765e553c3092ff153e0032c9b1538b4735db2d089b578。剩余为双折射相关 208 项和 Grid GRIN 29 项。上一批网格一阶量的实际差异仍开放，不能因本批能力完成而关闭。

[逐请求结果](../validation/rechecks/2026-10-31/model-capabilities-stage4/results.json)、[能力进度](../validation/rechecks/2026-10-31/model-capabilities-stage4/capability-progress.json)、[直接原生控制](../validation/rechecks/2026-10-31/model-capabilities-stage4/native-controls.json)。原生后台独立实例截图关闭，不使用用户桌面。定义依据：[Binary 3](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Binary_3.html)、[Paraxial XY](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Paraxial_XY.html)。
