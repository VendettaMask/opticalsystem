# 无焦POB原生边界契约核验（2026-10-28）

本阶段只核对官方定义、完善离线采集器，不修改Core光学算法、POB、原生参考或门槛，不执行Zemax。

## 已经确认的原生语义

Ansys **2026 R1** 的 [Polygon Object](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Polygon_Object.html) 明确说明：POB由三角形构成，R四边形在内部转为两个三角形；顶点应沿周界顺序连接。因此此前“原生是否剖分R”的问题已解决，不能继续列作完全未知。该页没有指定本例非共面四边形的对角线选择、法向一致判据或精确边界归属。

同版本的 [Special Considerations for Faceted Objects](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Special_Considerations_for_Faceted_Objects.html) 说明：邻接面法向不同时，公共边附近涉及Glue Distance吸收规则。这是相关契约差异，不能将Core的最近三角片规则视为原生全部边界行为，也不能未经数据直接加入一个猜测的吸收宽度。

当前事实仍为：42条Core探针的内部面命中和独立几何/Snell结果一致，模型三角片折痕解释1/h敏感性；现有原生中心/端点光线又有有效接纳记录。手册的通用规则不能反推该模型的具体Glue Distance、近共面识别阈值或内部路径，不足以证明应删除哪些点。

## 冻结证据中具体缺什么

核查 `afocal-reference-focus/evidence.zip` 中双筒配置1的 `native-centroid-rays/0065.../model.json`：现有导出含advanced、environment、rayAiming等，但没有NSC专用设置。advanced只含OPD/近轴/Huygens等选项，不能从中推出Glue Distance。没有替历史文件补默认值。

仍需2026 R1同处方、配置1/2、主波长/Field=1下：

1. 实际NSC/Glue Distance设置及单位；API未暴露时明确标记缺失。
2. 中心、±X/±Y五档h以及已有圆边界探针的第5/6面前后坐标、方向、error/vignette/intensity。
3. 原生矩形分三角形的对角线和内部面ID/法向/边缘归属规则。逐顺序面batch接口无法提供这些内部字段，不冒充完整NSC路径。

## 已实现的采集改进与验证边界

`AfocalDiagnosticCapture`增加只读SystemData接口发现：记录实际属性名，并导出名称含NSC/NonSequential的设置对象中可读取的原始标量。未找到的字段不填零，异常保存；明确 `completeNativeEdgeContract=false`。没有猜测安装版本的API属性名。

现有11任务采集器的两个双筒任务各由13点扩为33点：保留中心及圆边界探针，新增5档h的四方向中心探针；改为完整顺序面捕获，保留第5/6面与最终面。任务数和120秒/任务上限不变，不更改源镜头。末尾单独 `image-geometry.json` 仍为OPDMode.None，逐面OPD模式按既有标注解释。

离线准备检查通过：11任务、两个33点集合互不重复、源SHA-256保持、完整逐面请求设置正确。原生host**没有在专有API环境编译/执行**；这不是原生捕获完成或数值通过。对内部面ID仍需额外原生接口/定义，此脚本不捏造它们。外部采集默认不执行，必须显式`--run`且为有许可Windows环境。

## GitHub同步状态

用户最新“每一步记得同步GITHUB”授权提交及推送独立分支，不授权合并main或发布。第一阶段提交 `9507a25ecf98c257de6b6baf4b9b352a653dd2a2` 已推送且用`git ls-remote`确认远端精确一致。测试夹具改为直接读取已提交冻结归档与新哈希清单，40项契约测试通过；未提交新的大ZIP或大体积TRX，见`evidence-storage.json`。

GitHub Actions API查询返回Forbidden，因此没有确认远端CI结果，也未重试或绕过该拒绝。本阶段本地检查不等同CI通过。后续同步只推送同一工作分支，不自动开PR。


## 继续核查：原始NSCD存在，字段含义不能猜

已按实际编码读取冻结ZMX，发现原始行：
`NSCD 100 500 0 1.00E-03 5 1.00E-06 0 0 0 0 0 1 1000000 0 2`。
因此“model.json没有NSC设置”不等于“源模型没有设置记录”。当前读取器没有NSCD解释分支，不能声称保留了其原生边界语义；但本次官方检索没有得到该记录的字段位置映射，不能直接将某个1e−6赋予Glue Distance，也不能据通用默认填值。记录及源哈希现已进入离线采集清单，参考文件未改。

[2026 R1 Glue Distance说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Glue_Distance_In_Lens_Units.html)还明确其影响最小传播距离和一般曲面求交精度，并给出允许范围；这不是本例实际值或NSCD字段映射。五份已核查的冻结System Data文本没有提供可识别的Glue Distance标签。没有找到可据以修改导入器数值语义的充分证据。

本轮修复的是采集器的完成状态缺陷：旧脚本仅检查进程退出码，即使上下文为空也可正常结束。新增8个离线契约测试，添加实现前8项报错，修复后8通过；覆盖缺文件/坏JSON/错配置/空NSC/异常getter/非数值或负值/数值但语义未知，以及UTF-16原始记录保真。每个原生任务的证据状态与进程状态分开保存；仍未核实内部对角线/边缘语义时，整个采集批次保存所有结果后明确以退出码3标记不完整。不会因读到一个数字就宣布原生契约完整。

本轮未修改Core、POB或原比较门槛，也未运行原生或光学长测。当前不可继续修改物理行为所缺的最小输入是：同一冻结ZMX由原生2026 R1读回的NSC属性名/实际值/单位，或官方NSCD字段映射；对角线/边缘归属仍需对应定义。采集器发现接口的能力尚未经专有API编译运行验证，不把“准备好读取”写成“已经读到”。
