# 菜单示例模型能力补齐第五批，2026-11-01

> 后续验收：本批 27 项不可比当前为 25 项通过、2 项参考收敛阻塞，详见[中文分类交接](EXAMPLE_ACCURACY_HANDOFF_2026-11-01.md)。本批逐请求结果中，晶体方块的能量差异属于配置 1，一阶差异属于配置 2；下文历史“配置 2 的一阶/能量”笼统表述按逐请求身份区分，不能当成同一配置。原始结果不改写。

**最后 237 项解除模型阻塞；用户指定的 751 项模型阻塞为 0，最初 1,098 项模型阻塞也为 0。** 这项结论来自当前冻结 Core 对全部菜单源文件重新导入后的 114 个可用配置清单，不是累计推算。原有一个未导入的椭圆锥瞳孔示例不在这 1,098 项模型阻塞集合内，仍保留原记录。本批没有认证全部 95 支示例的分析精度。

最后 5 支示例、9 个配置、238 项原请求重算：**192 Pass、3 Close、16 Difference、27 Incomparable、0 Error**。其中 237 项是上批仍被模型阻塞的原请求，逐项结果见 capability-progress.json。原请求、比较配置和门槛不变，Close 不算通过。原 95 支总账和历史差异不覆写；没有把局部控制升级为完整分析认证。

产品保持纯 C#/.NET 和共享 Core。新增能力：

- 均匀单轴晶体 BIRE__IN/BIRE_OUT 的模式 0/1、平面和标准圆锥基底；解析真实普通/非常光色散，保留入口局部晶轴并转换为每条路径的全局晶轴。界面守恒切向波矢，光线沿能流方向，体光程使用波矢在能流上的投影。反射保留原晶体坐标，缺少晶体、色散或未验证的偏振/膜层组合明确失败。模式 2/3、渐变晶轴和晶体 Jones 运输仍不宣称支持。
- GRINSU11 的完整 Schott GGD 网格、UTF-8/UTF-16、坐标/单位、常数偏置和六个色散项；有限定义域、有界数据量、真实连续积分和完整 STAROPT 保存。相邻资产优先，官方示例数字数据按原始 SHA-256 内置，保存后不依赖本机 Zemax 文件。其他色散公式和外部 GRIN 文本导出仍明确拒绝，不能压成均匀玻璃。
- 网格入口/独立折射率查询采用经多项式和两波长控制验证的局部四节点自然三次样条；体内使用全网格自然样条的值及一致梯度。两条定义明确分开，未改写现有 GRIN 分布。源数据先在节点计算色散。每个不可变网格最多缓存四个精确波长，不跨输入状态复用。
- 入口瞳孔仅验证光阑之前的相关链路；下游非对称介质不再阻塞已明确的入口孔径。网格一阶量提取声明轴的高斯线性部分，保留梯度项，但与原生焦距仍有差异；独立 X/coupled 高斯 API 尚未认证。真实光线不会丢掉线性梯度。

215 项定向回归通过，默认 Release App 构建零警告、零错误，测试、App 与冻结运行器 Core 哈希相同：c880e5ed4b780a767cbde39e2d3d3e78d2f927d702c46b97669de75af59b27e6。验证始终后台执行，没有桌面控制、截图、前台验收、安装或发布。

双折射六组直接原生控制覆盖两波长、两分支、倾斜/轴向晶轴及玻璃环境，最大误差小于 1.1e-14。网格示例的原生最大步长从 1、0.1、0.02、0.005 降至 0.0005、0.0001、0.00002；最细的三个代表光线共 60 个独立量全部满足原有显式光线界限（另三行仅作光程参考）。其最大绝对光程差约 9.87e-6 mm，没有把该内部点控制替代为全部示例曲线通过。粗步长原生记录均保留。自定义色散网格的界面指数已对齐，体传播仍有实际差异。

当前精度后续工作仍包括：Grid GRIN 一阶定义及自定义网格体传播；晶体方块配置 2 的一阶/能量结果；旧批次 Grid Sag 一阶量和强非空气 Paraxial XY 相位差异；27 项不可比请求的直接原生参考。旧网格示例分析参考的收敛未逐项确认，因此其旧 Difference 不直接等同于已证实的当前计算错误，也不升级为 Pass。库外新模式、偏振和未验证格式不纳入本批完成数。

[逐镜头表](../validation/rechecks/2026-11-01/model-capabilities-stage5/BY_LENS.md)、[238 项结果](../validation/rechecks/2026-11-01/model-capabilities-stage5/results.json)、[原 751 项当前模型清单](../validation/rechecks/2026-11-01/model-capabilities-stage5/capability-progress-original751.json)、[精度后续项](../validation/rechecks/2026-11-01/model-capabilities-stage5/accuracy-followup.json)。历史批次结果由不同 Core 产生，未合并成当前 Core 的精度通过率。

定义依据：[单轴晶体](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Birefringent_In_and_Birefringent_Out.html)、[Grid Gradient 文件与体积分](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Grid_Gradient.html)。网格插值具体路径以独立原生控制为证据，未加载原生 DLL 参与产品求解。
