# 米制处方补采与不可比清单

冻结矩阵中的 Hubble 和 Image Simulation Example 4 均以 `UNIT METER` 声明长度单位。对照入口新增显式选项 `--normalize-native-length`，按 OpticStudio 的 Scale by Units 在内存中换算原生镜头。比较要求原单位、未保存输入的换算收据和毫米模型读回。默认行为和冻结矩阵保持原策略。 [OpticStudio 单位换算](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Scale_Lens.html)

两份原始源处方的字节哈希和分析配置均核对。原生未换算模型与毫米模型的面型、视场、波长一致。处方尺寸按 1000 倍换算，最大差为 3.64×10⁻¹¹ mm，入瞳直径由 2.4 m 读回为 2400 mm。44 条缺请求记录按原配置、分析、视场范围、波长和比较阈值补采。两条几何包围能量使用该分析已定义的全视场选择器。 [物理模型读回](model-scale-proof.json) [逐项设置、指标和历史索引](meter-comparison.json)

| 补采范围 | 通过 | 接近 | 不可比 |
| --- | ---: | ---: | ---: |
| Hubble | 16 | 1 | 5 |
| Image Simulation Example 4 | 16 | 1 | 5 |
| 合计 | 32 | 2 | 10 |

两条接近均为衍射包围能量。10 条不可比由 8 条裁光掩码差异和 2 条原生无效 FFT PSF 组成。32 条补充通过独立计数。第一次采集中的 7 条 Windows 长路径失败已在短路径使用完全相同的物理输入和设置补采。原生证据包中的 `index.json` 记录每条逻辑路径的原始内容哈希，同一内容只存一次。 [补采原生证据](native-numeric-evidence.zip) [统计与哈希](summary.json)

两份镜头的 8 条掩码差异另用 4 组独立原生显式光线验证，每组 83 条光线。内置曲线每条轴的漏点都在恢复通光的第一个采样点。8 条在独立显式全域和原图已报点两个范围均通过。光扇最大差为 4.54×10⁻⁹ µm，光程差最大差为 2.19×10⁻⁸ 波。独立通过与内置报告判定分开保留。 [光扇与光程差复验](meter-explicit-fan-results.json) [原生显式光线](meter-explicit-fan-native.zip)

对照器区分非有限曲线和光扇裁光掩码。原生空缓冲会在本地输出有效时记为参考不可比。定向验证覆盖单位换算收据、非有限值、原生诊断、光扇裁光和一阶饱和值，26 项通过。 [测试记录](comparison-contracts.trx)

普通 CLI 另行验证 Hubble 一阶和 #2605 的 RMS 随视场。前者使用显式换算入口，原生模型由米读回毫米，一阶比较通过。后者保留逐字段相同的冻结请求，原生重采仍报告无效并返回空缓冲，本地输出可用，参考判为不可比。 [CLI 冒烟记录](live-cli-smoke.json)

433 条冻结不可比逐项分类，包含缺少可执行请求、原生诊断、空数据、掩码差异、缺少专用容差或逐面弧长，以及缺少有限焦面等类型。 [冻结不可比逐项清单](frozen-incomparable-classification.csv)

## 既有参考的覆盖边界

- 独立无焦范围以已归档处方、快照、原生数据、请求和专项阈值重算，170 条为 162 通过、8 不可比。冻结矩阵中另 76 条缺请求项均有同源独立结果可对应，71 通过、5 不可比。73 条同视场与波长，3 条几何包围能量按该分析的全视场定义映射。5 条余项含 3 条相对照度缺原生参考，以及双筒两个配置的混合 NSC 无通用一阶等效。独立范围保留自己的采样、阈值和身份。 [76 条映射及重放身份](afocal-missing-request-coverage.json)
- 25 条空缓冲包含 20 条 FFT PSF 截线、4 条相对照度和 1 条 RMS 随视场。冻结捕获都缺少数值和文本。20 条配对二维 PSF 与截线使用同处方、配置、视场和波长，瞳采样分别为 64 和 32，二维报告也全部不可比。 [逐条空缓冲审计](empty-buffer-audit.json)
- 12 条无焦光扇和 3 条场曲与畸变缺少原策略所需的专用容差。用已有专项的角度和屈光度容差重比，15 条通过，原请求指纹与判定保留。 [独立策略复验](afocal-existing-policy.json)
- 原报告缺报弧长的 8 条单光线已有逐段原生截点和收敛对照。6 条来自 Selfoc 与 GRIN interconnect，2 条来自 Optical interconnect。冻结逐面整表和独立弧长仍分别统计。 [Selfoc 与 GRIN interconnect](../menu-folded-grin/arcs.json) [Optical interconnect](../optical-interconnect-reference/arcs.json)
- Grid Sag 的两条有焦 MTF 和两条场曲与畸变，以及 Etalon 的有焦 MTF、场曲与畸变和衍射能量，共 7 条缺少有限焦面的物理标度。Etalon 的独立无焦量仍保留原补采身份。MEMS #1241 的 `EFFL=-10¹⁰` 随 UserDefined 表面读回，原记录缺少足以认证零光焦度的独立正式一阶参考。 [Grid Sag 适用性](../../2026-11-06/grid-sag-zero-power/README.md) [Etalon 有焦和无焦对照](../fnumber-etalon-mirror-zoom/README.md)
