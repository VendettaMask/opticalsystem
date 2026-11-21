# 非序列彩色波片示例

非序列模式选择 **文件 → 示例 → 双折射 → Color Fringes**，在 ZMX 导入检查中点击
**应用完整场景**。随后打开追迹控制，执行 **清空并追迹**，在探测器查看器中选择
位置空间、辐照度或像素功率。彩色探测器默认勾选 **真彩色**；关闭后可使用原有
标量色表、归一化、平滑和对数显示。真彩色使用一个全图曝光，不改变 W、W/mm²
读数、剖面和数值导出；PNG 使用当前实际绘图。原例有 100×100 像素，分析光线数为
1,000,000，按原文件采用Sobol采样，低光线数仍会出现光谱抽样噪点。此前保存的反向
几何和旧光谱不会被猜测改写；请从原始ZMX重新导入以取得本轮修正。

## 导入与计算

导入器连续收集 `NOID`、`NSO*`、`NSCS` 和 `NSBR`，保留输入完整字节及815条记录。
五个对象分别有146、138、138、145、127条记录，不再被 `NOID` 截断。支持本例的
椭圆光源、两个矩形体、彩色矩形探测器和空物体。空物体只提供坐标参考，不参与求交。

矩形体按原始九个尺寸/倾角参数生成经过闭合性检查的真实平面网格，前后面满足
`z = z_vertex + x tan(θx) + y tan(θy)`，符号已通过原生单光线交点核验。共享斜面不插入虚构空气层。位置和参数拾取
通过依赖求值，拾取列0代表同列；忽略失效缓存，循环仍阻断。网格内部侧面编号3对应
原对象的侧面组0；前后面分别为1、2。本例无镀膜或散射面属性。

`GCAT` 明确绑定 `BIREFRINGENT:QUARTZ` 和 `BIREFRINGENT:QUARTZ-E`，避免与其他
材料库的同名石英混淆。`NSCD` 映射分裂、偏振、500段和0.001相对功率门限。
默认 `COFN` 数据库配置不再被误报为实际使用的外部资产；启用而未实现的属性仍阻断。

晶体支持本例的 `NSBR` 波片模式3：几何沿普通光线路径传播，同时保留两种互不相干
Jones 模态，每个模态的两个偏振分量保持相干。由入口平面的切向波矢连续条件求得
非常波矢，再沿该本征模式的能量方向投影，取得共同几何长度上的相位指数；相位差为
`2π (n_eff - n_o) L / λ`。晶轴随对象坐标变换，倾斜入口不能当作法向入口。
从各向同性介质进入波片时，Fresnel功率按普通/非常分量加权，保留共同路径的横向Jones幅值。
分支选择使用原始 `NSBR` 定义：0为反射及折射，1为仅折射，本例为1。忽略的反射功率
进入截断账目，不能重新分给透射。波片相位需要偏振及确定性分裂；
随机选支明确拒绝。此支持域不代表普通/非常光线空间分离模式、各向异性吸收或完整
各向异性界面振幅已经实现或通过原软件认证。

探测器参数16=1记录其局部X方向的电场功率，本例探测器绕Z旋转45°；通过光线仍携带
完整通量。分量1–3支持相干复场；忽略相位的分量4–9只支持非相干功率累积。直接追迹
与数据库重建共用同一检偏和光谱累积，晶轴、非常折射率、源光谱和颜色设置均参与
场景身份与过期检查。

## 光谱与颜色范围

原文件使用 XYZ=(0.9505,1,1.089)、440–640 nm、20个波段的光源颜色定义，而非系统中
重复的550 nm波长表。XYZ不能唯一确定光谱。本实现从零初值进行非负投影最小二乘
拟合，检验收敛后按辐射功率归一，并将完整权重及 `SpectrumProtocol` 保存至工程。
导入检查保留非阻断说明。已读取原软件处方报告，20个权重最大差小于1e-5；小差异来自
原始1 nm观察者与原软件旧观察者插值。发射空间/光谱采用真正的Sobol方向数，独立于后续
分支遍历；不要求与私有采样点顺序相同。采样算法及完整权重随工程保存。

实际到达各像素的逐波长功率乘以 CIE 1931 2° 标准观察者，再转换到 sRGB。观察者采用
原始1 nm数据及官方规定的线性插值/零外推；按全图三个线性RGB通道的共同峰值曝光，
再应用sRGB传递函数。不能先按Y单独缩放并截断其他颜色。显示超出sRGB色域
时裁切。原数据、校验值、CC BY-SA 4.0署名及许可随应用提供，见
[CIE说明](../third_party/colorimetry/CIE-NOTICE.txt)。

基础波片、显式源光谱、空物体和颜色检偏需要 **STAROPT格式26**；仅折射分支和Sobol源
设置需要 **格式28**。旧功能仍按自身最低版本
保存。工程保留嵌入网格、晶轴、两个材料名称、完整光谱和探测器设置；编辑、撤销、保存
重开保持这些数据。并未将106个菜单示例全部宣布为可执行。

实际追迹图、解析对照、回归与构建范围见
[实际原生比较与修正](../validation/rechecks/2026-11-21/nsc-color-parity/README.md)；
[初版实现记录](../validation/rechecks/2026-11-21/nsc-color-fringes/README.md)保留当时范围。

## 定义来源

- [波片模式与分裂要求](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Birefringent_Types.html)
- [矩形体参数和面倾角](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Rectangular_Volume.html)
- [彩色探测器与偏振分量](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Detector_Color_Object.html)
- [XYZ光谱拟合的非唯一性](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/The_spectrum_fitting_algorithm.html)
- [CIE标准观察者及官方数据身份](https://cie.co.at/datatable/cie-1931-colour-matching-functions-2-degree-observer)
- [ICC的sRGB矩阵与显示转换](https://registry.color.org/rgb-registry/files/sRGB.pdf)
