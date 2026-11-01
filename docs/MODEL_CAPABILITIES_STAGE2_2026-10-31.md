# 模型能力补齐第二批，2026-10-31

> 接续提示：本批剩余能力数属于当时版本；第五批已核验原模型阻塞清零。分析通过与能力实现分别验收，现状见[中文分类交接](EXAMPLE_ACCURACY_HANDOFF_2026-11-01.md)。

针对上一批剩余 751 项，本批 **342 项解除模型能力阻塞，409 项仍被剩余模型阻塞**。原 1,098 项累计解除 689 项；解除阻塞不等于精度通过。全部产品计算为纯 C#、共享 Core，原请求和门槛未修改，后台原生控制未使用桌面或截图。

相关 12 支示例、344 项原请求按同一冻结 Core 重算：**279 Pass、8 Close、20 Difference、35 Incomparable、2 Error**。Close 不计通过。两项 Error 为光谱仪和远心示例的相对照度瞳孔参考距离边界。原 95 支总账保持，没有将本批结果冒充全部 95 支收敛认证。

实现内容：

- PARAXIAL：独立理想平面作用，正确的折射率/空气焦距及零功率；接入 OPD 模式 1 的光束相位积分和模式 2 的无限共轭。主光线相位为零，逆向瞄准和积分均调用共享几何追迹，未收敛明确失败。模式 0/3、反射近轴面及 PARAX_XY 继续保留原不透明能力边界。
- DGRATING：局部 X 方向直线刻槽、每微米线密度、正负级次、折射/反射及曲面基底，方向和相位一起计算。
- BINARY_2：独立偶次非球面基底、最多 240 个归一化相位项；保留归一化半径、衍射级及真实波长单位，一阶传递包含衍射光焦度。现代 PARM 13+、XDAT 及配置参数均保留。
- HOLOGRM2：平面基底上会聚/发散构建光束、偏轴构建点、构建波长及级次，保留原生绝对相位常数；曲面及体全息模式未映射。
- IRREGULA：基准圆锥、球差/像散/彗差、归一化半径和方位角；临时偏心、依次局部 X/Y 倾斜及撤销，不旋转后续表面。净半口径编辑同步其归一化，完整几何可保存及往返。
- 共享波前和追迹的 OPD 汇总读取包含表面相位的光程，独立保留几何光程列；Jones 运输也计入本步表面相位。新增带明确单位与正光程约定的衍射交互，不重定义原 phase 类型的历史约定。缺少完整列的文本导出明确拒绝压平。

150 项针对性测试通过，默认 Release App 构建零警告、零错误，App 与冻结运行器 Core 哈希一致。新原生点控制覆盖有限/无限共轭、像差、非空气、正负级、多波长、平面/曲面、反射、单位转换和文件往返。近轴像差控制最大光程误差约 4.18e-8 mm；其余光栅、Binary 2、全息与不规则控制最大误差小于 3.7e-12。以第一物理面作差的 OPTH 参考行不计独立验证。另有 18 行原生一阶控制；其中 EFFL 请求的是 wave 0 主波长，不宣称捕获了两条独立波长的 EFFL。

剩余能力计数存在重叠：PARAXIAL 251、BIRE__IN/BIRE_OUT 各 208、GRID_SAG 64、BINARY_3 44、GRINSU11 29、PARAX_XY 21。不能求和。尚有 28 项 Close/Difference、35 项参考缺口和两项执行边界，不纳入本次模型能力完成数。首轮含 GLAS AIR 的退化瞳孔控制、模式 0/3 探索和不规则倾斜顺序修正前差异保留，不计通过。

定义依据：[原生近轴面](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Paraxial_sequential_surfaces_lens_data_editor.html)、[Binary 2](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Binary_2_sequential_surfaces_lens_data_editor.html)、[全息 2](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Hologram_2.html)、[不规则面](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Irregular.html)。刻槽模型另经本机原生直接控制核验，不依赖运行时 DLL。

[逐镜头表](../validation/rechecks/2026-10-31/model-capabilities-stage2/BY_LENS.md)、[逐请求结果](../validation/rechecks/2026-10-31/model-capabilities-stage2/results.json)、[751 项能力进度](../validation/rechecks/2026-10-31/model-capabilities-stage2/capability-progress.json)和[原生控制](../validation/rechecks/2026-10-31/model-capabilities-stage2/native-controls.json)。冻结 Core：4844c6afd442d8cb1056d9abdfac2905cf139d6e40842b689dcd83236274b13a。
