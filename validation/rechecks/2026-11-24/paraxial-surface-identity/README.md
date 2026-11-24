# 第 4 面近轴身份及编辑保留修复

2026-11-24。用户截图标题 `Crystal axis 45 degrees in YZ plane` 对应 95 个内置示例中的 `Birefringent prisms/Birefringent cube.zmx`。原文件第 4 面为 `TYPE PARAXIAL`，`PARM 1 100`、`PARM 2 0`，即空气焦距 100 mm、OPD 模式 0，随后厚度为 100 mm。原始文件与两配置均保留，原件摘要见 [summary.json](summary.json)。

## 官方依据及原因

[Ansys 2025 R2 官方近轴面说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Paraxial_sequential_surfaces_lens_data_editor.html) 明确：近轴面几何上为平面，但其光学作用是理想薄透镜，独立参数为焦距与 OPD 模式。不能因曲率为零就把它识别为普通标准面，也不能用普通折射交互替换。

原导入器已将本例映射到 `PlaneGeometry + ParaxialLensInteractionModel`，共享 Core 具有实际的方向与相位计算。问题有两处：

1. 界面只从几何类推导表面类型，近轴交互又落入默认“折射”名称，因此显示为标准面；薄透镜专用参数也不能表达此模型。
2. 通用几何／材料编辑的反射性同步没有保留近轴模型，可能把它覆盖为普通折射交互，改变实际计算作用。

## 修改范围

- 应用 DTO 提供结构化近轴 X/Y 光焦度及 OPD 模式。表格按实际模型识别“近轴面”或“近轴 XY 面”，不解析文件名、标注或本地化名称。
- 类型属性页显示实际焦距和 OPD，焦距按当前长度单位换算，零光焦度显示无穷。该页近轴参数和几何类型保持只读；未新增近轴参数编辑器或模型创建功能。
- 已支持的孔径、材料及其他通用属性编辑保留原近轴交互和参数。未支持的曲面或反射转换明确拒绝，并通过现有事务回滚。
- 原有近轴方向、相位、导入／导出及序列化公式未改。原示例文件未改，没有通过改写处方恢复外观。

## 验证

[首轮功能及原计算回归](regression.trx) 共 115 个不同用例：112 通过、3 个新增界面测试失败。三项失败来自测试未切到孔径页就查找其视觉控件，类型、焦距及三语显示断言此前均已通过。测试改为实际进入孔径页、修改并应用，再返回类型页截图，没有放宽产品断言。

[末轮新增专项](identity-final.trx) 7 项全部通过，覆盖原示例两个配置、改标注／材料／孔径后的模型保留、撤销重做、原生保存重开、零光焦度、独立 XY 光焦度、未支持转换的原子拒绝及三语真实控件。用末轮结果覆盖同名用例后，合计 **115 个不同用例通过**：108 项既有计算／表面属性回归和 7 项新增专项，不把重复执行相加。

实际离屏画面：[中文](ui/Light-zh-CN.png)、[英文](ui/Dark-en-US.png)、[日文](ui/Light-ja-JP.png)。画面中的其他已更新类型属于共享工作区的并行改动；本轮验收目标为近轴第 4 面。

主程序默认 Release 输出编译成功。验证期间其他任务持续新增测试，先后出现 GPU 文件的临时编译错误，以及 `NonSequentialSceneSettingsTests`、`BinocularViewerTests`、`SequentialSurfaceIdentityTests` 的未完成引用。保留各次日志；其中修正了 AppSettings 命名空间和主题字符串两处明确测试引用。末轮通过临时 `DirectoryBuildTargetsPath` 配置，仅从测试编译中排除 `BinocularViewerTests.cs` 和 `SequentialSurfaceIdentityTests.cs` 两个仍在编辑的类，没有排除产品代码或本轮七项检查。[末轮命令日志](identity-final.log)标注了此隔离范围。不能据此声称当前整个测试项目或完整测试套件已通过。日志另保留 NuGet 审计源连接警告。

没有重跑 95 镜头完整精度矩阵，没有新开原生桌面窗口、控制或重启用户程序，也没有新增外部原生数值捕获。该修复按既有共享 Core 定义维护模型身份，官方检索只用于确认定义。
