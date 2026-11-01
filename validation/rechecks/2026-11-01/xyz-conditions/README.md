# 已知观察条件 XYZ：最终证据

2026-11-01。结果：295/295定向测试；明确CIE1931 2°、D65、400–700 nm白Y=1条件的实测光谱派生XYZ，550 nm两例通过原门槛、三例失败；四组采用已通过校准的合成角形状Workbench完整链路通过；23组旧White实际追迹捕获复算通过。[实现、来源和限制](../../../../docs/XYZ_KNOWN_CONDITIONS_2026-11-01.md)

本目录不把Cornell原发布XYZ标记成观察者已核实，也不声称独立仪器XYZ、完整实测角分布/BTDF或全部商业认证通过。

## 最终入口

- [可机器读取的判定、最大误差和SHA](final-verification-summary.json)、[15个二进制/136个源与输入、旧993文件完整性](final-run-integrity.json)。[核查脚本](verify-final.ps1)不会构建或改写旧证据。
- [最终TRX](positive-final-focused.trx)、[测试日志](test-positive-final-focused.log)、[精确过滤与快照脚本](run-focused.ps1)、[运行前](before-positive-final-run.json)/[运行后](after-positive-final-run.json)。只运行该过滤范围，295不是全仓测试数。
- [最终拟合与导出汇总](final-positive/summary.json)，各材料目录内保存配对CSV/fit.json；仅garnet_red、krylon_blue含approved-550-calibration.json。959训练/480验证，固定6近邻，未改变NRMSE/最大绝对差门槛。
- [四组最终完整链路](final-real-chain-retry/summary.json)，各目录保存角控制源、trace/capture摘要、工程、相机ZIP。使用真实实测光谱派生校准和留出颜色，但角形状为合成均匀控制；QE亦为解析控制。各8,192样本、源1e−9 W、BTDF双面探测；删除原临时源后项目重开和冻结电子/RAW/ISP精确重放。
- [最终White功率复算](final-white-power/runtime6-native-power-audit.json)：23通过、0Unsupported、最大差1.4534376569230734e−6 W；复用旧原生捕获，不是新原生验收。
- [原测量条件审计](source-conditions-audit.json)、[下载身份](source-downloads.json)、[原始MAT解析](mat-inspection.json)、[全精度拟合诊断](final-mat/mat-fit.json)、[初始独立积分/核恢复/邻居数诊断](initial-audit/audit.json)。MAT拟合中的来源SHA占位符被显式标记DiagnosticOnlyProvenancePlaceholders，不用于批准导出。
- [最终默认App构建](build-app-final.log)、[最终测试构建](build-tests-final.log)、[最终链路驱动构建](build-driver-final.log)、[最终White审计构建](build-white-audit-final.log)，均0警告/错误，App包含两个内置模块。后续驱动/测试使用BuildProjectReferences=false复用此构建。

## 失败与中间输出

`first-positive`与`final-positive`批准的两个校准SHA相同。`positive-first/second.trx`为开发阶段定向运行，不累计到295。`first/second/third-real-chain`及日志保留原控制失败，透射零信号由FrontOnly朝向造成，第四次和最终控制明确BTDF双面接收。源功率在早期1e−11改为固定1e−9 W，不改校准、测量或门槛。

`build-chain-driver-first.log`是磁盘不足的失败构建。只移除本任务失败构建输出内Skia运行时副本；新驱动直接引用默认Application程序集，产品默认App仍有原依赖。`final-real-chain.log`保留系统临时目录原子重命名权限异常，`final-real-chain-retry.log`为同一已冻结驱动/产品字节、进程专属工作区TEMP/TMP的四组通过。`runtime-temp`只属本任务；不清理用户或其他聊天的临时目录。

旧证据根目录保持只读，三份manifest合计297+533+163=993文件全部核对。新目录最终由[封存脚本](seal-evidence.ps1)生成evidence-manifest.json（自身不入manifest），不可原地重跑输出覆盖封存证据。继续实验请用新目录。

## 数据与权利

见[NOTICE](NOTICE.txt)。Cornell来源页、MAT和原ASTM仅作为本机验证证据保留，没有推定其公开再分发许可，也没有加入CI夹具。新CI只含自制控制及上一阶段自制冻结ZIP。官方CIE表和既有归属/许可证文件仍在原冻结根目录；本轮保存实际表SHA及派生方法。未改写任何原始材料，不把相关拟合写成唯一光谱反演。
