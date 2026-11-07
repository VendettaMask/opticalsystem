# Grid Sag 单光线和一阶量复验

以同步后的 `d5a2a304` 为源码基线，沿用 95 支菜单镜头矩阵的原请求、采样和每项容差。原生参考为冻结的 OpticStudio 2026 R1 捕获。

## 95 支全量复验

3,024 项原请求中复算 2,904 项。结果为 **2,402 通过、38 接近、77 数值差异、437 不可比、70 求值错误**。本轮六项 Grid Sag 请求由不可比转为通过，原通过项全部保持。核验了 30,700 个原输入与参考文件，以及原请求指纹和比较策略哈希。独立固定光线为 143 组通过、108 组缺有效参考。[逐镜头结果](BY_LENS.md) · [622 项剩余请求](remaining-requests.csv) · [判定变化](verdict-changes.json) · [验收摘要](summary.json)

## 修复与定向结果

| 原请求 | 分析 | 结果 |
| --- | --- | --- |
| 1131、1153 | 一阶量 | 两配置均通过。Core 的光焦度为零，归一化后双方的有效焦距和 F 数均为无穷状态。 |
| 1132、1149 | 单光线，配置 1 | 省略第 2 面，完整比较第 1、3、4 面。11 列的最大误差均为 0。 |
| 1154、1171 | 单光线，配置 2 | 省略第 3 面，完整比较第 1、2、4 面。11 列的最大误差均为 0。 |

ZMX 导入和快照保留 `IGNR` 的明确标记。报告省略被忽略面，保持其余面号，并把跨过参考面的距离计入下一段。比较器要求原生行序与该配置的完整有效面集合一致，也核对反射符号。缺少标记、有效面缺行、面序不一致或同时需要推断折叠坐标断点时，保留不可比。[OpticStudio IGNR 定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Multi_Configuration_Operands.html)

网格矢高本身不提供零光焦度证据。仅在冻结处方逐行确认同一环境介质、无镀膜、网格后间距为零，并且 EFFL、EPDI 与 F 数相互一致时，将原生 `±1e10 mm` 饱和值解释为无穷焦距。带玻璃、反射或镀膜的网格继续要求独立参考。

Grid Sag Example 的 44 项原请求为 28 通过、10 不可比、6 求值错误。209 项 Core 与工作台测试、22 项比较工具测试通过。Release App 零警告、零错误，其 Core 与定向测试、全量复算的 SHA-256 均为 `944c43e5ca35a1b1a90ddfcc4310446abd815073068a0e8b7551cab7498fffde`。[定向数值账目](focused-results.json) · [Core 与工作台回执](menu-precision-targeted-final.trx) · [比较工具回执](menu-precision-comparison-final.trx)

## 仍需解决的求值问题

| 原请求 | 分析 | 当前证据 |
| --- | --- | --- |
| 1137、1159 | 二维 FFT PSF | 原生捕获发出无效结果诊断，Core 在原设置下无法建立有足够点数的二维瞳孔网格。 |
| 1139、1161 | FFT MTF | 原请求标记为有焦并用 cycles/mm，出射光线没有可用的有焦频率标度。冻结参考仍给出有限曲线，适用定义和收敛有待核实。 |
| 1148、1170 | 场曲和畸变 | 零光焦度且视场范围为零，无法建立非奇异畸变映射。冻结表的焦距为 `1e9 mm`，整列位移和场角为零，缺少有限焦面依据。 |

该阶段仍按原档位、原单位和原判定记录这六项。物理标度说明可参照 [FFT MTF](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_MTF.html) 与 [场曲和畸变](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Field_Curvature_and_Distortion.html)。
