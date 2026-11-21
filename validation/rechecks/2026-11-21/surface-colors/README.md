# 逐表面颜色

用户截图中「表面颜色」为禁用占位。本次接入默认/常用/自定义 `#RRGGBB`，继续使用属性页「应用」「还原」和工程撤销重做。

颜色沿 OpticalSurface → 原生快照 → 应用 DTO → 2D/3D 场景传递，按原表面号与结构号定位；实体模型对应的前后表面分别使用自身颜色，玻璃侧壁和材料图例继续表示材料。默认颜色不写入快照新字段，历史工程保持原配色。保存重开、克隆与撤销均保留非默认颜色；错误颜色不会提交或新增历史。

控件使用现有原生下拉框、字体角色和主题资源，颜色值为用户显示数据。选中高亮保留。光学参数、光线颜色及数值公式未改变。

## 验证

定向测试源：[SurfaceColorTests.cs](../../../../tests/OpticalSystemDesign.Tests/SurfaceColorTests.cs)，并链接现有 SurfacePropertiesPanelTests / SurfacePropertiesServiceTests。覆盖保存/历史、旧快照、异常输入、场景传递及光线结果不变、五主题编辑与不同DPI离屏绘制、二维/三维与实体模型各显示模式的像素变化和恢复默认。

39项逐表面颜色及既有属性案例最终全部通过，见[逐项结果](final-results.json)；与全视场热图的9项案例合计48项，按案例去重，见[汇总与最终程序集身份](summary.json)。采用[轻量运行器](probe/Program.cs)直接调用原测试方法及断言，不代表完整VSTest/xUnit项目运行。五主题实际颜色编辑、Light100%/Dark150%/Isekai200%原生DPI组合图及各三维显示模式均已查看。

[普通主题编辑面板](captures/editor-Light-1.0.png)、[Dark150%](captures/editor-Dark-1.5.png)、[Isekai200%](captures/editor-Isekai-2.0.png)、[实体模型表面着色](captures/render-True-True-Solid-False.png)。截图由可写临时目录移入证据目录并核验SHA256。

默认Release主程序完整构建通过，0警告、0错误，见[构建记录](build-release-final.log)；最终词库补齐后又完成App最小构建，见[热图最终构建](../full-field-heatmap/build-final.log)。默认Debug输出被用户正在使用的程序（PID31680）锁定，未关闭进程，该构建未通过；离线NuGet漏洞数据查询曾阻断构建，定向还原关闭该网络查询，未改变依赖版本。磁盘紧张时仅将本轮临时测试运行时放在C盘，没有把备用输出当成默认产品构建。

初次VSTest运行已中断，不计通过；[初次直接调用](direct-results.json)的系统Temp写入限制通过将TMP/TEMP指向可写根解决。后续修正测试自身的无穷值JSON选项、无改动Apply不增加历史的断言，以及全视场UI案例误用的不支持采样值。保留[48项执行原记录](direct-results-final.json)、[失败项及新热图复核](direct-results-recheck.json)、[最终字体/语言与DPI复核](direct-results-localization.json)，最终结果取各案例最后一次执行。

没有控制用户桌面、重启当前程序、运行完整光学精度矩阵或发布软件。
