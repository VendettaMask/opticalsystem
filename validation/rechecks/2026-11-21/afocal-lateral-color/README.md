# 无焦垂轴色差的角度与衍射尺度修复

用户截图中的 `Afocal Riflescope.ZMX` 已按同一原始文件复算。修复后曲线在视场 1.3° 附近达到 **−0.2806359652 mrad**，换算为 **−0.9647553830 角分**；艾里斑边界为 **±0.1778497287 mrad**，约 **±0.6114023304 角分**，与原生截图的曲线位置、弯曲方向和边界尺度相符。

## 原因与实现

旧垂轴色差分析未判断实际像方是否无焦：始终计算两波长在末面上的 Y 截高差并乘 1000，标为 µm；艾里斑也始终由有焦工作 F/# 得到。无焦读出面的位置会改变截高，但不会改变出射角。截图中的错误艾里尺度将自动横轴撑到约 ±1000 µm，使错误的截高曲线显得接近竖直。

按[原生分析定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Lateral_Color.html)，无焦垂轴色差应比较两个波长主光线的像方角度。产品现在通过真实 `ImageSpaceAfocal` 标志选择物理量：

- 真实光线使用末面局部坐标中的 `atan2(direction.Y, direction.Z)` 之差；近轴模式使用对应出射方向的角度之差。
- 产品统一使用 mrad，并发布 `Angle` / `Milliradian` 类型化坐标；数据中的艾里半径、手动图幅键也明确为角单位。
- 艾里角半径使用既有共享无焦出瞳尺度。原文件出瞳直径约 4.0266578162 mm，主波长 0.587 µm，得到 `1.22 λ / D = 0.1778497287 mrad`。
- 有焦模式保留像面截高差、µm 坐标及原有随视场变化的艾里边界。最短/最长、全部波长相对主波长、手动量程和隐藏静态图例的既有规则保留。

本轮未修改光学处方、光线追迹、材料色散、原生截图或任何参考容差。

## 画面与单位

- [同物理量程的实际新界面](lateral-color-native-range.png)：左侧为 mrad，手动半量程 `π×1000/(180×60) = 0.2908882087 mrad`，与右侧原图 ±1 角分覆盖同一物理范围。
- [默认自动量程的实际新界面](lateral-color-default.png)：产品自动范围为 ±0.5 mrad；保留该图说明手动配准量程不属于产品默认。
- [同量程数据](view-native-range.json)和[默认数据](view-default.json)保留三条曲线、角度单位、设置和报告。

`1 mrad = 3.4377467708 角分`。两边坐标数字不同不代表物理量不同，必须先换算再比较。图册保留旧捕获并引用本轮画面；本专项没有重新捕获原生数值表，因此上述原生一致性结论限于现有截图尺度与曲线形状，不能记作新的完整数值精度验收。

## 验证

[新增定向测试](../../../../tests/OpticalSystemDesign.Tests/AfocalLateralColorTests.cs)及两项既有有焦检查以直接调用断言执行，**9 组全部通过**，见[结果](checks.json)和[日志](probe.log)。检查包括真实/近轴模式分别与独立方向角计算一致、两模式将末端无光焦度读出面平移 100 mm 后角度曲线和艾里边界不变、原 Riflescope 的角度尺度、全部波长与手动图幅、有焦截高定义及已有设置/图形回归。

另有 **3 组最终应用检查通过**，见[渲染日志](render.log)：新的英文/日文单位文本可正确解析，两种量程的真实 `AnalysisPanel` 都绘制三条 mrad 曲线，且没有恢复已取消的静态图例。截图为后台离屏渲染保存的 PNG，没有操作用户桌面。

默认 Core 编译及最终默认 App Release 构建均零警告、零错误，见[Core 日志](core-build.log)和[最终 App 日志](app-build.log)。首个渲染探针遗漏应用服务层的类型化图形分派而失败，见[初始日志](render-initial.log)；补上正常应用使用的目录分派后渲染通过，未为该探针改变产品实现。

本轮没有执行完整 VSTest 工程、95 支样例的全分析矩阵、全部报告格式或新的原生逐点捕获。

输入身份：`Afocal/Afocal Riflescope.ZMX`，SHA-256 `36db5e0bd00f9435dbd0495d0bff5f5ccfa121ae95114dc75b11efdccf66570b`。原生依据为[现存图册原图](../all-output-visual-comparison/representative-six/afocal-riflescope/zemax/lateral-color.JPG)，独立方向角逐场数据保存在[independent.json](independent.json)。
