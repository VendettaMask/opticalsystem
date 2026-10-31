# 双筒 Fourier 收敛参考

`reference.zip` 保存原四文件集中的双筒 C1/C2 输入快照、原 ZMX、3.2 cycles/mrad 的原生 128/256 单频率随视场曲线、设置/单位/模型回读、704 条移位光线原始数据及原 32 采样曲线。`manifest.json` 记录归档和逐成员 SHA256。

OpticStudio 2026 R1，+Y，Field Density 10，全波长，无偏振，移除渐晕因子。以原生 128→256 在原曲线门槛内一致确认本检查点的参考收敛；Core 32 对原生 256 的 T/S 四条曲线通过，物理输入相同，积分密度分别披露。用户已明确选择“以收敛结果为精度基准”。原 32 对 32 失败仍保留，不反写为通过。

这个参考不覆盖其他五个频率或全部 Fourier/Huygens 分析。原生 512 只有边缘点补充控制，在[研究封存](../../../rechecks/2026-10-31/binocular-fourier-converged/README.md)内单列。两份快照保留各自配置身份，未用 C1 原生曲线代替 C2。

[专题报告](../../../../docs/BINOCULAR_FOURIER_CONVERGENCE_2026-10-31.md)说明收敛标准、逐点/整曲线统计和失败控制。所有运行均为后台独立 ZOS-API 实例，无截图或用户桌面操作。
