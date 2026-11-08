# 原物理窗口的分批傅里叶计算

自适应二维 PSF 先尝试既有路径。如果按像元间距拉伸的名义格点无法在预算内安排两档有效采样，使用完整归一化瞳孔分批追迹，并以受限一维卷积计算原物理像面坐标的傅里叶值。中心截线可以先沿垂直方向求瞳函数的复边缘和，再直接计算原自动范围或固定范围的全部坐标。两条路径均保留相位检查、原瞳孔归一化、射线与工作内存预算，以及物理范围检查。固定请求沿用已验证入口。 [FFT PSF 的像元间距定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_PSF.html) [正式产品回执](product-replays.json)

| 请求 | 原物理节点 | 两个 Core 档位 | 采用的自适应精度 | 独立对照最大 NRMSE |
| --- | ---: | --- | ---: | ---: |
| #443 Spectrometer PSF | 16,384 | 2560→4096 | 0.003 | 0.000720024 |
| #447 Spectrometer X 截线 | 201 | 3072→4096 | 0.005 | 0.00107489 |
| #1205 Irregular MTF | 600 | 2816→4096 | 0.003 | 0.000129093 |
| #1207 Irregular X 截线 | 201 | 3072→4096 | 0.001 | 0.000520244 |

两条截线各用原自动坐标，分别覆盖 ±44.1937487731 µm 与 ±20.9759689593 µm。前 3 条的默认自适应 0.001 证书仍为未收敛，正式成功回执明确记录精度选项。独立对照始终使用原比较门槛。 [Spectrometer 完整独立网格](../spectrometer-fft-psf-reference/verification.json) [两条原截线](../fft-central-axis-reference/README.md) [Irregular MTF](../irregular-fft-mtf-reference/verification.json)

原固定请求的身份、两种设置的正式输出及追溯记录按内容哈希归档。当前程序集的 57 项定向回归通过。小规模控制逐点验证两种分批 PSF 与对应稠密瞳孔，以及两方向分批截线与中心像面切片。 [载荷清单](manifest.json) [数值归档](numeric-evidence.zip) [定向测试](streamed-fourier-axis-focused.trx)
