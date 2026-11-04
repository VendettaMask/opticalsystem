# 图像导出设置移除底部示意框

用户要求不再绘制最终像素/DPI文字下方的空白矩形。设置窗口已移除整个画布/源图/图签缩略示意控件及其118 DIP占位，保留最终像素与DPI文字、分辨率/比例/DPI编辑和保存行为。无其他使用方的`ExportCanvasPreview`已删除；实际图纸、PNG和报告的外框不属于此次删除范围。

既有设置回归检查文字汇总为导出设置区最后一个元素；截图滚动至窗口底部，以同时显示汇总和后续字体预览区，确认没有额外示意框/占位。自定义分辨率的断言使用实际最后一项，兼容此前新增的4:3预设；原DPI、保存失败回滚、持久化和导出像素断言保留。

默认Release App、双内置模块依赖及测试构建完成，最终构建/运行日志无编译警告或错误。[9/9定向用例通过](settings-and-export-final.trx)，零失败/跳过，见[完整输出](settings-and-export-final.log)：三种语言设置、版式保存隔离/失败回滚、旧设置默认与持久化、原图比例锁定/拉伸、1080P/1440P/4K的真实PNG像素与DPI及报告源图比较。调整截图滚动位置后的三项设置复验单独记录在[最终截图复验](settings-captures-final.trx)及[日志](settings-captures-final.log)，与前述九项重叠，不累加用例数。首次补截图夹具错误地将Window.Content直接断言为ScrollViewer，实际包含ThemeChromeLayer包装，导致[3项失败](settings-captures.trx)；改为沿汇总元素的可视祖先定位现有ScrollViewer，产品代码和原设置/导出断言未改。

实际界面：[中文浅色](settings-default-zh-CN-Light.png)、[英文黑洞](settings-default-en-US-Dark.png)、[日文浅色](settings-default-ja-JP-Light.png)。`wavefront-*.png`为既有明确合成波前输入的导出回归，不是新增光学数据。

最初沙箱构建/测试调用在等待测试宿主时被中止；随后沙箱重试的[原始记录](settings-and-export-retry.trx)报告vstest/datacollector本地连接90秒超时，未完成测试，不能计为通过。允许本地测试进程通信后完成上述默认输出构建和全部定向验证。全部使用Avalonia Headless/Skia，不打开前台程序、不控制或重启用户应用。此次验证只覆盖所列设置与导出行为，不代表全仓、数值精度、原生桌面或发布验收，见[当前状态](../../../../docs/CURRENT_STATUS.md)。
