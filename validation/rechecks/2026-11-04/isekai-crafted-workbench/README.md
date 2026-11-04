# 历史：网络皮革与正式雕金书框

此方案将真实操作栏、设置、结果和原生页签放入同一书框，并使用下载皮革、生成雕金包角和铭牌。**用户随后否决质感和过于正式的边框；现行已转为[中世纪磨毛图纸](../isekai-frayed-vellum/README.md)。** 此处的截图和结果只记录历史阶段。

`final-scoped.trx`为133通过、0失败、6项导出检查按设计跳过；独立启用导出的`exports-final.trx`为23通过、0失败、0跳过。`final-material-and-resources.trx`为29通过、1失败，失败为旧资源测试仍要求Isekai命令页使用不透明纯色；后续按已接入皮革底图的透明资源契约修正。`book-integration.trx`、`book-repair.trx`保留了初始DrawingGroup不能记录DrawBitmap的修复过程；背景后来改用ImageBrush源矩形。以上不同版本不相加为现行通过率。

真实离屏截图在[workbench](workbench/)和[previews](previews/)；没有控制或重启用户窗口、推送远端、打包或发布。源图与科学数据保持原契约。网络材质的来源/许可及完整生成提示词留在[素材历史记录](../../../../src/OpticalSystemDesign.App/Assets/Themes/Isekai/README.md)。默认Debug输出曾被用户正在运行的进程锁住，未关闭该程序；随后使用正常默认Release目录构建和验证。
