# 再次无响应：显卡小矩形回放与标量图批量提交

## 本次捕获的阻塞位置

2026-11-27 14:51（北京时间），用户再次报告无响应。此时运行的是本地 Release 程序，已包含[第一阶段分配修复及日志](../interferogram-ui-diagnosis/README.md)，尚不包含本页的显卡批量提交。结果有 204,269 个有效点：计算 982 ms、结果控件构造 110 ms、首次托管绘图 91 ms，均已结束。

对仍无响应的进程只读捕获线程转储，未关闭用户窗口。UI 线程在 `MediaContext.SyncWaitCompositorBatch → Task.Wait` 等待合成批次；渲染线程在 `RenderDataStream.Visit → DrawingContextImpl.DrawRectangle → sk_canvas_draw_rect` 的原生调用内。绘图上下文的 `GrContext` 非空，当前透明度为 0.85，调用矩形约为 1.587×1.587 DIP。光学工作线程处于等待状态。

捕获时进程私有内存 9,152,688,128 字节，托管堆已分配约 566,784,432 字节、已提交约 568,381,440 字节。不能把全部差额归因于某个驱动分配，但线程栈已经确认本次界面堵在原生 GPU 小矩形绘制及同步等待路径，不能再将“托管绘图完成”视为显示完成。未确认驱动内部具体锁或函数耗时占比，也未将本次转储追溯成 13:59 事件的直接证据。

原始转储约 9.13 GB，包含进程内存，连同私人运行日志仅保留在本地忽略目录；不进入仓库或证据归档。[verification.json](verification.json)只记录最小诊断摘要。

## 通用修复

二维灰度／伪彩色的共用 `WavefrontSurfaceControl` 改用 `ScalarMapRasterOperation`。检测到真实 GPU 上下文时，在 CPU framebuffer 上按完整设备变换绘制原有全部采样单元，再向显卡提交一张图像；软件绘图及 PNG／报告继续直接绘制原矩形。光学计算、采样、数据表、缺口、物理坐标、数值范围和颜色映射均未修改，等高线及三维表面继续原有专用路径。

每个记录操作最多保留一张可见区域图像，尺寸上限 8192×8192 且总像素不超过 64 Mi；换图和释放时销毁旧图。设备矩阵、可见区域和透明度变化会重建图像；输入网格、坐标、数值范围、颜色映射及边缘模式参与操作身份。科学色表在记录时冻结，改变全局色表不会错误复用旧图。CPU 单元保持原边缘重叠和逐单元透明度，原生图像不再重复施加透明度。

批量图像与直接矢量绘制经过不同的 Skia blitter，GPU 屏幕边缘的抗锯齿及八位透明混合不宣称与旧矢量回放逐像素相同。软件 PNG／报告的原像素判据保持严格；新增图像核检查用独立公开位图 API 对照精确的样本中心颜色、透明度、坐标和缺口，不采用拟合容差或修改光学门槛。

## 真实显卡离屏检查

使用实际分析服务打开内置 `Afocal/Beam Expander.ZMX`，CPU 计算干涉图，`Sampling=512 x 512`、`XTilt=3`、`YTilt=-1`，得到 204,269 个有效点。随后初始化独立的 Avalonia Windows／D3D11 ANGLE 上下文，不创建可见窗口。调用真实 GPU 离屏表面上的生产记录操作，每帧确认 `GrContext` 非空、Flush 并回读像素。

20 帧全部结束。首帧 164.05 ms；第 1～9 帧固定设备状态，绘制加回读约 5.88～6.63 ms。第 10～18 帧交替缩放和透明度，包含重建图像，约 63.42～115.46 ms。第 19 帧还编码保存 PNG，175.97 ms，不能混入纯绘制计时。进程私有内存范围 519.46～542.98 MB，包括先前真实光学计算；这不是托管累计分配量，也不是与原 9 GB 用户会话等条件的内存基准。

此检查验证真实显卡单图提交、缓存复用、状态变化和像素回读；没有重放旧版二十多万个原生矩形以再次制造卡死，也没有接管用户的完整桌面会话。尚不能据此保证所有驱动、窗口合成状态和其他分析绘图均不会无响应。

## 检查结果与交付范围

后续默认 Release 离屏复验已通过同一组 65 项，包含下文首轮失败的全部 7 项。波前图的实时窗口和报告在 Auto 下均选中 1600×900 逻辑图纸，四组分别核对导出画布、图框尺寸及图框内的全部像素。干涉图的三个用例从图签的左侧区域查找文字，验证摘要数值、空值占位、标题、日期和布局边界。[复验回执和用例名称](footer-palette-recheck.json)

扩大定向检查 **65 项：58 通过、7 失败、0 跳过**。新增绘图回归共 **25 项全部通过**，其中本次新增位图检查 16 项，第一阶段分配／失效检查 9 项。7 项仍为第一阶段已在原公开程序集复现的画幅与图签断言，错误名称和消息一致，未修改断言或记为通过。

正常 Release 输出另跑新增 25 项，全部通过；测试目录与正常 App 目录的程序集 SHA-256 一致。正常 Release App 构建零警告、零错误。最终正常程序集离屏重放的真实 512 灰度 PNG，与第一阶段保存的 1200×900 图逐像素一致。相关 C# 格式检查通过。

早期检查曾因独立对照的 Y 轴方向、八位混合模型及 DPI 变换错误失败；离屏 GPU 诊断程序也曾因固定版本接口访问及构造器选择失败。原始回执保留，未删除失败历史。最终对照明确应用设备缩放并使用独立位图合成模型，没有放宽断言。

正常输出已更新，当前已启动实例不能热替换；公开 OTA 包尚不含此次本地修改。未进行全量 95 镜头精度矩阵或新公开发布。

## 回放与证据

[evidence.zip](evidence.zip)包含原始测试回执、构建／格式回执、GPU 分帧计时、唯一 GPU 图像、最终软件像素核对及离屏程序源码；不含私人运行日志、内存转储、安装包或第三方运行库。逐项 SHA-256 和归档身份见 [verification.json](verification.json)。第一阶段图像直接引用已有证据，未重复归档。

将归档中 `probe` 的内容解压到仓库根的 `.tmp/scalar-raster-replay`。在仓库根执行：

```powershell
$isolationProps = [IO.Path]::GetFullPath('.tmp/scalar-raster-replay/isolated-build.props')
dotnet restore .tmp/scalar-raster-replay/gpu-probe/GpuProbe.csproj --locked-mode -p:DirectoryBuildPropsPath=$isolationProps
dotnet run --project .tmp/scalar-raster-replay/gpu-probe/GpuProbe.csproj -c Release --no-restore -p:DirectoryBuildPropsPath=$isolationProps
```

程序在 Windows 使用实际显卡离屏目标，要求可用的 D3D11 ANGLE 上下文；接口反射绑定 Avalonia 12.1.0。初始化失败应明确失败，不回退成软件通过。独立临时配置位于该诊断程序输出下，不改用户配置。

源码：[共用标量图](../../../../src/OpticalSystemDesign.App/Controls/WavefrontSurfaceControl.cs)、[图像记录操作](../../../../src/OpticalSystemDesign.App/Controls/ScalarMapRasterOperation.cs)、[新增回归](../../../../tests/OpticalSystemDesign.Tests/ScalarMapRasterTests.cs)。
