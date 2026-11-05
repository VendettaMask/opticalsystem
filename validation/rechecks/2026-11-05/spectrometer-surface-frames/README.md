# Sample Spectrometer 逐面坐标与精度复核

95 支菜单示例中的 Sample Spectrometer 在相同处方、相同分析请求及原有容差下，30 项复算得到 **14 Pass、4 Difference、12 Incomparable、0 Error**。原 11 项 `Difference` 全部通过。原相对照度 `Error` 和视场 RMS `Close` 也通过。四个 Huygens 条目在光路恢复后能够比较，仍显示差异。

第 1 面狭缝的命中、方向和路径已与冻结捕获一致。第一处分歧发生在第 2 面球面反射镜，早于第 3 面光栅。处方用 `SCBD` 为三个反射面定义面前和面后偏心与倾斜。第 2 面前的 X 轴 +8° 改变命中位置及反射方向，其面后拾取定义下一个间隔轴。第 3 面前的 X 轴 −16° 和 Z 轴 +90° 将光栅刻线放入实际局部坐标。独立球面交点、镜面反射和坐标变换复现了冻结报告 0438 在第 2 面的 Y 命中 `0.39098314522 mm`、第 3 面的 X 命中 `0.35713564222 mm` 和衍射后局部 Y 方向余弦 `0.055`。实现保留反射、光栅模型和原面的数量及编号，按面后的坐标轴推进厚度。[Ansys 面前与面后变换定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Tilt_Decenter.html)、[Ansys 后置拾取编码](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/SETSURFACEPROPERTY_SURP.html)。

| 通过项目 | 范围 | 修复后的误差 |
| --- | --- | ---: |
| 单光线追迹 | 4 条光线、20 个逐面命中、每面 11 列 | 最大坐标差 `4.791×10⁻¹² mm`、方向余弦差 `4.709×10⁻¹¹`、路径差 `4.886×10⁻¹¹ mm` |
| 点列 | 4 项原 RMS/GEO 半径 | 最大绝对差 `9.238×10⁻¹³ µm` |
| 波前 | 1,153 个有效点 | NRMSE `0.00133354`，原门槛 `0.003` |
| 几何环围能量 | 两条曲线各 630 点 | 最大绝对差 `5.378×10⁻¹⁷` |
| 场曲与畸变 | 三条曲线各 102 点 | 场曲 NRMSE 最大 `1.576×10⁻⁵`，畸变最大差 `8.379×10⁻⁷` 个百分点 |
| 相对照度、RMS 随视场 | 原照度 22 点、RMS 16 点 | NRMSE 分别为 `8.242×10⁻⁷`、`1.389×10⁻¹⁵` |

验收还包括含复合角和非零偏心的独立解析控制、长度单位换算、当前面反向拾取与前一面拾取。STAROPT 保存并回读全部六面的姿态。遇到含非零 SCBD 的处方，现有不支持写出这类面姿态的文本导出会明确拒绝。扫描 95 个原处方仅找到这个非零案例。另一处方 `Example 6, tilted image plane.ZMX` 含一个全零 SCBD，其两个配置重导后仍逐字节匹配原规范化快照。

8 项 Ray Fan／OPD 仍有掩码冲突。冻结原生图在 sagittal `Px = −0.30` 为空，`+0.30` 有值。按 1 mm 有限物距和 `NA = 0.08` 独立求得两个狭缝交点 `±0.024077170617 mm`，均在 ±0.025 mm 物理狭缝内，实际两条光线均到达像面。`Px = ±0.35` 在第 1 面截断，符合独立交点 `±0.028090032387 mm`。原掩码和完整域比较继续适用。其余四个不可比项是 FFT 原生捕获报欠采样或失败。四个残余 Huygens 数值差异分别来自二维 PSF、MTF、PSF 截面及 Through Focus MTF。

冻结参考使用原 2026 R1 语料中的 0437–0466，保留原归一化、单位、场点、波长、口径和阈值。专项 Release Core、Application 和诊断程序构建零警告、零错误。[请求、参考哈希、逐项结果和独立控制](results.json)。

默认 Release 中两条新增回归与既有复合角坐标断点反向控制均通过。同次额外运行的旧浮动光阑坐标断点测试停在面数断言，预期 4 面、实际 5 面。11-04 已有程序集也复现该断言失败。[既有失败复现](../tolerance-precision-integration/prior-import-reproduction.trx)。
