# 相机代码审查复现

本轮仅审查，不修复产品实现。结论和范围见 [专项报告](../../../../docs/CAMERA_CODE_REVIEW_2026-10-31.md)。

- `reproductions.json`：三个非法 3D 输入属性事件抛出异常；几何和衍射路径分别接受曲面像面却给出与平面逐点相同的结果。
- `file-hashes.json`：相关源码及诊断程序实际加载的 App/Core DLL 指纹。工作区存在并行修改，因此不声称整仓为冻结状态。
- `default-test-compile.log`：后续编译被并行镀膜修改阻塞的原始输出；不是测试通过证据。

后台运行命令（PowerShell，仓库根目录）：

```powershell
dotnet restore tools/diagnostics/CameraReview20261031/CameraReview.csproj --ignore-failed-sources --disable-parallel -p:NuGetAudit=false -m:1 -nr:false
dotnet build tools/diagnostics/CameraReview20261031/CameraReview.csproj -c Release --no-restore -m:1 -nr:false
dotnet tools/diagnostics/CameraReview20261031/bin/Release/net10.0/CameraReview.dll validation/rechecks/2026-10-31/camera-review
```

这些案例使用真正的 Avalonia headless/Skia 和共享 Core。`EscapedException=true` 与 `OutputIdentical=true` 表示成功复现了缺陷，不是功能验收通过。未操作用户桌面。
