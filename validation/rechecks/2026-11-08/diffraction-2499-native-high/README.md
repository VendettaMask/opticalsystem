# Unobscured Gregorian #2499 的高档二维 FFT PSF 参考

OpticStudio 2026 R1 以原处方、配置、视场、波长及原 128×128、0.25 µm 像面设置补采 1024 和 2048 光瞳档。两档的设置与模型均回读核对。分析均报“计算终止；无效的结果”，API 网格为 0×0 的空占位。原 256／512 档也缺有效数值。 [原生回读、诊断和哈希](verification.json) [新增原始载荷](native-evidence.zip) [前轮原生范围](../../2026-11-07/fft-psf-native-window/README.md)

正式 Workbench 自适应入口保持原物理窗，密瞳 1024→2048 已通过本地相位和默认 0.001 相对 RMS 自收敛条件，实际相对 RMS 变化为 0.000827883。跨软件对照仍缺有效原生数值，独立条目保持开放。 [产品收敛证书与程序集](verification.json)
