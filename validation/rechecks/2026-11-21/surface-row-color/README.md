# 逐表面行颜色（2026-11-21）

使用者需要在密集镜头表里标记具体表面，并保留物面、光阑、像面、玻璃、反射镜和坐标参考面各自的辨识信息。主要阅读内容仍是处方数值；颜色编辑位于已有表面属性“类型”页，采用原生 ComboBox、自定义 RGB 输入和小色块，沿用共享字体、间距、输入边框和主题材质。

默认状态继续使用材料蓝、反射镜青绿、坐标参考面紫灰；只有用户显式指定时才以其 RGB 作为该行背景。角色文字和圆点、原生选择、键盘焦点、无效和禁用反馈继续保留。选中和悬停使用同色的明暗变化，黑白正文根据对比度选择。默认选项清除覆盖，自定义支持 `#RRGGBB`，通过“应用”提交、“还原”丢弃尚未应用的输入。

行色通过 `SurfaceDrawingOptions.RowColor` 存储，只属于显示设置；共享场景的透明度与绘图设置由 [表面绘图工作](../surface-drawing/) 一并实现。`SurfaceEditorRow` 读取和提交时完整保留 `Drawing`，避免普通标注或材料编辑丢失显示设置。

## 实际结果

默认路径 Release 主程序构建成功。最终定向检查 **34/34 通过，0 失败**，原始结果见 [row-color-final.trx](results/row-color-final.trx)，摘要见 [verification.json](verification.json)。其中 13 项为新增行色检查，21 项为原玻璃、反射镜和坐标参考面行色回归。只统计本轮最终结果，不累加重复运行。

- 五主题覆盖四种选中/悬停组合、禁用反馈、冻结与滚动单元格、自定义文本和透明数值输入的实际前景对比度。
- 滚动复用、主题往返、标注编辑、恢复默认、撤销、原生工程保存重开均保持正确的行色。
- 实际原生 ComboBox 和 RGB 输入可应用有效颜色；非法 RGB 不发布新修订；“还原”恢复已应用的颜色。
- 对黑、白、红、绿、蓝、灰、黄等颜色的全部交互状态核验正文对比度不低于 4.5:1。
- 保留五主题 100% 图像，以及颜色编辑器的 100%/150%/200% 离屏画面，见 [screenshots](screenshots/)。目视核对已修复深底行中普通 TextBlock 仍沿用主题前景的问题，原生输入、角色标记和周边布局保持。

首轮 29/34 通过；五条失败源于测试在撤销后保留选中行，却断言为常态背景。测试已显式切换到其他行，最终全部通过。首轮 [row-color.trx](results/row-color.trx) 保留其实际范围与失败记录。

## 运行与边界

```powershell
$env:OPTICAL_SYSTEM_DESIGN_ROW_COLOR_CAPTURE_DIR = "$PWD\validation\rechecks\2026-11-21\surface-row-color\screenshots"
dotnet test validation/rechecks/2026-11-21/surface-row-color/focused/RowColor.csproj -c Release -m:1 -p:NuGetAudit=false "-p:SurfaceTestOutputRoot=$env:TEMP\OpticalSystemDesign-20261121-surfaces" --logger 'trx;LogFileName=row-color-final.trx' --results-directory validation/rechecks/2026-11-21/surface-row-color/results
```

D 盘复制重复运行库时空间不足，本轮仅清理自己失败生成的 `focused/bin`；测试运行库改放 C 盘临时目录并启用硬链接。此重定向仅适用于定向测试工程，产品仍在默认 D 盘路径编译。默认沙箱临时目录有写入限制，实际测试在自动审核允许后使用系统临时目录后台运行。NuGet 漏洞数据源不可达使初次还原失败，本轮仅通过命令行关闭该网络审计，不更改产品 NuGet 配置，也不声称完成漏洞扫描。未启动或控制用户的桌面应用，未运行完整光学精度矩阵。

两组测试自然结束后，已核对并清理本轮固定 C 盘临时运行库中的 `row-color` / `drawing` 两个子目录；源码、截图、TRX、包缓存保留。[临时文件清理记录](runtime-cleanup.json)
