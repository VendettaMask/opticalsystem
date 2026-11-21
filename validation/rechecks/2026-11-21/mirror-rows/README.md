# 反射镜整行青绿色

用户于2026-11-21选择浅青绿色，用于镜头数据编辑器的反射镜整行底色，与玻璃蓝和坐标参考面紫灰区分。

## 实现范围

- 从 Core 的实际 `OpticalSurface.IsReflective` 经 `SurfaceRowDto` 传递只读身份，保留编辑行 DTO 往返；颜色不依赖用户标注、面型名称或本地化文本。
- `MirrorRowStyles` 集中管理五主题的常态、悬停、选中、选中并悬停色阶，覆盖原生模板底板及冻结列。参考面分类优先；其他材料仍为蓝色，Air 仍空显示。
- 保留原表格布局、角色圆点、字体、对齐、原生焦点、禁用及验证状态，未修改追迹和处方计算。

## 验证方法与边界

默认输出路径的 Release 主程序构建成功，0警告、0错误，见[构建日志](build-release.log)。

原主测试工程在编译阶段遇到相机测试仍引用 `OpticalSystemDesign.Core.Analysis.Imaging` 等不存在类型的问题，未能执行所选测试。本目录的[定向项目](focused/MirrorRows.csproj)直接链接仓库中的四个相关测试文件，引用当前主程序项目；仅补充相同的离屏启动器与集合声明，不复制产品实现或测试断言。

沙箱内测试宿主未返回执行结果，两次尝试中止，不能记为通过。随后在沙箱外仅运行后台无界面测试，保留相同的主程序、测试程序集与用例，未打开、控制或重启用户的桌面程序。定向项目还原产生 NU1900（NuGet漏洞数据源不可达），没有绕过产品安全设置；最后编译无 C# 或测试分析器警告。

覆盖真实 Yolo 示例的五主题四状态、正文对比度、改名与同名非镜面、实际材料变更、撤销、滚动复用、主题往返、保存重开和新建清除，以及原有玻璃行和数值输入回归。实际截图包括五主题100%，以及普通主题150%和200%的离屏合成像素；这不是原生桌面交互验收，也未运行全量光学精度矩阵或发布。

28项定向测试全部通过：反射镜10项、坐标参考面10项、既有材料行1项、数值输入行7项。原始结果见 [TRX](mirror-rows-focused.trx)，可提交的范围与源文件摘要见[结果清单](results.json)。

## 实际画面

- [普通100%](captures/mirror-rows-Light-100.png)、[150%](captures/mirror-rows-Light-150.png)、[200%](captures/mirror-rows-Light-200.png)
- [暗夜](captures/mirror-rows-Dark-100.png)、[光域](captures/mirror-rows-Lumen-100.png)、[异世界](captures/mirror-rows-Isekai-100.png)、[像素](captures/mirror-rows-Pixel-100.png)

## 重跑

在仓库根目录执行（需要先完成项目还原）：

```powershell
$env:OPTICAL_SYSTEM_DESIGN_MIRROR_ROWS_CAPTURE_DIR = "$PWD\validation\rechecks\2026-11-21\mirror-rows\captures"
dotnet test validation/rechecks/2026-11-21/mirror-rows/focused/MirrorRows.csproj -c Release --no-restore -m:1 -nr:false --logger 'trx;LogFileName=mirror-rows-focused.trx' --results-directory validation/rechecks/2026-11-21/mirror-rows
```
