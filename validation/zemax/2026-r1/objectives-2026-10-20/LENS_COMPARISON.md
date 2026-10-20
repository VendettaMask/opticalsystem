原生参考格式：ZMX。ZOS 补充控制的 Core 输入为原生导出的 ZMX；不代表产品直接支持 ZOS 导入。

# Sequential 选定目录逐镜头对比

日期：2026-10-20。所有源文件逐字节保持；保留原比较策略、采样及容差。原生数据为本机新采集，Core 由正式 Application 执行。

| 镜头 | Core/原生配置 | 导入错误 | Pass | Close | Difference | 不可比 | Error | 光线 P/D/I/E | ZMX/ZOS 模型读回 | 最大 NRMSE |
|---|---:|---:|---:|---:|---:|---:|---:|---|---|---:|
| Objectives/Apochromat.zmx | 1/1 | 0 | 46 | 0 | 0 | 4 | 0 | 8/0/0/0 | Equal | 3.52054E-06 |
| Objectives/Apochromat2.zmx | 1/1 | 0 | 33 | 0 | 1 | 4 | 0 | 5/0/0/0 | Equal | 0.0114336 |
| Objectives/Apochromat3.zmx | 1/1 | 0 | 26 | 0 | 0 | 4 | 0 | 3/0/0/0 | Equal | 0.000310185 |
| Objectives/Apochromat4.zmx | 1/1 | 0 | 42 | 0 | 1 | 27 | 0 | 13/0/0/0 | Equal | 0.0115403 |
| Objectives/Cooke 40 degree field.zmx | 1/1 | 0 | 30 | 0 | 0 | 0 | 0 | 3/0/0/0 | Equal | 8.20797E-05 |
| Objectives/Double Gauss 28 degree field.zmx | 1/1 | 0 | 29 | 0 | 0 | 1 | 0 | 3/0/0/0 | Difference | 0.000477183 |
| Objectives/Double Gauss 5 degree field.zmx | 1/1 | 0 | 34 | 1 | 0 | 3 | 0 | 5/0/0/0 | Equal | 0.00885041 |
| Objectives/Doublet using MIL number glasses.zmx | 1/1 | 0 | 33 | 0 | 1 | 4 | 0 | 5/0/0/0 | Equal | 0.0114087 |
| Objectives/Doublet.zmx | 1/1 | 0 | 27 | 0 | 0 | 3 | 0 | 3/0/0/0 | Equal | 1.0963E-05 |
| Objectives/Even Asphere.zmx | 1/1 | 0 | 20 | 0 | 0 | 2 | 0 | 1/0/0/0 | Equal | 0.000139952 |
| Objectives/Gaussian Quadrature over Annular Pupils.zmx | 1/1 | 0 | 18 | 0 | 0 | 4 | 0 | 1/0/0/0 | Equal | 3.22174E-06 |
| Objectives/Gaussian Quadrature Over Wavelength.zmx | 1/1 | 0 | 29 | 0 | 0 | 1 | 0 | 3/0/0/0 | Equal | 0.0013398 |
| Objectives/Inverse telephoto lens.zmx | 1/1 | 0 | 30 | 0 | 0 | 0 | 0 | 3/0/0/0 | Equal | 0.000391505 |
| Objectives/Petzval.zmx | 1/1 | 0 | 29 | 0 | 0 | 1 | 0 | 3/0/0/0 | Equal | 0.000966471 |
| Objectives/Relay lens.zmx | 1/1 | 0 | 29 | 0 | 0 | 1 | 0 | 3/0/0/0 | Equal | 7.71896E-05 |
| Objectives/Tessar lens using vignetting factors.zmx | 1/1 | 0 | 28 | 0 | 0 | 2 | 0 | 3/0/0/0 | Equal | 0.000454575 |
| Objectives/Wide angle lens 100 degree field.zmx | 1/1 | 0 | 29 | 0 | 0 | 1 | 0 | 3/0/0/0 | Equal | 0.00277599 |
| Objectives/Wide angle lens 200 degree field.zmx | 1/1 | 0 | 26 | 0 | 0 | 4 | 0 | 3/0/0/0 | Equal | 0.00196634 |
| Objectives/Wide angle lens 210 degree field.zmx | 1/1 | 0 | 26 | 0 | 0 | 4 | 0 | 3/0/0/0 | Equal | 0.00156675 |

P/D/I/E 分别为 Pass、Difference、Incomparable、Error。Close 不计 Pass；Error 和不可比保留在分母。反射局部方向约定、坐标断点面号对应、单位/无焦适配器与未支持面型另列限制，原始差值不自动等同于已确认的光学缺陷。任何单项通过不代表整镜头认证。完整误差、原因与证据目录见相邻 CSV/JSON。

分析结论：{"Pass":564,"Incomparable":70,"Difference":3,"Close":1}；逐面光线：{"Pass":74}。源文件 38/38 保持。

