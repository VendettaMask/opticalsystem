# 双筒 Fourier 剩余五频率参考

参考包 SHA256：`34acbad2a68835e959fde02170b909a9dd2f8c3c2f876dd62e667e0fbcad608d`，58 个成员，385,775 字节。`manifest.json` 和 `verification.json` 实际逐成员读回校验。

`contract.json` 声明双配置、+Y、十个视场计算点、全波长、无偏振、零渐晕因素和 cycles/mrad。`curves/c*-n*/fourier-operands.json` 是真实原生 MTFT/MTFS Grid=0、Data=0 计算值；128/256 采样分别独立捕获。字段、模型、单位、请求和依赖回读一并保存。

**本包的 `curve.json` 保留派生参考身份。** 早期整图调用多次超时，但后期 `live-buffer-control` 实际已成功；前一汇总遗漏了其最终状态，现予更正。后续已补齐[直接原生参考](../binocular-fourier-direct-2026-10-31/README.md)并独立确认本包结果，当前精度核对使用直接原件，本包 ZIP 原 SHA 不变。`proof/` 保留的六组完整原生曲线与计算点仍覆盖 42 条曲线、12,600 值，独立显示转换最大差 3.3306690738754696e-16；没有使用产品 Core 结果生成参考。

原验收门槛及历史低采样分类保持。原生 128→256 收敛、Core 32→64 收敛和结果比较分别检查。最终 20 条曲线通过的范围、658 个逐点超限及工具限制见[专题](../../../../docs/BINOCULAR_FOURIER_REMAINING_FREQUENCIES_2026-10-31.md)和[复核封存](../../../rechecks/2026-10-31/binocular-fourier-remaining/README.md)。原始高频专题及其档案没有改写。
