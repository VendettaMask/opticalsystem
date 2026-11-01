# Speos 二进制与 Zemax Tabular BSDF 导入

后续阶段：[Speos v11、原生镜面增强/White Specular 与校准 XYZ](SPEOS_V11_XYZ_2026-10-31.md)。本报告的测试数量、来源与未实现边界保留当时冻结身份。

2026-10-31。本阶段扩展共享 C# Core 的 BSDF 导入、积分和采样，接入现有 NSC→光谱 W/像元→RAW/ISP。没有调用 Speos/Zemax 作为产品计算后端，未执行原生桌面操作。

## 已实现范围

| 格式 | 当前入口 |
| --- | --- |
| Speos `.brdf v8.0` 二进制 | 已核验的 archive-3 小端/zlib 布局；光谱 R/T、相对 BSDF 或投影 BSDF 值 |
| Speos `.anisotropicbsdf v7.0/v9.0` 二进制 | 已核验的 archive-3 小端/分块 LZ4 布局；材料旋转、入射角、R/T 光谱及方向表 |
| Zemax `.BSDF` | Source Measured；Monochrome；BRDF 或 BTDF；PlaneSymmetrical、Asymmetrical、Asymmetrical4D |
| 原有入口 | CSV v1/v2、Speos v8 文本 BSDF 值模式，兼容与原始验证范围保留 |

Speos 必须匹配已核验的存档签名和布局。v11 二进制及未知附加参数、启用的 Specular Interpolation Enhancement / White Specular 均明确拒绝，避免忽略物理选项后给出错误结果。仅关闭状态下解析、校验其辅助预计算表和参考信息，并保存辅助字节 SHA-256；本地采样重新由角表积分建立。原始文件 SHA-256、转换说明与计算数据摘要一同保存。

工作区最初只有自有合成文本样例；本次自行查找了本机 Ansys 2026 R1 OpticalLibraries 的二进制材料和安装的 Zemax ScatterData，并取得官方 PySpeos 固定提交中的 `R_test.anisotropicbsdf`。它作为不变的公开二进制回归夹具保存，见[来源及 MIT 许可](../tests/OpticalSystemDesign.Tests/Fixtures/speos-r-test-v9.NOTICE.txt)。安装目录中的其他厂商材料只在本机读取，未复制入产品或样例目录。此过程核对文件结构，不构成材料真实性或商业软件数值认证。

## 操作与物理声明

对象属性的“导入 BSDF 文件”接受上述扩展名；商业文件仍须选择仅正面、仅背面或正反相同（显式声明）。有 BTDF 使用折射交互、声明实际前后介质，并提供两侧数据。没有 BTDF 的模型用于不透明反射/吸收界面。

Zemax 单色文件本身没有测量波长，而且[官方资料](https://optics.ansys.com/hc/en-us/articles/43071107687699-Zemax-Tabular-BSDF-data-file-format)指出 Monochrome 可代表光度 Y。用于绝对 QE 的 RAW 链路前，必须输入测量波长，并明确声明**原数据本来就是辐射度 BSDF**。默认不勾选；导入器不把未知波长、XYZ 或光度 Y 自动转换成辐射度。测量单位声明不能替代真实测量和单位核对。冻结模型只覆盖所声明的一个波长，不外推成无色差光谱材料。

Zemax BRDF 的 AngleOfIncidence 是镜面反射角；BTDF 的同名轴实际是折射后的镜面方向角。BTDF 输入正面入射/出射折射率，通过 `sin(theta_i)=(n_t/n_i)sin(theta_t)` 建立入射轴；背面交换介质。不可达的角度拒绝导入，追迹时实际介质折射率比不匹配也拒绝。该变换只用于解释文件角坐标，散射后按测量方向继续传播，不另做一次 Snell/Fresnel。

单个 Zemax 文件描述 BRDF 或 BTDF；导入会替换对象已有 BSDF。当前没有将两个独立 Zemax R/T 文件合并成一个对象模型的入口。Speos 文件可包含反射和透射两瓣。所有入口沿用本产品的绝对界面功率约定：测量 R/T 或 TIS 定义本次散射功率比例，其余吸收，不额外乘镀膜或镜面反射增益。

固定轴继续使用对象局部 XYZ，并随完整参考坐标链旋转；法线平行时使用对象局部次轴。Speos 只有一个 0° 材料旋转样本时按各向同性处理；多个旋转使用固定材料轴。Zemax SampleRotation 对应入射平面法线与局部 +X 的关系；本地入射离面方向方位为 `phi_i=wrap(90°−SampleRotation)`，不能套用 Speos 的 180° 偏移。

## 积分、采样与能量

Speos 二进制的类型位为 0 时，存储量是相对 `f|cos(theta_o)|`，不直接当作 BRDF，也不先逐节点除余弦再用旧插值。共享模型保留这一数量类型，在 cosθ/φ 网格中插值、积分和采样投影值；Evaluate 才恢复 f。这样避免掠射端点除零，也不会重复乘余弦。光谱参考角处的缩放继续与运行时相同的 cosθ_i 插值一致，保留不同入射角/材料旋转的相对幅值。

Zemax 保留原始镜面中心网格，径向角可到 180°，不重采样为法线中心规则图，因此不以角度栅格展宽窄峰。以 `mu=cos(alpha)` 表示径向角，在单元内对 mu/phi 双线性插值原相对 BSDF；在镜面中心基底中，朝实际出射半球的余弦为：

`c = cos(theta_specular)*mu + sin(theta_specular)*sqrt(1−mu²)*cos(phi)`。

仅积分 `max(c,0)`。phi 方向按真实半球交界分段解析积分；径向积分改用 alpha 与 sin(alpha) Jacobian，消除 mu=±1 的平方根端点奇异性，并采用有界自适应积分，未收敛则拒绝。采样依次选择输入切片和出射单元，再反演条件 CDF；PDF 对应整体插值分布。物理半球外与文件未测覆盖区域为零；不填孔洞、不外推。

每个文件切片按 TIS 归一化，输入切片按 cosθ_i/材料旋转凸混合。TIS、R/T 非负且总量不得超过 1；每条样本检验 `f|cosθ|/pdf` 等于模型总散射比例，余量进入吸收账本。相机继续只做一次光子换算、曝光/QE、噪声、ADC、ISP。

本地选择的是连续分布及功率的凸插值。它与 [OpticStudio 文档](https://optics.ansys.com/hc/en-us/articles/43071089490451-How-to-use-tabular-BSDF-data-to-define-the-surface-scattering-distribution)中的部分近邻/随机方向插值策略不同；文件格式支持和解析控制通过，不能据此宣称任意非节点输入与其数值相同。Speos 参考角的本地插值约定也继续明确保存。

## 保存与验证

新增 `StoredQuantity` 和 Zemax 原始角表/TIS/介质声明进入不可变模型与规范摘要，使用 `MeasuredBsdfGrid/3`。包含它们的 STAROPT 工程写 v11；旧商业文本/固定轴写 v10、原实测 v1 写 v9、Lambertian 写 v8、普通工程写 v7。低版本夹带新模型会拒绝。容器仍为 2，顺序光学快照仍为 6，见[唯一格式契约](STAROPT_FILE_FORMAT.md)。STARRDB 容器仍为 v1，协议头显式识别新模型；原 S/B/R/T 筛选和介质进出事件继续适用。

相机冻结完整计算模型、源摘要、数量类型、材料轴及全追迹采样统计。删除原文件、清空场景/数据库后，可由冻结 W/像元重放光电子、RAW 与 ISP；这不等于重新追迹 NSC。旧三个 V9 实验及上一阶段 grid/2 实验保留原指纹。

最终定向合并回归 **191/191**（新增 35 项）通过，默认 Release App/测试驱动零警告、零错误；44 个范围内源码/夹具和 9 个二进制指纹在末次运行前后无漂移。构建、源码/二进制指纹与离屏截图见[本阶段证据](../validation/rechecks/2026-10-31/binary-zemax-bsdf/README.md)。覆盖官方二进制样本参考角 R=0.6227、zlib/LZ4 长度/越界/损坏与活动选项拒绝、R/T 光谱、5° 镜面峰、85° 掠射半球、固定轴旋转、独立解析矩和探测器视因子、介质光程、STAROPT v11、数据库筛选、逐像元光子换算、冻结重放及真实控件离屏路径。分析控制沿用明确数值容差和六倍标准差，不是商业软件的通用精度门槛。

## 预算与剩余范围

保留 32 MiB 原文件、131072 原始/展开节点和 262144 文档总节点预算；二进制总解压预算 128 MiB。Speos 沿用波长 32、入射极角 64、材料旋转 65，输出极角 129/方位 257 的上限；部分安装库文件的波长/展开节点超过预算，明确拒绝，不自动降采样。Zemax 原表旋转≤50、入射角≤100、出射径向/方位各≤1000，仍受 TIS 总项≤6400 和总节点预算约束。

尚未实现 Speos v11/未知二进制布局、活动镜面插值增强/White Specular、未知文本版本、XYZ 光度到辐射度、独立 Zemax R/T 双文件合并、delta 镜面瓣、逐面覆盖、体散射、偏振、相干、MIS 和角度 QE。样例为自有辐射度合成控制，官方二进制仅作为格式回归；没有新增商业数值认证或安装包发布。

## 官方依据

- [Zemax Tabular BSDF 文件字段与角坐标](https://optics.ansys.com/hc/en-us/articles/43071107687699-Zemax-Tabular-BSDF-data-file-format)：文件层次、对称、TIS、单色/XYZ 和折射后方向角。
- [Zemax Tabular BSDF 的使用与插值](https://optics.ansys.com/hc/en-us/articles/43071089490451-How-to-use-tabular-BSDF-data-to-define-the-surface-scattering-distribution)：归一化和原软件插值边界；本地不叠加其额外镀膜系数。
- [Speos 格式和投影数量约定](https://optics.ansys.com/hc/en-us/articles/18384793374227-Speos-BRDF-BTDF-and-BSDF-Formats)、[官方 PySpeos 固定二进制夹具](https://github.com/ansys/pyspeos/blob/bc5f75644f568c6b1c9b9fa1b53d22d18170cf99/tests/assets/R_test.anisotropicbsdf)：公开说明只给文本布局，二进制结构另由实际样本核验。
- [LZ4 官方块格式](https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md)：分块解码的字面量、重叠匹配、长度与越界约束。解码器为独立 C# 实现。
