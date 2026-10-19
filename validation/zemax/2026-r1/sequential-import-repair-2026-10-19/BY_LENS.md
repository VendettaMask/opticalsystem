# 14 镜头导入修复逐镜头复验

日期：2026-10-19。14 个名称、21 个配置均完成导入与快照准备；原两类导入错误清零。数值分析并未全部通过。分析计数依次为：通过 / 接近 / 差异 / 不可比 / 错误。原容差、原生数据和原文件保持。

| 镜头 | Core/原生配置 | 导入/快照 | 分析计数 | 具体残留问题 |
|---|---:|---|---:|---|
| Afocal/Binocular_System.ZMX | 2/2 | 成功 | 0/0/0/60/0 | 未支持：Zemax TYPE NONSEQCO；现有分析适配器限制毫米有焦像空间 |
| Birefringent prisms/Birefringent cube.zmx | 2/2 | 成功 | 0/0/0/0/60 | 未支持：Zemax TYPE BIRE__IN；未支持：Zemax TYPE BIRE_OUT；未支持：Zemax TYPE PARAXIAL |
| Birefringent prisms/Rochon prism.zmx | 2/2 | 成功 | 0/0/0/0/60 | 未支持：Zemax TYPE BIRE__IN；未支持：Zemax TYPE BIRE_OUT；未支持：Zemax TYPE PARAXIAL |
| Birefringent prisms/Senarmont prism.zmx | 2/2 | 成功 | 0/0/0/0/44 | 未支持：Zemax TYPE BIRE__IN；未支持：Zemax TYPE BIRE_OUT；未支持：Zemax TYPE PARAXIAL |
| Birefringent prisms/Wollaston prism.zmx | 2/2 | 成功 | 0/0/0/0/44 | 未支持：Zemax TYPE BIRE__IN；未支持：Zemax TYPE BIRE_OUT；未支持：Zemax TYPE PARAXIAL |
| Diffractive components/Achromatic singlet.zmx | 1/1 | 成功 | 0/0/0/0/30 | 未支持：Zemax TYPE BINARY_2 |
| Miscellaneous/Cylinder.zmx | 1/1 | 成功 | 16/0/1/3/2 | Field Curvature and Distortion×1 |
| Miscellaneous/Grid Sag Example.zmx | 2/2 | 成功 | 0/0/0/0/44 | 未支持：Zemax TYPE GRID_SAG |
| Miscellaneous/Grid sag surface.zmx | 1/1 | 成功 | 0/0/0/0/22 | 未支持：Zemax TYPE GRID_SAG |
| Miscellaneous/MEMS DLL Sample.ZMX | 1/1 | 成功 | 0/0/0/0/22 | 未支持：Zemax TYPE USERSURF |
| Miscellaneous/Toroid.zmx | 1/1 | 成功 | 7/0/4/9/2 | Field Curvature and Distortion×1；Optical Path Difference×2；Wavefront×1 |
| Optical testing/Mach Zender Interferometer.zmx | 2/2 | 成功 | 0/0/0/0/44 | 未支持：Zemax TYPE TILTSURF；未支持：Zemax TYPE FZERNSAG |
| Telescopes/Yolo.zmx | 1/1 | 成功 | 8/2/4/7/1 | Encircled Energy×1；Field Curvature and Distortion×1；First Order×1；Wavefront×1；原生 REAX/REAY 独立控制 20 项仍有差异，最大 0.3461933632 mm |
| Tilted systems & prisms/OAP using Chebyshev Polynomial surface.zmx | 1/1 | 成功 | 0/0/0/0/22 | 未支持：Zemax TYPE CHEBYSHV |

全部 518 项分析请求已完成尝试：31 通过、2 接近、9 差异、79 不可比、397 错误。主要错误来自尚未实现的特殊面型等模型能力；原生 6 项采集失败另存原始错误，不算精度通过。35 组逐面光线为 1 通过、2 不可比、32 错误。

Yolo 原始面 9 的光线位置引用保留为 Core 面 4 的独立透明平面，原像面 13 对应 Core 面 6；没有将坐标断点改指相邻曲面。REAX/REAY/REAR 已改为目标面局部坐标，原对象零面含义保持。20 个经过原生批量光线检查的 Yolo 控制均有效，但仍为 Difference；最大差从修复前约 311.4214 mm 降至 0.3461933632 mm，未达到原门槛。

独立倾斜参考平面控制中，只有 2 个原生有效请求，18 个原生追迹失败的请求记为不可比，禁止把失败返回的零值当成参考。2 个有效请求最大差 3.04656e-5 mm，仍未通过原限值；保留为后续参考平面/追迹验证问题。

既有 165 份有效精度比较均通过原门槛，输入、原生捕获、请求指纹及逐项误差与前次完全一致。冻结资产在 HEAD 与工作树均为 2668/2668 保持。原目录全部 295 文件的最终哈希核验另列最终验证账目。

详细数据：[分析 CSV](analysis-matrix.csv)、[逐面光线](ray-results.json)、[Yolo 原生控制](reference-controls/yolo-native-qualified.json)、[Yolo 数值对比](reference-controls/yolo-comparison-qualified.json)、[旧精度保持账目](formal-preservation.json)。
