# Sample Spectrometer 参考球出瞳平面修复

Sample Spectrometer 的两处超容差来自非共轴光阑方向笔的轴向定位。真实方向笔在像面 X、Y 都有变化。旧球面用 Y 分量的交点，得到 0.092907754976 mm。新球面用同一方向笔的两个横向分量求最窄截面，得到 0.092751627120 mm。独立原生 EXPP 为 0.092752357417 mm，且用两场三波长的实际像面位置、方向和相位光程逐点反算得到同一原生平面。产品计算采用真实旁基光线与本地光学处方。 [相位分解](phase-split.json) [原生 EXPP 与光程读数](spectrometer-controls.zip) [OpticStudio 出瞳位置](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Exit_Pupil_Position.html)

同处方六组逐面 OPTH 含光栅相位。它与实际几何光程加处方光栅相位的最大差为 4.01×10⁻¹⁵ mm。修复保留各波长的真实主光线球心，并保留标量出瞳报告及 MEMS 不连续方向笔的既有定义。 [OpticStudio OPTH](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Constraints_on_Real_Ray_Data.html) [OpticStudio 球面参考](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Exit_Pupil.html)

| 请求 | 场 / 波长 | 弧矢坐标 | 修复前光程差误差 | 修复后光程差误差 | 原逐点容差 |
| --- | --- | ---: | ---: | ---: | ---: |
| #458 | 2 / 0.400 µm | −0.30 | 4.378 nm | 0.0206 nm | 0.00562727 波 |
| #466 | 2 / 0.700 µm | −0.30 | 4.413 nm | 0.0208 nm | 0.00324667 波 |

最终 Core 重新执行原 22 条 OPD。物理通光掩码一致，1,593 个原物理节点的曲线均通过，28 个从嵌套原生图表补回的节点均满足原逐点阈值，最大曲线 NRMSE 为 8.45×10⁻⁶。另在两场、三个波长的每侧 160 光线控制中逐点核对 2,514 个原生实际报告节点，全部通过原逐点阈值。两种覆盖分别保留。 [22 条逐项结果](recheck-results.json) [两场三波长报告值](six-curves.json) [原补采和批量语义](../opd-exact-pupil-reference/README.md)

`Expand-Evidence.ps1` 根据 [清单](manifest.json) 将本次原始数据展开到空工作目录。另用上一阶段 `opd-exact-pupil-reference/Expand-Evidence.ps1` 恢复原补采原始数据。`Analyze.cjs` 和 `Compare-Product.cjs` 分别复算相位分解和六组曲线报告值，以这两个展开目录作为参数。重跑 22 项时，将本次展开目录下的 `replay/current-core` 复制到上一阶段展开目录，再编译 `verify/verify.csproj`。`ComparisonBin` 指向本次 Core 和比较程序集所在目录，验证器参数依次为合并后的原始数据工作目录和 `fan-physical-coverage` 证据目录。
