# 公差与精度首阶段集成验证

默认 Release 构建包含共享 Core、应用、测试和两个内置模块，零警告、零错误。Core SHA-256 为 `ef619509d9af578999561f1b65f7ffa938e89b5d9b10cd939df0fdc5697528a3`，与 95 支原请求复算运行时一致。见[构建日志](build-release.log)。

定向集成 **263 项，258 项通过，5 项失败，0 项跳过**。覆盖面形公差、机械支点、离屏编辑器与向导、Zernike 几何、真空腔压力、ZMX、复合光学组件及 FFT 有界窗口。新压力与 FFT 控制均通过。中英日词库当批 3104 条、2864 处界面引用，零漏译、零错误。见[集成 TRX](integration-release.trx)和[词库检查](localization-final.log)。

五项失败来自 ZMX 扩展回归。修改前保留的 11 月 4 日 Release 产物复现了全部五项，测试名称和失败消息逐条一致。两项涉及历史样例中未支持的环曲面圆锥或多项式，一项为坐标断点保留后的面数断言，另两项分别为双圆锥面导出和 Binary 2 导入的旧断言。见[修改前复验 TRX](prior-import-reproduction.trx)。该旧 Core 的 SHA-256 为 `311d071f66aa8babfddc76609b587ac84e75960c899e5f8b546ecc8f95447b03`。

本记录对应 95 支精度和首批面形及机械支点改动。后续 ISO 面形公差另记验收范围。验证在后台执行。

## ISO B/C/D 与精度后续的联合构建

默认 Release 应用和对标项目均为零警告、零错误。应用内 Core 和 Application 的 SHA-256 与 95 支后续复算逐个匹配。见[程序集核对](followup-cohort.json)、[应用构建](build-workbench-final.log)和[对标构建](build-comparison-final.log)。

最终应用定向 **284 项，279 项通过，5 项失败，0 项跳过**。五项失败与上方修改前证据的名称及消息逐条相同。新增 ISO B/C/D、机械支点、表面偏心倾斜、Chebyshev 一阶和 FFT 定向回归均通过。一阶对标的独立 9 项通过。最终词库 3116 条、2869 处引用，零漏译、零错误。见[应用 TRX](workbench-followup.trx)、[一阶对标 TRX](../first-order-state/first-order-comparison-final.trx)和[最终词库检查](localization-iso-final.log)。
