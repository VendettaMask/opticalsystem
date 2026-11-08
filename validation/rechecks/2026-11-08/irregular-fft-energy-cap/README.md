# Irregular Surface #1210 的最高档 FFT 相位检查

保持原处方、配置 1、轴上视场 1、0.55 µm、原像面、质心参考及 0–5 µm 包围能量合同。正式 Workbench 回放只改变瞳孔档位，在现有最高名义档 262144 报告相邻通光点最大相位差 1.148733193 波，超过 0.5 波门槛。

沿用已由冻结 #1202 原生波前核对的独立处方。按当前包围能量网格的 X/Y 归一化瞳距 0.0020022902 和 0.0019078565，独立计算全部 822,375 个通光瞳点及四邻点相位。采用当前能量参考球，Y 相邻点最大相位差为 1.148733192724 波，与正式回执相差 2.85×10⁻¹⁰ 波。采用原生波前反解的参考球，相同点差为 1.148790996 波。独立证据复现最高档的相位阻断，实际与衍射极限的完整曲线仍待交叉验收。 [原请求、证据哈希与最大差点位](verification.json) [原生波前与独立处方](../irregular-fft-psf-reference/README.md)

运行 `python IrregularEnergyCap.py <仓库目录> <输出路径> <Workbench数据> <Workbench请求>` 复算。 [复算脚本](IrregularEnergyCap.py) [正式入口原始存档](../parallel-streamed-fourier-followup/highest-energy-phase-controls.zip) [载荷清单](../parallel-streamed-fourier-followup/highest-energy-phase-controls.json)
