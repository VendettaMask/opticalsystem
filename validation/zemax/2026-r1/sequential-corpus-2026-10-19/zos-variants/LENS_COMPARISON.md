原生参考格式：ZOS。ZOS 补充控制的 Core 输入为原生导出的 ZMX；不代表产品直接支持 ZOS 导入。

# Sequential 全目录逐镜头对比

日期：2026-10-19。所有源文件逐字节保持；保留原比较策略、采样及容差。原生数据为本机新采集，Core 由正式 Application 执行。

| 镜头 | Core/原生配置 | 导入错误 | Pass | Close | Difference | 不可比 | Error | 光线 P/D/I/E | ZMX/ZOS 模型读回 | 最大 NRMSE |
|---|---:|---:|---:|---:|---:|---:|---:|---|---|---:|
| Afocal/Binocular_System.ZMX | 2/2 | 0 | 0 | 0 | 0 | 60 | 0 | 0/0/0/6 | Equal | — |
| Birefringent prisms/Birefringent cube.zmx | 0/2 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Birefringent prisms/Rochon prism.zmx | 0/2 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Birefringent prisms/Senarmont prism.zmx | 0/2 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Birefringent prisms/Wollaston prism.zmx | 0/2 | 1 | 0 | 0 | 0 | 0 | 0 | 0/0/0/0 | Equal | — |
| Diffractive components/Sample Spectrometer.zmx | 1/1 | 0 | 0 | 0 | 0 | 0 | 30 | 0/0/0/3 | Equal | — |
| Miscellaneous/Doubly telecentric system.zmx | 1/1 | 0 | 20 | 1 | 0 | 1 | 0 | 1/0/0/0 | Equal | 0.0122918 |
| Miscellaneous/MEMS DLL Sample.ZMX | 1/1 | 0 | 0 | 0 | 0 | 0 | 22 | 0/0/0/1 | Equal | — |
| Objectives/Double Gauss 28 degree field.zmx | 1/1 | 0 | 26 | 0 | 0 | 3 | 1 | 3/0/0/0 | Equal | 0.000840441 |
| Objectives/Wide angle lens 100 degree field.zmx | 1/1 | 0 | 0 | 0 | 0 | 30 | 0 | 3/0/0/0 | Equal | — |
| Telescopes/Schalcktelescope 10.zmx | 1/1 | 0 | 22 | 1 | 5 | 1 | 1 | 0/3/0/0 | Equal | 2 |
| Tilted systems & prisms/Offner relay.zmx | 1/1 | 0 | 19 | 0 | 1 | 2 | 0 | 0/0/1/0 | Equal | 2 |
| Zoom systems/Mirror zoom.zmx | 5/5 | 0 | 0 | 0 | 0 | 110 | 0 | 0/5/0/0 | Equal | — |

P/D/I/E 分别为 Pass、Difference、Incomparable、Error。Close 不计 Pass；Error 和不可比保留在分母。反射局部方向约定、坐标断点面号对应、单位/无焦适配器与未支持面型另列限制，原始差值不自动等同于已确认的光学缺陷。任何单项通过不代表整镜头认证。完整误差、原因与证据目录见相邻 CSV/JSON。

分析结论：{"Incomparable":207,"Error":54,"Pass":87,"Close":2,"Difference":6}；逐面光线：{"Error":10,"Pass":7,"Difference":8,"Incomparable":1}。源文件 26/26 保持。

