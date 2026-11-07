# Zemax 导入回归复核

历史 0.5.8 夹具把 80 mm 环曲面旋转半径放在了 `PARM 2`。在 Zemax 顺序环曲面中，旋转半径使用 `PARM 1`，`PARM 2` 是二次非球面系数。保留冻结文件及原文件的拒绝测试。几何与 BOM-less UTF-16 测试使用内存中更正后的原处方，保留原数值约束，并按 YZ 母线的圆周旋转单独计算环曲面矢高。 [Ansys 顺序环曲面定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Toroidal.html)

未映射几何的只读保留测试使用未知面型。导出拒绝测试覆盖尚无无损映射的多边形口径。坐标断点测试验证具有非零厚度的透明参考面保留玻璃介质，并核对镜面和像面的空间位置。

180 项导入、口径与精度定向回归全部通过，包含前一轮复现的 5 个失败项。本次修订测试契约和测试输入，95 支菜单镜头的最近一次数值结果沿用已发布的复验。 [测试结果](core-import-release.trx) · [95 支精度复验](../fnumber-etalon-mirror-zoom/README.md)
