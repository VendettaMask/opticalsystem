# 原生 FFT 瞳面相位控制

2026-10-09 在本机 OpticStudio 26.1.0（build 260127）、有效 Enterprise API 许可下新增捕获。`manifest.json` 保留 76 份原始文件的字节数和 SHA-256；本说明不属于原生输出。

| 控制 | 来源 | 瞳孔 / FFT | 设置 |
| --- | --- | --- | --- |
| ms-l7-32 | 原冻结 MS-L7 `source.ZMX` | 32 / 64 | 配置 1、轴上视场 1、波长 1、自动间距、原生 Paraxial 瞄准 |
| ms-l7-64 | 同源 MS-L7 | 64 / 128 | 同上；默认伸展 √2 |
| cooke-32 | Zemax Samples/Sequential/Objectives/Cooke 40 degree field.zmx | 32 / 64 | 配置 1、轴上视场 1、波长 1、自动间距、原生瞄准关闭 |
| tessar-32 | Zemax Samples/Sequential/Objectives/Tessar lens using vignetting factors.zmx | 32 / 64 | 配置 1、轴上视场 1、波长 1、自动间距；实际瞄准见 model.json |

每个目录保存同源处方、模型与环境、Linear/Real/Imaginary 三个独立 FFT 输出和设置，以及 `OPDMode.CurrentAndChief` 批量追迹的原始 OPD、强度、坐标和方向。所有控制关闭偏振与峰值归一化。未改处方来制造理想 PSF；未拟合位移、增益或相位常量。

相位诊断使用正式 Core 的 FFT 例程对原生复场做逆变换。逆变换推断值属于诊断，不是原生瞳面 API 导出。四邻域棋盘节点规则由四组控制共同支持；它不是公开文档给出的通用 Zemax 算法规范。新测试将原生瞳孔输入送入正式 `ComputeFftPsf`，检验变换构造，不能替代完整光线追迹、偏振、离轴或所有档位的认证。

捕获工具及复算入口：`tools/diagnostics/N02Layers20261009`。最终结果见 [FFT 瞳面相位修复报告](../../../../docs/FFT_PUPIL_PHASE_REPAIR_2026-10-09.md)。原 N02 的实际/理想包围能量另行判定。
