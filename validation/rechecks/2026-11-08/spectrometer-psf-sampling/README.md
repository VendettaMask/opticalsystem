# Sample Spectrometer 二维 FFT PSF 的采样检查

#443 保持原处方、配置 1、轴上视场、波长 2、原像面、标量线性强度和 128×128、0.25 µm 原网格。OpticStudio 2026 R1 分别补采 512、1024 和 2048 档。三档设置与选择器均与原请求吻合，三份输出均有取样不足诊断。 [原生身份及诊断](verification.json)

Core 固定 2048 档的最大相邻相位差为 3.30223 波，超过原 0.5 波保护门槛。正式自适应入口上限设为 6656 时，按已有相位数据无法在预算内安排两档相位收敛试验，返回不可用。#443 继续开放。同一处方的 #445 MTF 有独立频率区间收敛证据，另行记账。 [原网格 PSF 回执](verification.json) [MTF 的独立复验](../streamed-mtf-frontier/445-receipt.json)

新增输入、原始输出和度量程序按 SHA-256 保留。 [载荷映射](manifest.json) [数值归档](numeric-evidence.zip)
