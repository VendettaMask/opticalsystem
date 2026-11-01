# 胶合与浸没镜组转换：验证证据

2026-11-01。实现及明确支持域见[说明](../../../../docs/CEMENTED_IMMERSION_NSC_2026-11-01.md)。解析控制不是实测镜组或商业软件认证。

最终488/488项定向回归通过，其中新增30项，零失败/跳过。默认Release App及独立测试驱动零警告/错误；能量/光程、采样与两组完整冻结链路通过，六批2439个旧冻结文件保持原字节。

- [最终汇总](final-verification-summary.json)、[最终定向测试](final-focused.trx)及[日志](test-final-focused.log)。新增三个测试类共30项，涵盖逐面介质/口径/镀膜、方向/OPL、能量、采样、回滚、文件和离屏属性编辑。
- [最终默认 Release App 构建](build-app-infinite-radius.log)包括两个内置模块，零警告/错误；[独立测试驱动构建](build-tests-infinite-radius.log)零警告/错误。早期默认输出锁定的失败构建保留，未关闭用户进程；没有将独立输出冒充默认构建通过。
- [空气工程](final-controls/air.staropt)、[数据库](final-controls/air.starrdb)、[相机实验](final-controls/air.camera-experiment.zip)及[能量/光子误差](final-controls/air.json)；[浸没工程](final-controls/immersion.staropt)、[数据库](final-controls/immersion.starrdb)、[相机实验](final-controls/immersion.camera-experiment.zip)及[误差](final-controls/immersion.json)。工程v20，实际数据库结果经过一次光子换算与完整RAW/ISP，冻结回放逐值相同。
- [空气曲面逐面比较](final-controls/curved-air.json)、[浸没曲面比较](final-controls/curved-immersion.json)、[完整分支能量](final-controls/full-branch-energy.json)、[40000条随机光线统计](final-controls/stochastic-statistics.json)。独立平板多反射解析公式只用于测试参考，不进入产品计算。
- [离屏编辑器](final-controls/assembly-editor-offscreen.png)显示冻结的三界面浸没镜组，已人工查看图像并通过属性应用/像面口径保留断言。没有操作用户桌面。
- [源码与输入快照](source-snapshot-map.json)、[四个程序集的92项范围内编译文档校验](compiled-source-audit.json)、执行文件[运行前](before-final-run.json)/[运行后](after-final-run.json)指纹。测试执行目录位于任务所有的C盘副本，避免并行聊天修改共享bin影响结果；真实路径见[执行位置](execution-location.json)，不能将共享bin后续状态当成本轮执行字节。

六份以前的BSDF/XYZ冻结证据共2439个文件只读核验。先前Blue Acrylic校准失败及其余商业范围仍保留原结论；本轮没有调整旧校准值、门槛或协议。新复合组件不升级旧Grid/1–11身份。

首轮487项全通过后，补齐Infinity平面半径的场景哈希/工程往返守卫，再以最终代码重建重跑同一范围。首轮[测试记录](pre-infinite-radius-roundtrip/final-focused.trx)、[执行指纹](pre-infinite-radius-roundtrip/after-final-run.json)与[原源码映射](source-snapshot-map-first.json)分别保留；其`source-snapshot`目录未改写，最终源码使用`verified-source-snapshot`。首轮汇总的XML计数对象序列化为空数组，以该轮TRX为计数依据；最终汇总改为明确的整数计数，不将两轮测试相加。

初次验证发现负半径法线定向错误、测试行含Infinity的序列化、目录玻璃真实k被过严拒绝，以及旧构造器的默认MaterialBefore与顺序追迹的实际传递介质不同，均已修正并保留失败记录。部分早期测试编译还受并行镀膜App的旧引用程序集影响；采用完整程序集引用后通过，没有删除或排除无关测试源。

复现需.NET 10及完整仓库资源。先正常构建App与测试项目，将测试输出复制到新的任务所有目录；对新证据目录运行`prepare-verification.ps1 -ExecutionDirectory <目录>`，用`tools/diagnostics/OpticalAssembly20261101/PdbAudit.csproj`核对其源码/编译校验，再运行`run-verification.ps1`与`verify-final.ps1`。脚本拒绝覆盖源码快照或在已封存目录重跑。最终[evidence-manifest.json](evidence-manifest.json)记录本轮全部证据的长度和SHA256；历史文档摘要不要求回写当前维护文档。

未运行全仓测试、原生桌面验收、CAD/GPU/优化公差或实拍RAW标定。机械边缘圆台与弱吸收标量实光线的限制见实现说明，不能将本轮转换结果直接称为任意完整整机。
