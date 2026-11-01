# 双程零位检验与两种干涉仪证据

最终原110项为 **96 Pass / 4 Close / 3 Difference / 6 Incomparable / 1 Error**，修复前19/8/14/21/48。五配置360条固定原生光线的5,184次逐面核对通过。两类别426项为 **304/16/16/86/4**，原13项及所有前批Pass保持。

234项定向测试和正常默认双模块Release App构建通过，最终执行Core同一哈希。三配置波前128/256同档、双方收敛通过；泰曼–格林第二配置22项通过。第一配置Huygens128/256同档通过但双方未收敛；512受原Core直接PSF工作量守卫拒绝，未提高预算、未执行对应原生采集。其照度折叠和旧低采样参考仍开放。

- [范围、计数、运行身份与余项](summary.json)
- [最终426项逐请求结果](results-final.json)
- [相对前批的Pass保持检查](prior-pass-audit.json)
- [执行PDB与源文件对应](source-verification.json)
- [封存索引](archive-index.json)、[ZIP校验](archive-verification.json)
- [修复与未关闭问题](../../../../docs/OPTICAL_TESTING_PROPAGATION_REPAIR_2026-11-01.md)

`evidence.zip`只存一份按SHA-256寻址的payload，逻辑路径由索引映射到`payloads/<sha256>`。封存后逐项回读验证长度和哈希，再移除本任务的展开目录；镜头源文件、旧完整原生参考和前批证据保持。所有中间失败、旧低采样结论和512拒绝日志均保留，不能用新局部认证改写历史。

逻辑入口：`categories-final/`为最终426项；`rays-final/`为五配置固定光线；`native-rays/`、`native-chief-solve/`、`native-phase/`为独立原生采集；`phase-final/`为最终OPTH/OPDX控制；`convergence-final/`为最终128/256复核；`convergence-closure/`含512预算拒绝前的请求；`tests/`和`logs/`保留测试/构建/失败记录。旧全量原生数据复用现有主档，不重复收录。
