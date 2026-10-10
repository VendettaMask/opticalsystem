# N02 分层诊断工具

只调用正式 C# Core 的光学计算与 FFT；没有独立光学引擎或产品运行时外部数据依赖。原生 API 客户端 `NativeEnergyControl.cs` 使用本机安装的 ZOS API，与 SDK 项目分开编译。原生 API、许可及专有二进制不随仓库分发。

2026-10-10：`CollectVerification.ps1` 在读取验收结果、写账本前重新读取冻结文件并严格核验哈希，不再信任清单内的历史 `unchanged`。当前检出存在 8 项字节差异，旧入口会明确拒绝而不会覆盖历史账本。本轮来源保护的独立验收入口是 `tools/validation/CollectPreparedPupilRepair20261010.ps1`，不把这些差异记作通过。见[来源与证据修复](../../../docs/PREPARED_PUPIL_INTEGRITY_REPAIR_2026-10-10.md)。

```powershell
dotnet run --project tools/diagnostics/N02Layers20261009 -c Release -- --inputs 32 pupil-inputs.json
dotnet run --project tools/diagnostics/N02Layers20261009 -c Release -- --complex-control validation/zemax/2026-r1/fft-pupil-phase-2026-10-09/ms-l7-32 fresh-inverse 32
dotnet run --project tools/diagnostics/N02Layers20261009 -c Release -- --prepared-controls validation/zemax/2026-r1/fft-pupil-phase-2026-10-09 prepared-controls.json
```

完整 N02 层诊断参数为 `repository-root native-run-directory fresh-output-directory`。输出目录须为空；native run 需保留原始捕获结构 `input/source.ZMX`、`dee-32`、`psf-32` 和可选复场/批量瞳孔目录。工具确认同源哈希和原 DEE 数值未变，再记录原设置的正式曲线、二维 PSF、窗口和积分控制。

`CaptureControls.ps1` 对三份独立控制先复制处方并核对 SHA-256，再读取模型、捕获复场和批量 OPD。它需要已经编译的 `host/ZemaxHost.exe` 和 `host/NativeEnergyControl.exe`。客户端编译用系统 .NET Framework x64 csc、现有 `tools/OptilandWorkbench.ZemaxComparison/ZemaxHost/*.cs`、本诊断源，引用 System.Web.Extensions 和安装目录的 ZOSAPI、ZOSAPI_Interfaces、ZOSAPI_NetHelper；原生客户端入口指定 `/main:NativeEnergyControl`。

原生客户端隐藏启动，并只等待宿主进程；许可子进程可能保留重定向管道，因此不能将其通过 PowerShell 输出管道作为完成判断。请求和设置只作用于未保存的原生副本，不保存或修改冻结处方。

逆变换相位、32 节点样条和窗口积分都是明确标记的诊断；它们不替代直接原生输出。32 节点样条控制没有消除 DEE 残差，未接入产品。最终证据与适用边界见 [报告](../../../docs/FFT_PUPIL_PHASE_REPAIR_2026-10-09.md)。

原生客户端的 `adapter=huygens-methods` 用实际反射得到的高级方法枚举逐一设置并读回，捕获同一显式分析契约，结束恢复原方法；不保存副本。N01 截面只有原生文本、API 数值数组为空；二维 Huygens 控制才有直接双精度网格。必须检查通道非空及节点数，禁止把两个空数组相同当作数值一致。
