# 椭球浮动光阑与 1024 档 Huygens PSF

## 浮动光阑

顺序追迹先用各视场的子午边缘光线判断浮动光阑的环形边缘落在椭球哪一侧，再以该边缘的局部轴向深度裁光。半径达到赤道后，边缘限制在赤道。原生对照中的偏心浮动光阑仍采用同一轴向边缘。显式圆孔保留投影平面上的裁光。双轴离轴的锥束采用 Zemax 对照确认的旋转顺序。 [OpticStudio 半口径与浮动光阑](https://ansyshelp.ansys.com/public/views/secured/zemax/v252/en/opticstudio_user_guide/OpticStudio_Help/topics/Semi_Diameters.html)

15 组共 273 条光线逐条复验，36 条在反射面裁光。ZMX 导入、STAROPT 快照恢复和 ZMX 往返均得到相同判定，逐面坐标最大差为 5.26×10⁻¹¹ mm。 [逐光线原生参考](clipping-native.json) [分组误差](clipping-results.json)

当前曲面裁光支持居中、首面为光阑的闭合旋转二次曲面及无渐晕变换的角度视场。宽锥角下的非居中光阑、其他光阑位置以及相应物点落在闭合曲面外的组合仍会报出支持范围错误。

## 1024 档 Huygens PSF

原有统一阶数的求和方案超出预算时，按各光源落入傅里叶格点后的相位余量选取展开阶数。每项尾差仍有解析上界，累计误差界由各项的权重和尾差组成。估算工作量和工作区继续受原有上限约束。

320° 菜单反射镜在 1024 档保留全部 818,753 个有效光瞳节点，估算工作量 90,319,867 次，求和工作区约 93.8 MiB。64×64 像面使用固定 0.25 µm 间距。与 Zemax 同档逐点比较，最大辐照度差为 6.30×10⁻⁹，NRMSE 为 5.61×10⁻¹⁰。512→1024 加密时，本软件与 Zemax 各自的 NRMSE 都约为 1.26×10⁻⁴，通过原阈值。 [对照指标和计算预算](huygens-comparison.json) [当前 1024 档网格](huygens-1024-core-grid.json) [冻结 Zemax 网格](../object-cone/numeric-controls.zip)

## 95 支菜单镜头

原矩阵重放 2,904 条可执行请求。3,024 条原请求的判定逐项一致，共 **2,518 通过、22 接近、31 差异、433 不可比、20 求值错误**。25 条请求的比较指标数值有变动。最终程序集重新导入全部 95 份处方，115 份配置快照和请求映射逐字节一致。最后的双轴旋转修正只在锥角路径中执行。唯一使用该孔径的菜单反射镜没有冻结请求，其光线和 Huygens PSF 用最终程序集单独复验。30,700 份冻结参考载荷通过哈希核对。 [复验身份与结果](summary.json) [指标变动请求](changed-requests.json) [逐镜头](../object-cone/by-lens.csv) [逐项余项](../dense-huygens-conic/remaining-requests.csv)

199 项定向回归通过。Sample Spectrometer 的 2048 档 PSF 另行复算，1,024 个物理网格点与上次结果一致。 [测试记录](core-release.trx)
