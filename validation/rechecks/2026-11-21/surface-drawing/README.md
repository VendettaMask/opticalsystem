# 逐表面绘图设置（2026-11-21）

## 实现

表面属性“绘图”页接通六个原生勾选项、边缘显示和绘图精度；“类型”页的表面透明度使用原生下拉。设置属于展示元数据，不修改交点、材料、光焦度、物理光线或分析结果。

- 隐藏这个表面的光线：隐藏到达该面的入射绘图片段，后续片段继续显示。
- 光线忽略这个面：在绘图中直接连接前后非跳过节点，适用于没有光学作用的辅助面。真实追迹仍包含该面。保留显式传播方向和交互元数据，不根据绘图坐标顺序推断反射。
- 不显示此表面：隐藏表面曲线/面片及选中高亮。局部坐标和原点遵守各自的显式开关。
- 不显示此表面的边缘：隐藏从该面到下一面的连接边和侧壁，保留表面自身轮廓。
- 显示局部坐标轴 / 绘制局部原点：使用真实表面坐标变换生成标记，不根据标签推断位置。二维按投影显示，三维及实体模型使用全局三维坐标。
- 边缘显示：方形连接、锥形连接、平直到下表面和不绘制边缘。方形连接保留原来的机械肩部；锥形连接使用两面的各自口径。平直方式在二维采用当前面半径与下一面连接；三维及实体模型使用方形方式，与所参照的绘图定义一致，控件提示已明确。
- 绘图精度：标准、中等、高、演示切换实际曲线/网格采样密度。每个三维镜片的两面共用角向节点，防止不同精度造成侧壁错接；径向采样保留各自精度，全部采样有上限。
- 表面透明度：表示相对于原有绘图强度的不透明度，100% 保留默认外观，0% 完全透明。二维、三维、实体、透明、线框和剖切使用同一显示设置；它不表示材料的物理透过率。

`SurfaceDrawingOptions` 是共享 Core 的不可变展示记录，快照的可选字段在默认状态省略，旧工程恢复原有外观。应用层纯绘图更新支持撤销/重做、STAROPT 保存重开，且不要求重新应用物理面型。类型页只改透明度或行颜色也走该接口：加载时保留物理控件原值，避免把无穷开关、导入面型名称或显示舍入误判为物理编辑。绘图 DTO 传递面号、结构号和局部坐标；实时显示和 PNG 导出共用原绘制路径。行颜色由同批任务实现，见 [行颜色记录](../surface-row-color/README.md)。

## 验证范围与执行方式

定向项目链接正式测试源：`SurfaceDrawingSettingsTests`、`SurfaceDrawingPanelTests`、`SurfaceDrawingRenderTests`，并包含已有 `SurfaceColorTests` 回归。覆盖保存/撤销、非法值拒绝、缺省快照兼容、真实光线不变、显示节点合并、边缘几何、采样密度、偏心倾斜的局部坐标、五主题原生控件及离屏像素。

主程序保持默认 D 盘输出。定向测试的运行库暂存于 C 盘固定临时目录，通过仅作用于测试项目的 `SurfaceTestOutputRoot` 设置输出位置，避免 D 盘空间不足。依赖和光学计算不读取测试参考来生成运行结果。

验证结束后，本轮两组测试的临时运行库已清理；源码、截图、TRX、运行程序集哈希与包缓存保留，见[清理记录](../surface-row-color/runtime-cleanup.json)。

```powershell
$env:OPTICAL_SYSTEM_DESIGN_SURFACE_DRAWING_CAPTURE_DIR = 'D:\Projects\opticalsystem\validation\rechecks\2026-11-21\surface-drawing\captures'
dotnet test validation/rechecks/2026-11-21/surface-drawing/focused/SurfaceDrawing.csproj -c Release -m:1 -p:NuGetAudit=false -p:SurfaceTestOutputRoot=C:\Users\19851\AppData\Local\Temp\OpticalSystemDesign-20261121-surfaces --logger 'trx;LogFileName=surface-drawing.trx' --results-directory validation/rechecks/2026-11-21/surface-drawing/results
```

实际验证：59 个唯一检查全部通过，零跳过。首次完整运行 53 项通过、6 项 UI 测试因错误假定原生短复选框宽度必须超过 200 DIP 而失败；实际固有宽度为 154 DIP，Isekai 为 166 DIP，均与内容所需宽度一致且没有裁切。测试改为检查实际内容完整、32 DIP 命中高度与窗口边界，不改动产品。最终仅复验这 6 项，全部通过，并补充窄窗两项下拉框的滚动可达检查；重复检查不累计通过数。

原始结果：[完整运行](results/surface-drawing.trx)、[边界诊断](results/surface-drawing-ui-diagnostic.trx)、[最终 UI 与滚动验证](results/surface-drawing-ui-scroll-final.trx)。[verification.json](verification.json) 按相同测试名称用最新结果覆盖初次错误断言，记录 59 个唯一检查及实际运行程序集 SHA-256。

默认 D 盘 Core/Application/App Release 构建成功。39 张离屏图片保存在 [captures](captures/)：包含五主题属性页、Light 窄窗及其滚动后的设置、0/35% 不透明度、局部坐标、边缘与剖切。已目视检查 [Isekai 英文页](captures/drawing-Isekai-1100.png)、[Light 窄窗滚动设置](captures/drawing-Light-620.png)；完整控件、语言、字体与布局保持可读。

过程中的输出占用、并行分析任务暂时的类型可见性编译错误，以及测试误断言均保留为未通过的过程记录，不计为成功。输出占用进程随后自然结束，没有停止未知进程或用户程序。NuGet 漏洞数据源不可达仅在测试命令中关闭 audit，不能将其描述为漏洞检查通过。全仓本地化检查还包含既有未翻译项，本次只补充本功能 29 条实际运行时资源并验证原 4,215 条逐项保留，没有宣称全仓检查通过。

## 外部定义与边界

绘图选项语义参照 [OpticStudio Draw surface properties 官方手册](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Draw_surface_properties.html)。本次是独立实现与本地离屏验证，没有新做原生数值捕获，也没有执行 95 镜头完整光学精度矩阵。这里的展示采样、透明度和显隐均不是光学求解参数。
