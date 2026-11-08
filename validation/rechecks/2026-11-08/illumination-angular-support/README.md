# 零光焦度与轴上相对照度

四条原相对照度请求的 OpticStudio 图表均没有可用数值。两档独立无偏振实光线追迹保留原模型、配置和波长，核对原视场与零渐晕因子。每个视场分别有 253 和 901 个瞳内节点，全部透射。 [轴上操作数及旧图表诊断](../wavefront-axis-reference/README.md) [逐档方向余弦](angular-support.json) [原生载荷与哈希](native-ray-evidence.zip) [归档清单](manifest.json)

## 同一平面内的准直光瞳

Grid Sag Example 的 #1146、#1168 在同一均匀介质中穿过零间距的光阑、网格矢高和像面。Etalon #2210 是同一像面上的平面镀膜，原处方定义了轴上和 1° 视场。镀膜及孔径可改变透射功率，但对这些原设置下的入射平面波没有光焦度。在每个视场，两档原生透射光线的像方方向余弦逐值相同，投影角面积严格为零。以图表最大像方照度作归一化时，分母为零。三项相对像面照度按物理定义不适用。

Core 复用同一平面准直光瞳的处方判定，在相对照度曲线及轴上归一化操作数入口报告不适用。原处方保持有焦标志，底层原始角面积接口维持独立。 [Ansys 相对照度定义](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Relative_Illumination.html)

## 唯一物理视场的相对值

Twyman Green Interferometer #2166 只有一个物理视场。253 和 901 条原生光线的透射方向展开相同，像面方向余弦网格的最大相邻有效三角形面积分别为 0.00147086 和 0.000412814，证实至少一个透射子域有正投影角面积。唯一物理视场对自身归一化的比值为 1，与正式 Core 曲线吻合。该独立对照覆盖原相对值，原生及 Core 的常规积分均未建立有效 F 数或绝对面积。 [原始实光线](native-ray-evidence.zip)

[请求生成](Prepare-AngularSupport.ps1) [光线掩码及角面积复算](Measure-AngularSupport.ps1)
