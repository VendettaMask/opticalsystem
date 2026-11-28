# 近轴面行色

2026-11-26。用户否定初稿金色，最终改为偏黄的浅草绿；反射面保持原青绿色。以真实 `ParaxialLens` 模型分类，包含近轴 XY 及零功率模型，不解析标注或本地化名称。坐标／反射识别及显式用户行色优先规则保持，普通材料面不抢占近轴行色。

## 最终绿色结果

最终 10 项定向检查通过，0 失败、0 跳过。六组实际界面覆盖 Light 中文／日文、Dark 英文、Lumen 中文、Isekai 固定英文和 Pixel 中文；普通、悬停、选中及选中悬停四态逐项检查原生行背景与冻结单元格。正文对比度至少 4.5，自定义行色覆盖／清除及主题往返通过。既有近轴实例编辑、撤销、原生保存重开、XY 与零功率类型检查通过，原示例文件哈希保持。

- [Light 中文](green/Light-zh-CN.png) · [Light 日文](green/Light-ja-JP.png)
- [Dark](green/Dark-en-US.png) · [Lumen](green/Lumen-zh-CN.png)
- [Isekai](green/Isekai-en-US.png) · [Pixel](green/Pixel-zh-CN.png)

调色板和实际镜面颜色在普通／选中等四态保持不同色相，比较见[验证数据](results.json)。色相差是颜色记录，不替代人工辨识或用户审美反馈；行类型文字继续保留。

## 构建与检查范围

初次标准构建曾因工作区 `Application → CoatingDesign.Engine → Application` 引用循环报 MSB4006。前期单独界面检查使用已有依赖；随后工作区项目引用调整，已重新完成正常 Release 主程序及全部依赖构建，并以正常项目引用再次构建和运行定向检查。最终没有使用 `BuildProjectReferences=false` 或 `MSBuildCopyContentTransitively=false`。测试输出继续隔离在仓库外，共享默认输出中的其他 testhost 没有被关闭或中断。

正常 Release 主程序构建完成，0 编译错误、3 项 NU1900 连接警告，未关闭 NuGet Audit；没有运行全解决方案测试或全镜头数值矩阵，也没有启动／控制用户的前台程序。

## 回执

[最终绿色 TRX](green-final.trx.gz) · [最终结果和程序集身份](results.json)。初稿金色的检查及首次绿色检查都因新增 Isekai 用例错误期待中文摘要而有 1 项失败；产品原规则为该主题固定英文。修正用例输入后最终绿色 10 项通过，未改动语言规则或删去断言。保留[初稿原始结果](initial.trx.gz)、[首次绿色原始结果](green-first.trx.gz)及[前期使用已有依赖的绿色检查](green-cached.trx.gz)，均与最终正常依赖检查分列。被否定的金色截图不作为当前画面保留。
