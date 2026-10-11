# Huygens 与 FFT 包围能量原设置残差修复

2026-10-11：用户确认 Zemax 就在本机，已经实际连接 `D:\Program Files\ANSYS Inc\v261\Zemax OpticStudio`，核实 **26.1.0 / build 260127 / EnterpriseEdition / validLicense=true** 并完成补采。此前“本机不能运行、必须使用另一台电脑”的判断已撤销。

原 MS-L7、原捕获请求和比较容差保持不变。通过正式 `WorkbenchRuntime`、JSON 序列化边界和原 `AnalysisComparisonRegistry` 重算，原 N01/N02 的三个指标均为 **Pass**：

| 原指标 | 修复前 NRMSE | 修复后 NRMSE | 修复后最大绝对差 | 分类 |
| --- | ---: | ---: | ---: | --- |
| N01 Huygens PSF X 截线 | 0.0037256670590166724 | 0.00000046272612713387683 | 0.00000046127337582912986 | Pass |
| N02 FFT DEE 理想曲线 | 0.013875784968407625 | 0.0001049654093689442 | 0.00040911213538841196 | Pass |
| N02 FFT DEE 实际曲线 | 0.011173141548632002 | 0.0002421804838391122 | 0.0007154387307732879 | Pass |

NRMSE 按原注册表的动态范围归一化，不是逐点相对误差。诊断工具中另有相对 L2，不能将其替代 NRMSE。N01 仍为波长 1、视场 1、瞳孔/像面 32、像间距 0.25 µm、偏振关闭；N02 仍为波长 1、视场 1、瞳孔 32、内部像面 64、质心参考、距离 5 µm、401 个输出点。没有用更高采样替代原请求。

## Huygens：轴上标量平面传播

原生 Auto 与 Planar 复场对照、等权光瞳反演及独立 Gaussian 控制确认：32 档平面传播使用 665 个内部光瞳节点，FFT 的 709 个节点不能直接代用。共享波前结果现保留完整像方方向 LMN，平面传播使用原 OPD、像方折射率和完整方向计算相位；振幅使用发射照明，保留渐变及截断。无偏振时不混入需要偏振追迹的镀膜/材料衰减。

平面单元的有限支撑采用矩形单元的 sinc 变换；其首零尺度由近轴出瞳几何、波长、像方折射率和光瞳间隔数给出。没有逐点读取原生结果、对镜头文件名分支或运行时拟合系数。Auto 的平面条件为请求像面范围的曲率光程不超过四分之一波长，包括质心偏移。

**当前启用范围为轴上、有焦、无偏振的平面条件。** 离轴需要投影单元的独立认证。全量回归中直接将轴上单元推广到离轴会使 Huygens MTF 预算退步，最终实现保留离轴的既有一般传播路径。球面、偏振、无焦与复杂三维出瞳不能据本轮结果宣称全面原生等价。

新独立原生二维证据包含 MS-L7 均匀照明及 Gaussian 4/32/512、Cooke、Tessar，共六个案例，每例比较全部 1024 个强度。回归判据为最大绝对差小于 1e-5、相对 L2 小于 5e-5，保存在 [Huygens 新捕获](../validation/zemax/2026-r1/huygens-integration-2026-10-11/README.md)。这是六案例受限验证，不是多视场/全波长认证。

## FFT DEE：内部网格与累计曲线

原生小范围 X 狭缝曲线的分段斜率揭示像元距离分箱：半像元边界为 `pitch/2 ± centroid`。32 档原生 pitch 为约 0.10588469 µm，Core 由自身几何计算为约 0.10581905 µm；没有将原生 pitch 或质心作为运行时补偿。

32/64/128 独立采样控制支持内部 Fourier 光瞳间隔伸缩 `sqrt(N/4 - 1)`、像面大小 `2N`。累计使用 `clamp((radius-distance)/pitch + 0.5, 0, 1)`；输出先形成 N 个累计节点，原点固定零，首节点由第二节点的径向面积或狭缝长度极限给出，再自然三次样条到请求输出点。强度混合使用共同物理坐标、稳定波长权重与正 Nyquist 节点。

这些内部约定是**从原生控制推断并独立复验**的；公开帮助没有说明私有伸缩或分箱公式。官方说明仅支持 FFT DEE 的光瞳采样、参考点与采样不足警告等公开语义。PSF 显示窗的矩形面积积分虽有独立解析验证，不能因此等同于原生 DEE 显示。

共享 Core 增加 `FftCumulativeEnergyGrid`；默认 DEE 的有焦、32 及以上二次幂采样采用该路径，并发布 `Method`、`EffectiveImageSampling`、`CumulativeKnotCount`。小采样/非二次幂/无焦沿用既有像元面积路径。构造器另提供显式 `DiffractionEnergyIntegrationConvention.PsfPixelArea`，用于物理像元面积分析；DENC/DENF 操作数仍采用其有限窗物理定义。操作数与此显式面积分析的单色、复色、顶点及平移一致性继续严格验证，不能将 DEE 绘图样条当作操作数函数。操作数原生认证范围没有因本轮绘图 Pass 自动扩大。

新 DEE 回归涵盖 32/64/128、范围 2.5/5/10 µm 的理想/实际曲线，以及三个独立 X 狭缝小范围控制。保留原 NRMSE 0.003 判据，另有四个独立孤立像元的线性分箱解析检查，共 17 个案例，见 [DEE 新捕获](../validation/zemax/2026-r1/fft-energy-integration-2026-10-11/README.md)。123456 主基准原 0.02 RMS 预算不变。

## 用户范围调整与保留记录

2026-10-11 用户明确排除独立 Cooke、Tessar 后续工作。这两项移出专项修复、补采和验收待办，已有捕获与通过的回归保留。原 MS-L7 N01/N02 三条曲线的 Pass 结论不变。

- 独立 Cooke 的 32 档 DEE 历史测量为相对 L2 约 0.00755（理想）和 0.03835（实际），保留为范围外记录。
- Tessar 的 32 档短波 DEE 空曲线、采样不足提示及其他波长警告保留为范围外记录。

## 尚未关闭的边界

- Huygens 离轴、球面、偏振、复色与复杂出瞳，以及 DEE 更广镜头、渐变和多视场边界仍需单独原生认证。
- 六镜头 132 项完整矩阵、实验室、安装包及人工桌面本轮未重验；本次三条 Pass 不重写历史矩阵分类，也不等于整个商业软件发布门禁完成。

## 验证与保存

默认 Debug/Release 完整构建零警告、零错误；正式完整 Release **4639/4639**、完整比较工具 Release **181/181**、Debug 相关 **283/283** 通过，零失败、零跳过，集合重叠不相加。二进制哈希、TRX 身份与次数比较及冻结完整性结果写入[同名 JSON](HUYGENS_FFT_ENERGY_REPAIR_2026-10-11.json)。旧失败/中止运行未计为通过；保留其记录并说明修复原因。新增正式 23 个案例，比较工具新增一个原 DEE 双曲线精度断言；原 Huygens 截线测试保持身份并升级为严格 Pass。

原 2668 份冻结资产的工作区和 Git 原字节检查继续固定原聚合 SHA `3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10`。新捕获独立保存，旧参考、旧账本和旧诊断保留历史身份。诊断 Python 只用于离线反演，不进入产品工程或运行时；正式计算保持纯 C#/.NET。

公开语义来源：[Huygens PSF](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26102/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Huygens_PSF.html)、[Diffraction enclosed energy](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Diffraction_enclosed_energy.html)、[Wavefront sampling](https://optics.ansys.com/hc/en-us/articles/42661791331859-What-does-the-sampling-correspond-to-in-wavefront-based-calculations)。私有内部网格推断见本轮捕获和诊断，不将其写成官方规格或通用默认设置。
