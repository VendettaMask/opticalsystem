原生参考格式：ZMX。ZOS 补充控制的 Core 输入为原生导出的 ZMX；不代表产品直接支持 ZOS 导入。

# Sequential 全目录逐镜头对比

日期：2026-10-19。所有源文件逐字节保持；保留原比较策略、采样及容差。原生数据为本机新采集，Core 由正式 Application 执行。

| 镜头 | Core/原生配置 | 导入错误 | Pass | Close | Difference | 不可比 | Error | 光线 P/D/I/E | ZMX/ZOS 模型读回 | 最大 NRMSE |
|---|---:|---:|---:|---:|---:|---:|---:|---|---|---:|
| Afocal/Binocular_System.ZMX | 2/2 | 0 | 0 | 0 | 0 | 60 | 0 | 0/0/0/6 | Difference | — |
| Birefringent prisms/Birefringent cube.zmx | 2/2 | 0 | 0 | 0 | 0 | 0 | 60 | 0/0/0/6 | Difference | — |
| Birefringent prisms/Rochon prism.zmx | 2/2 | 0 | 0 | 0 | 0 | 0 | 60 | 0/0/0/6 | Difference | — |
| Birefringent prisms/Senarmont prism.zmx | 2/2 | 0 | 0 | 0 | 0 | 0 | 44 | 0/0/0/2 | Difference | — |
| Birefringent prisms/Wollaston prism.zmx | 2/2 | 0 | 0 | 0 | 0 | 0 | 44 | 0/0/0/2 | Difference | — |
| Diffractive components/Achromatic singlet.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 30 | 0/0/0/3 | Equal | — |
| Miscellaneous/Cylinder.zmx | 1/1 | 0 | 16 | 0 | 1 | 3 | 2 | 1/0/0/0 | Equal | 0.771327 |
| Miscellaneous/Grid Sag Example.zmx | 2/2 | 0 | 0 | 0 | 0 | 0 | 44 | 0/0/0/2 | Equal | — |
| Miscellaneous/Grid sag surface.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Miscellaneous/MEMS DLL Sample.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Difference | — |
| Miscellaneous/Toroid.zmx | 1/1 | 0 | 7 | 0 | 4 | 9 | 2 | 0/0/1/0 | Equal | 8.93607 |
| Optical testing/Mach Zender Interferometer.zmx | 2/2 | 0 | 0 | 0 | 0 | 0 | 44 | 0/0/0/2 | Equal | — |
| Telescopes/Yolo.zmx | 1/1 | 0 | 8 | 2 | 4 | 7 | 1 | 0/0/1/0 | Equal | 1.73402 |
| Tilted systems & prisms/OAP using Chebyshev Polynomial surface.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |

P/D/I/E 分别为 Pass、Difference、Incomparable、Error。Close 不计 Pass；Error 和不可比保留在分母。反射局部方向约定、坐标断点面号对应、单位/无焦适配器与未支持面型另列限制，原始差值不自动等同于已确认的光学缺陷。任何单项通过不代表整镜头认证。完整误差、原因与证据目录见相邻 CSV/JSON。

分析结论：{"Incomparable":79,"Error":397,"Pass":31,"Difference":9,"Close":2}；逐面光线：{"Error":32,"Pass":1,"Incomparable":2}。源文件 28/28 保持。

