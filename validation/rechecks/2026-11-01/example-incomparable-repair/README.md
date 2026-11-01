# 27 项不可比：已验证子集

这是 2026-11-01 原 27 项不可比中的 **23 项验收集**，不是全部 95 支示例的精度认证。四项 Grid GRIN FFT 请求在本包封存时仍待单独收敛核验；后续结果应追加独立证据，不改写本包。

| 验收范围 | 数量 | 结果 |
|---|---:|---|
| 晶体单光线逐面表 | 16 | 原十一列门槛通过；按物理面映射坐标断点 |
| 晶体完整 PSF 网格 | 6 | Core 256 对原生 512 通过；双方 256→512 分别通过 |
| Grid GRIN 投影波前 | 1 | 覆盖率 100%；NRMSE 约 0.000539，原门槛 0.003 |
| 相邻晶体波前回归 | 8 | 全部通过；单列，不计入原 27 项 |

执行 Core SHA256：`0d7b4b2085976134bdc38ab3503253e123f422ceebd57f9befe94ab49bb1507b`。应用、比较器和每次结果的实际来源另见包内 identity/provenance。42 项 Core 与 67 项比较器/归一化/插值定向测试通过；实际测试 Core 与上述冻结版本一致，测试前后未漂移。未做全仓测试或原生 GUI 操作。

`accepted-evidence.zip` 为 32,371,252 字节，SHA256：`4c8bb49a9546f4bfcd076f0f46c7f2fc203b04450f047f1008a7d0c1f37408cd`。[manifest.json](manifest.json) 记录 991 项文件的路径、大小及 SHA256；[verification.json](verification.json) 记录归档回读均匹配。[scope.json](scope.json) 明列原通过索引、四项待核验索引和相邻回归索引。

包内保留原总账、原低采样 Core/原生数据、原门槛、有效直接原生缓冲区、双方收敛数据、源/模型/请求、冻结执行程序集及定向测试。超出请求窗口的巨大原生整幅文本留在原路径，以 full-text-index.json 记录哈希；没有用截断窗口重新归一化。计算和对比脚本见 [IncomparableRepair20261101](../../../../tools/diagnostics/IncomparableRepair20261101/)，进展及开放项见[专项记录](../../../../docs/EXAMPLE_INCOMPARABLE_REPAIR_2026-11-01.md)。

追加的 `psf-evidence.zip` 单独记录 Grid GRIN PSF（原索引 495），当前累计 **24/27**。该包为 1,842,524 字节、44 份文件，SHA256 `87e0fc48419b7f37b11c152cd4a04b47897b37140dd7266890f94f2f562d8671`；全部回读哈希匹配，见 [psf-manifest.json](psf-manifest.json) 和 [psf-scope.json](psf-scope.json)。原生积分步长 0.005→0.001、原生瞳采样 1024→2048、同一 Core 瞳采样 1024→2048 及最终对比均满足原逐点绝对/相对替代条件。最终 NRMSE **0.09236** 大于原 0.01，不将其描述为 NRMSE 通过。仍开放索引 497、499、502；初始 23 项包及其 scope 保持原样。

`bounded-supplement.zip` 保存最新 02abb Core 的 53 份独立 Workbench 计算、前 24 项及八项相邻回归重验、77 项定向测试、有界能量与截线的 Core 收敛、实际二进制和源快照。本追加包 350 份文件、58,306,530 字节，SHA256 `59cbc409be950b7388c627d3af79b45fba10b79c074e1ccb7e0b9cb6c61b0866`，逐项回读全部匹配，见 [bounded-manifest.json](bounded-manifest.json)、[bounded-verification.json](bounded-verification.json) 和 [bounded-scope.json](bounded-scope.json)。它仍只关闭 **24/27**，保留其余三项未验收和 16 个既有严格能量失败；后续原生重采不写入或改写本包。完整旧包的哈希再次核对不变。

`native-frontier.zip` 追加之后完成的原生高采样 MTF、固定完整范围截线、能量内存控制及独立 GRIN 材料曲线/光程诊断。共 1,110 份文件、39,538,380 字节，SHA256 `04d9d31dd71491ba022b8166b88b4e7616c57d474aae65ab6e0a32ef34598254`；全部条目回读匹配，前三包哈希不变。见 [native-frontier-manifest.json](native-frontier-manifest.json)、[native-frontier-verification.json](native-frontier-verification.json)、[native-frontier-scope.json](native-frontier-scope.json)。正式范围仍 **24/27**：原生 MTF 1024→2048 已收敛但产品 S 仍为 Close；原生截线 4096→8192 在相同完整范围仍为 Difference；原生能量 8192 仍警告、16384 仍内存失败。包内各私有 Core 为明确未安装的诊断副本，不能替代产品 02abb 的验收身份或用原生残差校准产品。

后续 [MTF 相位修复独立包](../grid-grin-mtf-phase/README.md)另行关闭索引 497，当前累计 **25/27**。本目录四个既有包及对应 scope 仍保持封存时的阶段身份与哈希；499、502 继续开放，不用新结果改写旧失败。
