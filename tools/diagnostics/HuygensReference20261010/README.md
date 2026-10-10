# Huygens 完整光线平面参考诊断

2026-10-10 的独立测试项目，不加入正式产品或正式测试总数。只通过正式 C# Core 取得光线追迹结果；独立平面波相位求和只作为测试参考，不为产品分析、实验室候选或验收供给计算结果。

使用同源 MS-L7 原文件和已冻结的 Auto 二维网格，固定轴上、单色、瞄准开启、32×32 像面与 0.25 µm 间隔。参考直接使用光线的像面 XYZ、完整方向与累积光程，按原生物理坐标逐像素计算，振幅为强度平方根，理想峰值由同源无像差相干和定义；没有拟合尺度、额外峰值归一化或移动网格。

端点、单元中心、FFT 式中心、半开网格、32 闭区间网格是五个明确标记的节点诊断，不认定其中任意一组就是原生 Huygens 的内部节点。测试只断言测量能够完成且误差有限，**不是原生精度通过门禁**。五组仍有约 3.9%–6.1% 的二维相对 L2 差异，没有接入产品；不能因为测量测试通过而关闭 N01。

```powershell
$env:OPTICAL_REPOSITORY_ROOT = (Get-Location).Path
$env:OPTICAL_REFERENCE_OUTPUT = Join-Path (Get-Location).Path 'artifacts/validation/huygens-reference-20261010/new-run'
dotnet test tools/diagnostics/HuygensReference20261010 -c Release --logger 'trx;LogFileName=reference.trx' --results-directory $env:OPTICAL_REFERENCE_OUTPUT
```

每次使用新的输出目录。首次编译的缺失命名空间错误不算数值失败；最终完整五项诊断及各 JSON 另留证据。用户已确认原生 Zemax 采集在另一台电脑，本机不支持；后续不再尝试本机连接或安装。本轮没有新原生采集、安装或许可变更。仍需在可用的另一台电脑取得同源 Huygens 的原生节点、最终光线方向、相位与权重证据，不能只凭 Auto/Planar 像素一致就替换传播模型。
