# 瞄准镜 MTF 随视场原生参考

`reference.zip` SHA-256：`1edd441164ec1278c73e1db80a7bfe54e65e5f7f694ef392fba5d41ba168cf3d`，260 项、652,861 字节，逐项哈希/长度读回通过。输入与策略身份、单位转换、分析契约见归档 `contract.json` 和 `manifest.json`。

Fourier 使用直接原生 256/512 完整曲线并通过收敛门槛。Huygens 的 64/128 完整曲线、256 六个单频完整曲线和 Field 2 主光线/质心 PSF 是有限采样回归参考；Huygens 128→256 尚未全部收敛，**没有声明最终精度通过**。单频曲线仅按顺序组合，不插值或生成原生值。

原生单位 ArcMinutes，物理频率 cycles/mrad。全部参考由后台 `CreateNewApplication` 取得，无桌面控制、无截图。超时、分离窗口/瞳孔的派生诊断及实际 Core 结果保存在[本轮证据](../../../rechecks/2026-10-31/riflescope-mtf-field/README.md)，没有替换历史失败或旧参考。

结论及剩余工作见[研究记录](../../../../docs/RIFLESCOPE_MTF_FIELD_2026-10-31.md)。
