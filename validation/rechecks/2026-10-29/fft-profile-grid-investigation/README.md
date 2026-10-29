# 双筒配置2 FFT边界调查证据

**未修复：配置2仍335点超限，最大归一化强度差0.0011946610381988604。**
原门槛`1e-5 + 0.002 × |原生参考强度|`、处方、指纹、坐标、采样及原生参考保持。
本阶段不增加光学Pass，不替代完整170项复核。

- [调查说明与反证](../../../../docs/AFOCAL_FFT_GRID_INVESTIGATION_2026-10-29.md)
- [assessment.json](assessment.json)：17次原生任务的有效性、三角片反序差异、Gaussian推断、11种失败试验、原五份最终统计。
- [verification.json](verification.json)：证据SHA-256、长度、映射计数、12项实际守卫执行和Core身份。
- [evidence.zip](evidence.zip)：采集、原始失败、诊断源码、关键试验源码/补丁及逐请求输出；`entry-map.json`保留去重前逻辑路径与字节身份。

本批新增**10个有效原生采集、6个无效结果、1个失败**，与此前
[20个任务的恢复阶段](../afocal-native-resume/README.md)分别记账。
正常顺序显式T两配置均重现原R路径；反序配置2出现约1.56e-9方向分量差，
不能称顺序无影响。直接法向来自独立复制的NSC组，不能冒称完整原系统的三角片ID。

Gaussian推断的有效文件是`gaussian-coordinates-reference-corrected.json`。
`gaussian-coordinates.json`归一化处理错误，`gaussian-coordinates-corrected.json`
用了尚未完成重编译的旧二进制，二者均为无效历史输出。初版审计账目在最后一次
相交试验之前生成，保留为`assessment-initial.json`；以本目录最终账目为准。

所有产品试验均撤回。隔离工作树`D:\Projects\opticalsystem-afocal-335`的Core
源码匹配`cf61c001dc1a5f36afdd84a4d24d7d020b130d99`，最终五份截面统计与
本批前完全相同。主工作区同时有其他任务修改，本证据只认证这里记录的隔离Core。
最终Release诊断及Framework host编译通过；首次测试空退出未计通过，恢复依赖后
真实执行POB/配置守卫12/12。没有全仓测试、前台窗口、桌面输入或发布。

所有原生任务ZMX前后哈希保持，安装的LH/RH POB字节保持，独有临时POB已清理。
证据不包含专有API程序集，也不向产品提供原生掩码、原生尺度常量或替代光学引擎。
原始冻结参考仍为独立前置资产：
[`afocal-fft-energy/evidence-current.zip`](../../2026-10-27/afocal-fft-energy/evidence-current.zip)，
SHA-256 `235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1`。

继续调查应从两条已确认的事实出发：近共面三角片择片具有顺序敏感性，原生FFT
边界映射接近正向瞳孔尺度且微小比例差会改变落界节点。两者的完整规则仍缺，
不能通过轴交换、选择差分步长、导入原生掩码或放宽逐点标准关闭问题。
