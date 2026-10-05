using OptilandWorkbench.Core.Optimization;

namespace OptilandWorkbench.Application.Services;

internal sealed record MeritOperandReference(string Category, string Calculation);

internal static class MeritOperandReferenceCatalog
{
    private const string CompatibilityCalculation =
        "当前仅支持从 ZMX 导入、在工程中保留原始参数并再次保存；计算引擎不会执行该操作数，也不会用零值冒充计算结果。";
    private const string GradientFirstOrderScope =
        " GRIN 使用共享连续一阶传递与界面局部折射率，当前支持共轴、旋转对称、轴上可微的 Gradient 1～5 及标准透射面/平面；偏心、倾斜、反射、横向一次梯度和 X/Y 不同梯度明确拒绝。Gradient 5 可带显式色散系数，其余模型无色散；原生数值捕获尚未完成。";

    internal static MeritOperandReference Describe(string code, bool compatibilityOnly)
    {
        var canonical = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (canonical is "FNUM" or "RADI" or "THIC" or "RWFE")
            return new MeritOperandReference("本程序扩展", "本程序自定义操作数，不是 Zemax 2026 R1 的 MFE 操作数。" + CalculationFor(canonical));
        if (ZemaxOperandRegistry.IsDocumentedUnused(canonical))
            return new MeritOperandReference("Zemax 未用名称", "官方 2026 R1 手册标记为 Unused；仅保留旧文件中的名称和参数，不列入待实现功能，不执行计算。");
        if (canonical is "OGSS" or "SPHD")
            return new MeritOperandReference("Zemax 定义待核实", "已核对官方 2026 R1 API 枚举中存在此名称，但未取得公开计算定义和实机参数证据；仅兼容保存，不能据名称猜测计算含义。");
        if (compatibilityOnly)
        {
            return new MeritOperandReference("Zemax 兼容保留", CompatibilityCalculation
                + (canonical == "LPTD" ? " LPTD 是 Gradient 5 的轴向单调性约束。共享 Core 已有 Gradient 5 四次轴向分布及色散、追迹和保存基础；桌面系数编辑和受支持系数优化可用；LPTD 约束残差、倾斜边界与原生验证仍未完成，不能用端点折射率差代替。" : string.Empty));
        }

        return new MeritOperandReference(CategoryFor(canonical), CalculationFor(canonical));
    }

    private static string CategoryFor(string code) => code switch
    {
        _ when ZemaxOperandRegistry.IsGradientIndexControl(code) => "GRIN 材料约束",
        "MCOV" or "MCOG" or "MCOL" or "CONF" or "ZTHI" => "多重配置",
        "RRET" => "偏振与镀膜",
        "CMGT" or "CMLT" or "CMVA" or "CIGT" or "CILT" or "CIVA" or "CEGT" or "CELT" or "CEVA" => "镀膜层参数",
        "CODA" => "偏振与镀膜",
        "HYLD" => "实际光线",
        "BFSD" or "TSAG" => "面形与制造",
        "RELI" or "EFNO" => "照度与有效 F 数",
        "ZERN" => "Zernike 系数与波前统计",
        "ABCD" or "DIST" or "DISA" or "DISG" or "DIMX" or "SMIA" or "FCGS" or "FCGT" => "畸变与场曲",
        "GBPD" or "GBPP" or "GBPR" or "GBPS" or "GBPW" or "GBPZ" => "近轴高斯光束",
        "PRIM" or "SVIG" or "CVIG" or "IMSF" or "FDMO" or "FDRE" => "批次内系统状态",
        "SPHS" or "PSLP" or "DPHS" or "QSLP" => "表面相位",
        "DENC" or "DENF" => "衍射圈入能量",
        "SSAG" or "SSLP" or "SCRV" => "指定点面形",
        "GENC" or "GENF" or "ERFP" => "几何能量与边缘响应",
        "STRH" or "CEHX" or "CEHY" => "惠更斯 PSF",
        "MTHA" or "MTHS" or "MTHT" or "MTHN" or "MTHX" => "惠更斯 MTF",
        "GMTA" or "GMTS" or "GMTT" or "GMTN" or "GMTX" or "MTFA" or "MTFS" or "MTFT" or "MTFN" or "MTFX" or "MSWA" or "MSWS" or "MSWT" or "MSWN" or "MSWX" => "MTF 与方波传递",
        "CENX" or "CENY" or "CNPX" or "CNPY" or "CNAX" or "CNAY" => "光线束质心",
        "RWCE" or "RWCH" or "RWRE" or "RWRH" or "MWCE" or "MWCH" or "MWRE" or "MWRH" => "波前误差",
        "GSCE" or "GSCH" or "GSRE" or "GSRH" => "几何点列半径",
        "GLCX" or "GLCY" or "GLCZ" or "GLCA" or "GLCB" or "GLCC" or "GLCR" => "全局位置与姿态",
        "RAGX" or "RAGY" or "RAGZ" or "RAGA" or "RAGB" or "RAGC" => "全局光线数据",
        "DXDX" or "DXDY" or "DYDX" or "DYDY" => "光线扇导数",
        "BLNK" or "DMFS" or "REQS" or "GOTO" or "ENDX" or "OOFF" or "SKIN" or "SKIS" or "USYM" => "说明与控制",
        "CONS" or "SINE" or "COSI" or "TANG" or "ASIN" or "ACOS" or "ATAN"
            or "ABSO" or "SQRT" or "RECI" or "LOGE" or "LOGT" or "SUMM" or "PROD"
            or "PROB" or "DIVB" or "DIVI" or "DIFF" or "EQUA" or "MAXX" or "MINN"
            or "OSUM" or "QSUM" or "OPVA" or "OPGT" or "OPLT"
            or "ABGT" or "ABLT" => "行数学与约束",
        "RSCE" or "RSCH" or "RSRE" or "RSRH" or "RWFE" or "OPDX" or "OPDM" or "OPDC"
            or "TRAC" or "TRAR" or "TRCX" or "TRCY" or "TRAX" or "TRAY"
            or "ANAC" or "ANAR" or "ANCX" or "ANCY" or "ANAX" or "ANAY"
            or "MECA" or "MECS" or "MECT" => "像质与波前",
        "REAX" or "REAY" or "REAR" or "RANG" or "REAZ" or "REAA" or "REAB" or "REAC"
            or "RENA" or "RENB" or "RENC" or "RETX" or "RETY"
            or "RAID" or "RAIN" or "RAED" or "RAEN" or "OPTH" or "PLEN" or "MNAI" or "MXAI" or "MNRE" or "MNRI" or "MXRE" or "MXRI" => "实际光线",
        "MNIN" or "MXIN" or "MNAB" or "MXAB" or "INDX" or "MNPD" or "MXPD" or "GTCE" or "GCOS" => "玻璃数据约束",
        "PMVA" or "PMGT" or "PMLT" => "表面数据与边界",
        "SCUR" or "SDRV" => "表面几何",
        "TRAI" or "BSER" => "实际光线",
        "LONA" or "AXCL" or "LACL" or "SPCH" => "焦移与色差",
        "SPHA" or "COMA" or "ASTI" or "FCUR" => "三阶像差",
        "PETC" => "一阶量与系统数据",
        "CARD" => "一阶量与系统数据",
        "CVOL" or "TMAS" or "VOLU" => "制造与尺寸约束",
        "DSAG" or "DSLP" or "DCRV" => "面形统计",
        "PARX" or "PARY" or "PARZ" or "PARR" or "PARA" or "PARB" or "PARC"
            or "PATX" or "PATY" or "PANA" or "PANB" or "PANC" or "YNIP" => "近轴光线",
        "TCVA" or "TCGT" or "TCLT" => "表面热膨胀约束",
        "DMVA" or "DMGT" or "DMLT" or "MNDT" or "MXDT" or "BLTH" => "制造与尺寸约束",
        "AMAG" or "LINV" or "PIMH" or "OBSN" or "EFLA" => "一阶量与系统数据",
        "SAGX" or "SAGY" or "NORX" or "NORY" or "NORZ" or "NORD" => "表面几何",
        "EFFL" or "EFLX" or "EFLY" or "ENPP" or "EPDI" or "EXPP" or "EXPD"
            or "ISFN" or "SFNO" or "TFNO" or "WFNO" or "FNUM" or "ISNA" or "PMAG" or "PETZ"
            or "WLEN" or "POWR" or "TOTR" => "一阶量与系统数据",
        "RADI" or "THIC" or "CVGT" or "CVLT" or "CVVA" or "COGT" or "COLT" or "COVA"
            or "MNCV" or "MXCV" or "MNSD" or "MXSD" => "表面数据与边界",
        _ => "厚度与结构边界"
    };

    private static string CalculationFor(string code) => code switch
    {
        "CMGT" or "CMLT" or "CMVA" or "CIGT" or "CILT" or "CIVA" or "CEGT" or "CELT" or "CEVA" => "Surf/Layr 为表面及从 1 开始的物理相干膜层编号。CM 读取基础厚度的倍率，CI/CE 读取材料 n/k 的无色散加性偏移，均无量纲。GT/LT 在满足下/上限时返回 Target；0 可选择全部表面或全部层，本地用最违反约束的一层（GT 取最小、LT 取最大），空集或指定层不存在时报错。VA 只接受正编号并返回实际参数。膜层编辑器支持固定/变量及 STAROPT 保存；基础材料与厚度不被调整值覆盖。倍率 0..10，调整后 n>0、k≥0，禁止零基础厚度设倍率变量。原生多层边界聚合、层倍率开关/拾取和 ZMX 膜层字段尚未验证，导入行保持只读；不声称数值等价。",
        "CODA" => "本地六槽 Surf/Wave/Field/Px/Py/Data；Surf=0 为像面，Wave/Field 为正编号。始终使用系统 Jones 输入，即使非偏振开关打开。|Data|=0 为全路径偏振强度、8 为非偏振强度；1/2/3 为目标界面 R/T/A，4/5 为功率归一化透射复振幅实/虚部，6/7 为反射复振幅实/虚部，仅 1..7 的负值选择 S、正值选择 P。101..106 为目标局部 Ex/Ey/Ez 出射实/虚部；110 为 Ex−Ey 主值相位差、111..113 为分量相位（弧度）；121/122 为局部 XY 椭圆长/短轴振幅，123 为主轴角（度）。零分量相位、圆/零椭圆主轴角明确报错。采用正时间约定，绝对电场包含从生成光线起点开始的光程相位；共同相位在强度/椭圆/相位差中抵消。复用正式均匀介质、Fresnel、TIR 和物理相干膜层；GRIN/各向异性/散射/光栅等仍拒绝。原生列、系统偏振文件映射、绝对相位原点及数值等价未捕获，ZMX 行禁用只读。",
        _ when ZemaxOperandRegistry.IsGradientIndexControl(code) => "Surf/Wave/未用/未用/未用/未用；Surf 为有下一边界的实际 GRIN 入口面，Wave 为正编号。直接使用正式 Gradient 1～5 空间材料，不需要光线追迹或近轴系统。I1..6 的具体位置见名称与上级六点说明，侧向距离为两端较大的净半口径；本地曲面取点使用实际光学矢高和中心厚度，不使用机械半径/延伸区。GT/GRMN 返回 min(n,Target)，LT/GRMX 返回 max(n,Target)，VA 返回 n；GRMN/GRMX 只比较这六点，不搜索全体积。DLTN 为毛坯轴向两端 n 的绝对差，凹面用各自净半口径处矢高扩展毛坯，不搜索内部非单调 n 的极值。支持局部横轴对齐的共轴标准面/平面；整体位姿不改变局部结果，非球面、边界相对倾斜/偏心明确报错。Gradient 5 使用所选波长的色散折射率，其余模型无色散；原生列、曲面点 Z 约定及数值尚未实机捕获，ZMX 导入保持禁用只读；本地编辑、保存和优化可用。",
        "RRET" => "Ring/Wave/Hx/Hy/Jx/Jy/X-Phase/Y-Phase 八槽；Ring=1..32，Wave=0 为按谱权重合成，否则取指定正编号。Jx/Jy 为非负相对振幅，不能全零；相位为度，输入先归一化。正式偏振追迹采用系统设置的物面局部参考轴、正时间约定；薄膜复系数按该时间约定转换，保留 TIR 和每次旋转的入射面。使用 6 方位高斯圆瞳，对像面局部 Ex/Ey 的主值相位差 [-π,π] 求加权 RMS（弧度），不去均值、不按出射强度再加权，不是 Jones 矩阵本征延迟。共用正式界面与相干多层膜求解，保留相对相位；共同传播相位在 Ex/Ey 中抵消。仅均匀各向同性确定性介质、未渐晕圆瞳；零 Ex/Ey、GRIN、散射、衍射等报错。原生绕相/权重/参考轴、八列和数值尚未捕获，ZMX 导入禁用只读；应力双折射仍待接入。",
        "HYLD" => "Surf/Wave/Hx/Hy/Px/Py；Wave=0 选主波长，Hx/Hy 为归一化视场，Px/Py 须在单位圆内。共享追迹记录交点处两侧折射率，返回 |n−n′|×(1−cosθ)：n′>n 时取入射角，否则取出射角，法线朝所选光线方向。使用稳定的小角度计算；值无量纲，是角度敏感性惩罚，不是良率百分比或 Monte Carlo 公差结果。支持普通折射界面及正式 GRIN 局部折射率；反射、全反射、衍射、理想薄透镜、目标面散射、无穷远物面、失追迹和渐晕明确报错。原生列映射和数值尚未实机捕获，ZMX 导入仍禁用只读；本地编辑、保存和优化可用。",
        "TSAG" => "Surf/Mode/X/Y/Z/Tilt About X/Y/Z 八槽。局部参考坐标为 mm，倾角为度；沿共享坐标变换 Rz·Ry·Rx 旋转的 +Z 测量轴，双向调用正式交点求解，返回较近交点的有符号距离。Mode=0 在净半口径加延伸区以外使用恒定边缘矢高，当前限标准面及偶/奇次非球面；Mode=1 使用完整几何方程，不裁切孔径。表面全局位姿不参与局部测量。相切、无有限交点或两侧等距离歧义报错，不补零；高阶多根不保证全局最近根。复合倾角顺序、根选择及原生八列尚未实机核实，ZMX 导入保持禁用只读。",
        "BFSD" => "本地六槽 Surf/Data/MinR/MaxR/未用/未用。Data=0..7 为曲率、半径、顶点偏移、最大去除深度、去除体积、最大斜率差、RMS 去除深度和 RMS 斜率差；单位依次为 mm⁻¹、mm、mm、mm、mm³、无量纲、mm、无量纲。MinR=MaxR=0 采用 0..净半口径，其余要求 0≤MinR<MaxR。共享 Core 按旋转对称矢高拟合最小去除体积球面，空气侧决定材料包络方向；使用正式矢高、解析导数与体积积分，径向驻点搜索及 1000 区间统计有工作预算。当前支持平面、标准圆锥及偶/奇非球面，要求明确空气/材料边界；反射和非旋转面未支持。平面半径为无穷，有限评价函数报错。原生 MFE 列及非球面数值尚未核实，ZMX 导入只读；已提交球面矢高表仅验证指定文件的球面情况，不证明任意轮廓的全局最优。",
        "RELI" or "EFNO" => "本地六槽 Int1=Samp、Int2=Wave、Data1=Field、Data2=Pol、Data3/4 未使用。Samp=5..128 为均匀像方方向余弦网格每边点数，经正式追迹求逆；Wave/Field 必须为正编号，仅有焦系统。RELI 清除全部五个渐晕因子，保持真实孔径及透射，以轴上 (0,0) 为分母，允许大于 1；轴上无透射报错。EFNO=0.5×sqrt(π/透射加权方向余弦面积)，无能量报错；非零渐晕因子的原生规则未核实，当前需先 CVIG。Pol=0 沿用正式标量透射，Pol=1 使用非偏振输入的完整 Jones 功率链，透明入射介质及受支持物理膜层；不支持的复介质/散射明确报错。局部折叠或求逆失败不回退近似。可编辑保存及优化，但网格边界权重尚未验证原生等价，固定文件的有效 F 数仍有差异；ZMX 列尚未捕获，导入行保持只读。",
        "ZERN" => "Term/Wave/Samp/Field/Type/Epsilon/Vertex 七槽，正波长和视场编号；Type=0/1/2 为 Fringe/Standard/Annular。均匀瞳孔采样 32..512 点/边，本程序匹配的相邻启用行共同拟合最高请求项（至少 11），仅项号不同复用拟合。Term=-8..0 返回峰谷、RMS、方差、近似 Strehl 或拟合误差；-6 为零参考 RMS。Chief 去均值，Centroid 再去最佳拟合瞳孔倾斜，Strehl 使用指数近似。仅 Vertex=0，环形 Epsilon=0..0.95；资源不足、欠采样或不满秩报错。原生列、Fringe 组最高项规则及数值未实机核实，导入只读。",
        "MCOV" or "MCOG" or "MCOL" => "本地六槽，Int1=Op# 多配置操作数行号，Int2=Cfg# 配置号，均从 1 开始；其余未用。必须先在多配置行表定义绑定，不能把表面编号当作行号。当前行表支持 THIC 厚度（mm）、CRVT 曲率（mm⁻¹，平面为 0）、CONN 圆锥系数、SDIA 半口径（mm）。MCOV 返回指定配置的当前数值；MCOG 返回 min(值,Target)，MCOL 返回 max(值,Target)，满足约束时贡献为零。不依赖最近 CONF。行序调整会重映射本地评价引用，引用中的行/表面不能直接删除；编辑接通链接/拾取及 STAROPT v7 保存。四类数值单元格可独立设为变量，参与跨配置联合优化；行重排保持变量身份。P 拾取按源值×比例+偏移跟随同配置或更早配置，源行也不能晚于目标；支持链式依赖，循环和直接覆盖会报错。自动半口径源须先设固定或变量。更多行类型、热/材料替换/宏求解、原生行表及 MFE 列映射/数值未核实；ZMX 导入保持只读。",
        "CONF" => "本地六槽，Int1=Cfg#（从 1 开始），其余未用；只在有序评价函数中切换后续行读取的配置，目标/权重不参与贡献或归一化。不更改活动文件或配置，切回同一配置也清除 FDMO 临时覆盖；跳过/禁用行不切换。当前未核实 PRIM/SVIG/CVIG/IMSF 后再 CONF 的组合，明确报错。活动配置镜头表变量和所有显式多配置单元格变量可联合优化，候选副本同步基准链接与拾取。ZMX 原生列/状态组合未捕获，导入保持只读。",
        "ZTHI" => "本地六槽，Int1/Int2=Surf1/Surf2 正编号闭区间，可为同一表面；物面不支持。读取文档全部配置的原始有限有符号厚度和，返回 max(Target, 最大总厚度−最小总厚度)。Target 为非负允许差，单位 mm；单配置差为零。临时 IMSF 不截断统计范围，非有限数值或任一配置缺面报错。优化候选同步配置链接，不改用户活动配置。ZMX 列及数值未实机捕获，导入只读。",
        "FDMO" => "Field/未用/Hx/Hy/VDX/VDY/VCX/VCY 八槽。使用批次初始最大视场半径将 Hx/Hy 转为实际位置，四个渐晕因子为归一化瞳孔偏移与压缩；保留权重、标签和切向旋转角。后续行读取隔离副本，FDRE 恢复同编号视场首次覆盖前的数据，CONF 处清除全部 FDMO 覆盖，批次结束丢弃副本。CONF 已支持本地有序配置切换；归一化尺度随所选配置重新读取。新原生列与数值未捕获，导入只读。",
        "FDRE" => "Field 为正视场编号；恢复该视场第一次 FDMO 前的数据。未修改过的有效视场不变，其他临时视场及主波长、像面不变。仅在有序评价中执行；不修改活动文件。原生列映射尚未捕获，导入只读。",
        "REQS" => "Requirements start：需求操作数插入位置的标记，无活动参数、数值贡献或优化残差。保留行号、顺序和注释；尚未实现 Requirements Editor 自动生成需求行，不将标记当作需求求解器。",

        "GBPD" or "GBPP" or "GBPR" or "GBPS" or "GBPW" or "GBPZ" => "Surf/Wave/UseX/W0/S1toW/M²。Surf 为面 1 及之后的实际表面，结果位于该面折射之后、后续厚度传播之前；Wave=0 选主波长。W0 是嵌入 TEM00 模的输入束腰半径（mm），S1toW 是从面 1 至束腰的有符号距离，负数代表束腰在左侧。M²≥1，仅尺寸、束腰和发散角乘 sqrt(M²)，位置、相位曲率半径、瑞利范围不缩放。共用正式近轴矩阵；当前限共轴标准球面/圆锥、平面及理想薄透镜的透射系统；不考虑孔径截断。UseX=0 为 Y，非零整数为 X，当前旋转对称范围内相同。D 为发散半角（弧度）；其余单位 mm；P 为从束腰至表面的距离，负值表示束腰在右侧。R 在准确束腰处为无穷，有限评价函数明确报错，不伪造零值。非法输入、非对称/倾斜/反射/非球面、未同步坐标明确失败。ZMX 通用六槽和 STAROPT 往返；尚无原生 MFE 数值等价捕获。",
        "SPHS" or "PSLP" or "DPHS" or "QSLP" => "主波长（含零谱权重的主波长）下，直接读取正式相位函数和解析梯度，单位 waves、waves/mm。SPHS/PSLP 用 Surf/Mode/X/Y/Data 或 Remove/Remove 或 Orientation 六槽；Mode=0/1 为局部 mm，2 为净半口径归一坐标，指定点不裁孔径。DPHS/QSLP 用 Surf/Data/Samp/Remove/Orientation 或未用/未用；按真实圆、环、偏心圆、矩形或椭圆孔径内均匀 XY 网格统计，Samp=1..5，受相位模型计算预算约束。Remove=0/1；SPHS Data=0，统计 Data=1..12（未移除的倾斜/光焦度系数为 0），Orientation=0..4 径向/正交/X/Y/模值。非相位面明确报错。Zernike 倾斜/光焦度移除、更多采样及原生列和数值验证待完成，原生导入只读。",
        "DENC" or "DENF" => "本程序八槽 Samp/Wave/Field/Type/Refp/Fraction 或 Distance/I Samp/I Delta。Type=1 圆、2 X 狭缝、3 Y 狭缝、4 方框，狭缝/方框距离为半宽 µm。Refp=0/1/2 为 FFT 主光线/质心/顶点，3/4/5 为惠更斯对应参考。Samp=1..4；FFT 使用双倍图像网格的中央窗口，惠更斯另用 I Samp=1..4、I Delta（µm，0 默认），受总计算预算限制。复用共享 PSF 和像素面积积分；分数按有限窗口总能量归一，区域超出窗口或窗口未覆盖目标分数时报错。当前仅有焦、标量；窗口归一不证明未采样尾部误差。原生 1e10 哨兵阈值及列/数值未验证，ZMX 导入保持只读。",
        "SSAG" or "SSLP" or "SCRV" => "本程序 Surf/Mode/X/Y/Off-axis/Remove/BFS 七槽，SSLP/SCRV 再用第八槽 Data6=Orientation。XY 为面局部 mm，点不按孔径裁切；当前 Off-axis=0、Remove=0/1（原面形/先减基准球面），Orientation=0..3（径向、正交、X、Y），SSLP 另支持 4（梯度模值），BFS=0..3 保留。Mode=0 机械/1 净半口径影响尚未接通的拟合范围，在当前模式不改变坐标或点值。复用共享解析导数，无有限差分；矢高 mm、斜率无量纲、法曲率 mm⁻¹。BFS、离轴零件坐标、曲率方向模值和原生列/数值验证尚未完成，ZMX 导入保持禁用只读。",
        "GENC" or "GENF" => "本程序 Int1=Samp（1..5，32..512 个瞳孔区间），Int2=Wave（0 多波长），Data1=Field，Data2=Type（1 圆、2 X 狭缝、3 Y 狭缝、4 方框），Data3=Refp（0 主光线、1 质心、2 顶点、3 最小包围圆圆心），Data4=Fraction/Distance，Data5=No Diff Lim（0 乘 Airy 极限，非零纯几何）。距离 µm；狭缝/方框用半宽。按正能量光线的经验累积分布及最小达到距离计算，不插值绘图；多波长先合并能量。极限缩放用轴上全瞳工作 F 数和旋转对称 Airy 积分。当前仅有焦、标量；原生列映射/分位插值及数值未捕获，ZMX 导入只读。",
        "ERFP" => "本程序 Samp/Wave/Field/Type/Fraction/Max Radius 六槽。Type=0/1 返回相对主光线的 X/Y，2/3 返回相对像面顶点的 X/Y；多波长参考主波长主光线。Fraction=0.01..0.99，采用完整光斑加权经验累积分布的最小达到位置，正向为亮边。输出 mm；Max Radius 当前只支持 0。当前仅有焦、标量，自定义窗口及原生列/分位插值待捕获验证，ZMX 导入只读。",
        "VOLU" or "TMAS" => "本程序 Int1/Int2=起止面（闭区间，单片相同），Data2=Mode（0 机械、1 净半口径）。旋转对称标准/偶次/奇次非球面、同轴无倾斜相邻面、圆形边界；较小面的边缘平延伸到较大面半口径。VOLU 含空气，输出 cm³；TMAS 空气质量为零，玻璃使用目录密度输出 g，缺失密度明确失败。物面/像面、反射、非圆/偏心孔径等尚不支持。原生列映射未捕获核实，ZMX 导入仍只读。",
        "DSAG" or "DSLP" or "DCRV" => "本程序 Int1=Surface、Int2=Data（1 RMS、2 PV、3/4 最小/最大、5/6 最小点 X/Y、7/8 最大点 X/Y）；Data1=Samp（1..5，即 33..513），Data2=Off-axis（当前 0），Data3=Remove（0 原面形、1 减基准球面），Data4=BFS（保留 0..2，拟合未实现），DSLP/DCRV 另有 Data5=Orientation（0 子午、1 弧矢、2 X、3 Y，DSLP 另支持 4 梯度模值）。真实孔径内均匀 XY 网格；RMS 为关于零的均方根，极值并列取先 Y 后 X 扫描首点。输出 sag 为 mm、斜率无量纲、曲率 mm⁻¹、坐标 mm。当前不支持 BFS/离轴重建/曲率 Orientation=4，原生列与数值未验证，ZMX 导入保持只读。",
        "PRIM" => "本程序 Int1=正波长编号，改变后续操作数的主波长。只修改有序评价批次的隔离副本；不改变原系统，Target/Weight 不参与贡献。独立求值明确报错。原生 ZMX 参数列尚未捕获核实，导入记录保持兼容只读。",
        "SVIG" => "本地 Int1=Precision：0 高、1 中、2 低。按当前主波长、光线瞄准设置和实际孔径，在原归一化圆瞳内寻找四条可通光边缘光线，计算 X/Y 偏移与压缩；保留瞳孔旋转角。只修改有序评价的隔离副本，后续行使用新因子，原工程不变，Target/Weight 不参与贡献。求值失败保留上一个有效状态，不返回伪零值。四条边缘通过不保证整个瞳孔都通过，复杂遮挡仍需检查；多波长联合包络及原生迭代/数值未验证。CONF 后可使用，SVIG 后切换 CONF 的组合尚未核实。独立求值报错；ZMX 原生列未捕获，导入只读，本地行须保存 STAROPT。",
        "CVIG" => "清除隔离副本中所有视场的 X/Y 瞳孔偏移、X/Y 渐晕压缩及瞳孔旋转角，影响后续操作数。共享光线生成器先偏移和压缩，再旋转；单光线与批量采样均只应用一次。原系统始终不变，Target/Weight 不参与贡献。",
        "IMSF" => "Surface=0 恢复原像面；正编号选择原系统的中间像面。Refocus=0 保留表面，=1 在所选面之后添加同介质平面并用正式近轴边缘光线调焦。当前仅选择原光阑处或之后，使用角度或物高视场；调焦限共轴透射系统的有限正向焦点，无焦/虚焦/光阑前重建未完成。切换不改动原系统；主波长/渐晕临时状态继续保留。仅有序评价，目标和权重忽略；构造失败不改变批次上一个有效状态。原生 ZMX 参数列尚未捕获核实，导入记录保持兼容只读。",
        "STRH" => "Samp/Wave/Field/Pol/All Conf。Samp=1..2 为瞳孔和像面同时 32/64；图像间隔为共享默认值的一半。返回 PSF 网格峰值与理想峰值之比；多波长按光谱权重及逆波长平方合成强度后取峰值，不能平均各单色峰值。Wave=0 为多波长，Field 必须为正编号；当前仅有焦、Pol=0、All Conf=0。无能量或非法输入明确失败；无原生 MFE 数值等价捕获。",
        "CEHX" or "CEHY" => "Wave/Field/Pol/Pupil Samp/Image Samp/All Conf。两采样代码 1..4 对应 32..256，同时受共享直接 PSF 工作预算约束，不会自动降采样。图像间隔使用共享默认值，多波长取最长有效波长；强度按光谱及逆波长平方权重合成。在像面主光线交点的切平面计算质心，再转回像面局部绝对 X/Y 坐标（mm），不是图上相对偏移。Wave=0 多波长；当前仅有焦、Pol=0、All Conf=0；计入共享 PSF 的变迹和孔径。相邻操作数尚未复用 PSF 积分，原生 MFE 数值等价尚待验证。",
        "MTHA" or "MTHS" or "MTHT" or "MTHN" or "MTHX" => "七参数 Samp/Wave/Field/Freq/Pol/All Conf/Ima Delta。Samp=1..2：瞳孔及像面同时取 32/64 点每边，更高采样超过共享直接 PSF 预算。Field 必须为正编号；Wave=0 按光谱权重合成共享 Huygens PSF，正 Wave 单色忽略光谱权重。频率 cycles/mm，Ima Delta 单位 µm，0 按所选视场及最长有效波长自动计算。仅有焦、Pol=0、All Conf=0；不以偏振标量近似替代完整偏振。复用正式 Huygens 频率图的逆波长平方权重、2N 变换及端点跨度三次插值。A/S/T/N/X 返回平均/弧矢/子午/最小/最大；超过频率网格返回零，无有效能量或非法参数报错。七槽编辑及 STAROPT 可用，原生 ZMX 扩展行继续只读；原生 MFE 数值等价尚待捕获。",
        "DIST" => "本地槽位 Int1=Surf、Int2=Wave（0 为主波长）、Data1=Absolute（0/1）。Surf>0 返回该面的赛德尔 W311=S5/(2λ)，单位波长，Absolute 不改变逐面单位；Surf=0 返回全系统 50×S5/H 百分比，Absolute=1 返回 -S5/(2n′u′) 镜头长度。全系统量以近轴焦点为参考，不是当前像面真实光线的 DISG；最大视场取共享近轴模型。当前支持共轴均匀介质球面/平面折射系统；圆锥、反射、GRIN 等报未支持，全系统无焦与零边缘光线出射斜率报错。原生列及 Surf=0 数值/离焦约定尚未捕获验证，ZMX 导入保留只读，不自动升级。",
        "DISA" => "本地八槽位 Int1=Ref Field（0 为轴上）、Int2=Wave（0 为主波长）、Data1=Field（正编号）、Data2=Data（0 径向、1 X、2 Y）、Data3..6=A/B/C/D。矩阵乘以相对参考视场的线性物高或 tan(角度) 差，实际主光线坐标扣除参考主光线像点；径向百分比为有符号向量差长度/预测径向像高×100。X/Y 本地采用对应坐标差/对应带符号预测像高×100，该方向归一化尚待原生数值核实。恰在参考视场返回 0，其他零预测分母报错；不反求矩阵，允许一维扫描的秩一矩阵。仅有焦，支持实像高共享转换；失追迹/渐晕报错。必须提供完整六个 Data 槽位；ZMX 原生列未核实，导入只读、不补造扩展槽位、不自动升级。",
        "ABCD" => "Int1=Ref Field（0 为轴上）、Int2=Wave（0 为主波长）、Data1=Data（0/1/2/3 为 A/B/C/D）。使用正式网格畸变的非对称二维参考矩阵，对线性物高或 tan(角度) 求局部放大率。仅有焦；最大角必须小于 90 度。实像高先通过共享引擎转换为物方视场。参考矩阵需要非零视场范围；奇异参考、渐晕或失追迹报错。定义级实现，尚无新增原生 MFE 数值等价证据。",
        "DISG" => "Int1=Ref Field（0 为轴上）、Int2=Signed Wave、Data1..4=Hx/Hy/Px/Py。正 Wave 返回百分比；负 Wave 按绝对编号选波长并返回 mm，Wave=0 未支持。共享网格畸变参考矩阵；使用实际与预测像点差的矢量长度，实际径向高度更小时取负号。参考主光线返回零；其余零参考像高的百分比报错。Hx/Hy 以当前或转换后的最大视场归一化；仅有焦、最大角小于 90 度，失追迹/渐晕报错。尚无新增原生 MFE 数值等价证据。",
        "DIMX" => "Int1=Field（0 取最大径向视场坐标）、Int2=Wave（0 为主波长）、Data1=Absolute（0 为 %、1 为 mm）。始终以轴上主光线为参考，计算指定视场畸变的绝对值；不是全视场极值搜索。满足上限时返回 Target，否则返回实际值。沿用 DISG 的正式参考矩阵和有效性限制，尚无新增原生 MFE 数值等价证据。",
        "SMIA" => "Int1=Ref Field（当前仅支持 0 或轴上视场编号）、Int2=Wave（0 为主波长）、Data1/2=X/Y 全视场宽度。六条真实主光线在像面形成左右边高 A1/A2 和中间边高 B，返回 100×((A1+A2)/(2B)−1)。宽度使用视场单位；实像高先转为物方视场。仅有焦，矩形需在已有归一化视场 [-1,1] 内；失追迹、渐晕或零中间像高明确报错，不用零值冒充成功。尚无新增原生 MFE 数值等价证据。",
        "FCGS" or "FCGT" => "Int1 未使用、Int2=Wave（0 为主波长）、Data1/2=Hx/Hy。共享正式场曲傍轴追迹，瞳孔扰动 ±1e−5；子午方向沿视场径向，弧矢方向正交，轴上以 Y/X 定义。输出相对当前像面的有符号焦移（mm），计入离焦。仅有焦、未倾斜的平面像面；平行、失追迹或渐晕报错。角度视场按 tan(角度) 定义实际径向方向。固定 123456 捕获中 FCGT 尚有约 2.16e−5 mm 残差；未声明原生 MFE 数值等价。",

        "GMTA" or "GMTS" or "GMTT" or "GMTN" or "GMTX" => "Int1=Samp、Int2=Wave、Data1=Field、Data2=Freq、Data3=!Scl、Data4=Grid；Field 必须为正编号。!Scl=0 使用衍射极限缩放，否则不缩放。共享真实点列复 OTF，瞳孔/变迹权重，不启用偏振。当前仅 Grid=1；Grid=0 专用稀疏算法未接入。Samp=1..5 为 Workbench 32..512 点/边，非原生 MFE 采样等价声明。有焦频率 cycles/mm，无焦 cycles/mrad；其它单位尚未适配。Wave=0 按光谱权重先合成复 OTF；正 Wave 单色忽略光谱权重。A/S/T/N/X 分别为两方向平均、弧矢、子午、最小、最大。无有效光线或非法参数报错。尚无新增原生 MFE 槽位、网格、数值捕获。",
        "MTFA" or "MTFS" or "MTFT" or "MTFN" or "MTFX" => "Int1=Samp、Int2=Wave、Data1=Field、Data2=Freq、Data3=Grid、Data4=Data Type（仅 A/S/T）。Data Type=0 模值、1 实部、2 虚部、3 相位（度）。Field=0 去除轴上瞳孔 OPD 计算衍射极限；正编号为实际视场。共享 FFT PSF/OTF，两倍边长零填充；MTFN/X 的 Data4 未使用。当前仅 Grid=1；Grid=0 专用稀疏算法未接入。Samp=1..5 为 Workbench 32..512 点/边，非原生 MFE 采样等价声明。有焦频率 cycles/mm，无焦 cycles/mrad；其它单位尚未适配。Wave=0 按光谱权重先合成复 OTF；正 Wave 单色忽略光谱权重。A/S/T/N/X 分别为两方向平均、弧矢、子午、最小、最大。无有效光线或非法参数报错。尚无新增原生 MFE 槽位、网格、数值捕获。",
        "MSWA" or "MSWS" or "MSWT" or "MSWN" or "MSWX" => "Int1=Samp、Int2=Wave、Data1=Field、Data2=Freq、Data3=Grid、Data4 未使用。Field=0 去除轴上瞳孔 OPD；正编号为实际视场。共享 FFT OTF 和 Coltman 奇次谐波展开（最多 999 次谐波，受频率网格范围限制）；不是正弦 MTF 的别名。当前仅 Grid=1；Grid=0 专用稀疏算法未接入。Samp=1..5 为 Workbench 32..512 点/边，非原生 MFE 采样等价声明。有焦频率 cycles/mm，无焦 cycles/mrad；其它单位尚未适配。Wave=0 按光谱权重先合成复 OTF；正 Wave 单色忽略光谱权重。A/S/T/N/X 分别为两方向平均、弧矢、子午、最小、最大。无有效光线或非法参数报错。尚无新增原生 MFE 槽位、网格、数值捕获。",
        "RWCE" or "RWCH" or "RWRE" or "RWRH" or "MWCE" or "MWCH" or "MWRE" or "MWRH" => "Int1=Ring/Samp、Int2=Wave、Data1/2=Hx/Hy；正式 Core 参考球面/无焦参考平面的 OPD，单位 waves。CH/RH 去活塞，CE/RE 去加权最佳拟合活塞和双向倾斜。几何瞳孔求积权重；强度只筛选有效样本，不作偏振/变迹强度加权。RMS Wave=0 按波长权重合成各单色方差，逐波长去参考项；PV 当前仅正 Wave，返回有限采样残差的最大值减最小值。主光线参考采用系统主波长。高斯 Ring=1..32、6 方位，渐晕报错；矩形 Samp=1..256，使用 (2·Samp+1)² 圆内点并剔除渐晕。需要正式引擎能够计算主光线参考；无有效样本明确报错。尚无原生 MFE 网格及多色数值等价证据。",
        "CENX" or "CENY" => "Surf/Wave/Field/Pol/Samp；Surf=0 为像面，Field 为一起始视场号，Wave=0 按波长权重合并全部光线。Samp 为整个圆瞳所用方形网格边长，保留圆内点；按追迹强度计入变迹、孔径与渐晕，返回局部坐标（镜头单位）。Pol 当前仅支持 0；无有效光线或非法参数报错。",
        "CNPX" or "CNPY" or "CNAX" or "CNAY" => "Surf/Wave/Hx/Hy/Pol/Samp；显式 Hx=Hy=0 为轴上。位置质心返回局部 X/Y（镜头单位）；角质心先对交互后的单位方向加权，再取 atan2(X或Y,Z)，单位弧度，保留反射方向。Wave=0 多波长；网格、权重与 Pol=0 限制同 CENX/CENY。尚无 Zemax 原生角质心数值捕获。",
        "GSCE" or "GSCH" or "GSRE" or "GSRH" => "Int1=Ring/Samp、Int2=Wave、Data1/2=Hx/Hy；像面局部采样点至质心或主波长主光线的最大距离，单位镜头长度。高斯采用共享 Gauss 求积 Ring=1..32、6 方位；矩形采用 (2·Samp+1)² 网格内圆瞳点，Samp=1..256。高斯采样渐晕报错，矩形剔除渐晕；无有效光线报错。当前仅支持正波长编号，Wave=0 未验证。采样为 Workbench 定义级实现，尚无原生 MFE 采样/数值等价证据。",
        "GLCX" or "GLCY" or "GLCZ" => "Int1=Surf；返回表面顶点相对 GlobalReferenceSurfaceNumber 的坐标（镜头单位），计入参考面的平移和旋转。默认参考面 1，ZMX GLRS 与原生保存可保留选择；非法参考面、无穷物面或非有限坐标明确报错。",
        "GLCA" or "GLCB" or "GLCC" => "Int1=Surf；表面局部 +Z 轴转换到全局参考系后取 X/Y/Z 方向余弦；沿用已解析的坐标断点与旋转，不按光线传播或反射方向猜测表面轴向。",
        "GLCR" => "Int1=Surf、Int2=Data，Data=1..9 按行返回局部到全局参考系的 3×3 旋转矩阵；3/6/9 分别等于 GLCA/B/C。参考面自身为单位矩阵。",
        "RAGX" or "RAGY" or "RAGZ" => "Surf/Wave/Hx/Hy/Px/Py；共享真实追迹交点转换到所选全局参考面，返回 X/Y/Z 坐标（镜头单位）。Surf=0 为有限物面，Wave=0 为主波长；失追迹、渐晕或无穷物面明确报错。",
        "RAGA" or "RAGB" or "RAGC" => "Surf/Wave/Hx/Hy/Px/Py；交互后的真实有向单位光线转换到所选全局参考系，返回 X/Y/Z 方向余弦；折射、反射及全反射保留追迹方向。",
        "DXDX" or "DXDY" or "DYDX" or "DYDY" => "Int1 不使用，Int2=Wave，Data1..4=Hx/Hy/Px/Py；像面局部 X/Y 截距对归一化 Px/Py 的导数，单位镜头长度。共享追迹配合五点差分及步长减半收敛校验，圆瞳边缘向内单边采样；切点无邻域、失追迹、渐晕或未收敛明确报错。不是对物理瞳孔长度求导，尚无 Zemax 原生槽位/导数数值捕获。",
        "SPHA" or "COMA" or "ASTI" or "FCUR" => "Int1=Surf，0 为全系统总和；Int2=Wave，0 选主波长。共享赛德尔计算分别返回 S1/(8λ)、S2/(2λ)、S3/(2λ)、S4/(4λ)，单位波长；以主波长边缘光线的光阑高度固定归一化。当前限共轴均匀介质球面/平面折射系统及非折射平面像面，圆锥/多项式非球面、偏心、反射、GRIN、理想薄透镜和衍射面报错。",
        "PETC" => "Int2=Wave，0 选主波长；返回 −n像·Σ[(n后−n前)/(n前·n后·R)]，单位为镜头长度倒数。忽略物面和像面半径，保留两端介质；零曲率有效。当前限共轴均匀介质的标准折射面（含圆锥）与平面，其他模型明确拒绝。",
        "LONA" => "Int2=Wave、Data1=Zone：焦点减当前像面的有符号轴向距离（镜头单位）；Zone=0 使用近轴，0<Zone≤1 使用轴上真实光线 Py。0 波长选主波长。限共轴未旋转、均匀介质、标准面及平面理想薄透镜的有焦透射系统，像面须为不折射的平面；无有限焦点或真实光线失追迹/渐晕报错。",
        "AXCL" => "Int1/Int2=Wave1/Wave2、Data1=Zone；返回焦点 Wave1−Wave2（镜头单位），不取绝对值。Zone、0 波长及模型限制同 LONA；交换两波长时符号反转。",
        "LACL" => "Int1/Int2=Minw/Maxw 波长序号；在正向最大子午视场取近轴主光线的当前像面 Y 像高之差 Maxw−Minw（镜头单位），沿用倍率色差分析符号，不自动重排波长。0 选主波长；共轴有焦透射和平面像面限制同 LONA；不支持无焦角度单位。",
        "SPCH" => "Int1/Int2=Minw/Maxw、Data1=Zone；返回该瞳带真实轴向色差减近轴轴向色差，差值均按 Minw−Maxw（镜头单位）。Zone=0 为零，其他参数和模型限制同 LONA；仍校验焦点，不能用零掩盖失追迹。",
        "SDRV" => "Surf/Data/X/Y：Data=0/1 返回子午/弧矢一阶矢高导数，2/3 返回对应二阶导数。共享 Core 解析计算标准面、Even/Odd Asphere 与 XY 多项式；局部坐标，不限制在净口径内。轴上非零 r 项不可微时明确报错。",
        "SCUR" => "Surf/Data/X/Y：Data=0..2 为子午、弧矢及两者曲率差，4..6 为 X、Y 及两者曲率差，8 为 |r·弧矢曲率|；3/7/9 在顶点至目标点的 50 个等距点（含端点）取对应最大绝对值。面型范围同 SDRV，未支持面型报错。",
        "TRAI" => "Surf/Wave/Hx/Hy/Px/Py：在指定表面局部坐标计算相对主波长主光线的横向距离。Surf=0 保留物面语义，Wave=0 选主波长；失追迹、渐晕和无穷物面明确报错。",
        "BSER" => "轴上主光线在像面局部坐标中的径向距离除以系统主波长有效焦距；Int2 为光线波长，0 选主波长。支持平行光轴的透射标准面及理想薄透镜，允许偏心；倾斜、反射、非标准面、GRIN、无焦或失追迹报错。",
        "MNAI" or "MXAI" => "按所选表面、波长、视场追迹主光线和 ±X/±Y 边缘光线，取入射角极值（度）；三项选择为 0 时遍历全部。Symmetry=0 全部、1 仅 Y、2 仅 X。Data=0 为角度边界，1/2/3/4 返回对应光线/视场/波长/表面编号；失追迹明确报错。",
        "PMVA" => "读取当前几何模型的 Zemax Param；目前支持 Even/Odd Asphere 的 1..8 系数，省略项按多项式零系数处理，其他面型明确拒绝。",
        "PMGT" => LowerBoundary("当前 Even/Odd Asphere 的 Param 1..8 系数"),
        "PMLT" => UpperBoundary("当前 Even/Odd Asphere 的 Param 1..8 系数"),
        "GCOS" => "读取当前玻璃目录 AGF OD 首项的相对成本；缺失、负值或非有限数值明确报错，不返回零成本替代。",
        "CVOL" => "所选闭区间内，以顶点 Z 跨度和最大半口径计算包围圆柱体积（镜头单位³）；Mode=0 机械、1 净半口径，不计矢高。偏心、倾斜、反射和无穷物面明确拒绝。",
        "CARD" => "共享近轴矩阵计算 Data=0..11 的物/像方焦距、焦面、主面、反主面、节点和反节点；分别相对 Surf1/Surf2 顶点。Data1=Wave，Data2=Orientation（0 YZ、1 XZ），Data3=Data。当前支持共轴透射标准面与平面理想薄透镜，考虑两侧介质折射率；无焦及未支持模型报错。",
        "PARX" or "PARY" or "PARZ" or "PARR" => "共享近轴引擎追迹 Hx/Hy/Px/Py，返回表面顶点切平面上的局部坐标或径向距离；共轴透射系统中 PARZ=0，不使用真实光线矢高替代。",
        "PARA" or "PARB" or "PARC" => "共享近轴斜率组成归一化方向向量，再转换到表面局部坐标；当前支持共轴透射、均匀介质，折叠反射或偏心系统报错。",
        "PATX" or "PATY" => "返回共享近轴追迹的局部 X/Z 或 Y/Z 方向比；这是近轴斜率，不是实际光线追迹。",
        "PANA" or "PANB" or "PANC" => "在近轴顶点切平面交点调用共享表面法线函数，统一 +Z 朝向后取分量；不把近轴交点改成真实光线交点，非法面形定义域报错。",
        "YNIP" => "轴上 +Y 近轴边缘光线给出 y，取入射折射率 n 和近轴入射角 i=u入+y/R，返回 y·n·i；当前限共轴透射系统。",
        "EFLA" => "以指定表面及下一面、夹层实际折射率和中心厚度构建共享近轴矩阵，两侧按空气 n=1 计算；零光焦度或非实体折射面报错。",
        "DMVA" => "返回所选半口径的两倍；Data2(Mode)=0 使用机械半口径，=1 使用净半口径。",
        "DMGT" => LowerBoundary("指定面的直径；Mode=0 机械、=1 净口径"),
        "DMLT" => UpperBoundary("指定面的直径；Mode=0 机械、=1 净口径"),
        "MNDT" => LowerBoundary("Surf1..Surf2 中直径/正中心厚度最小值；按主波长仅选折射率非 1 的透射空间，Mode=0 机械、=1 净口径"),
        "MXDT" => UpperBoundary("Surf1..Surf2 中直径/正中心厚度最大值；按主波长仅选折射率非 1 的透射空间，Mode=0 机械、=1 净口径"),
        "BLTH" => "两个共轴对齐表面按各自所选半口径，每条径向轴取 200 点（含顶点和边缘），返回轴向包围厚度。Code=0/1/2/3 为 +Y/+X/−Y/−X，4 为四轴；Mode=0 机械、=1 净口径。",
        "REAZ" => "以实际交点转换到指定表面的局部坐标系，返回 Z 矢高。",
        "REAA" or "REAB" or "REAC" => "共享追迹给出交互后的有向单位方向，转换到表面局部坐标，分别取 X、Y、Z 分量；反射和全反射保留真实方向。",
        "RENA" or "RENB" or "RENC" => "在实际光线交点调用共享几何法线，统一为局部 +Z 朝向，分别取 X、Y、Z 分量。",
        "RETX" or "RETY" => "取局部出射方向的 X/Z 或 Y/Z；Z 为零时明确报错。",
        "RAIN" or "RAID" => "使用追迹记录的入射方向和交点法线计算绝对夹角；RAIN 返回余弦，RAID 返回度。",
        "RAEN" or "RAED" => "使用交互后的真实方向和交点法线计算绝对夹角；RAEN 返回余弦，RAED 返回度。",
        "OPTH" => "读取共享追迹的含相位光程。有限共轭以物面为起点，无穷共轭以首个光学面为起点，单位为镜头长度单位。",
        "PLEN" => "以主波长追迹同一条光线，返回 Surf2 与 Surf1 的含相位光程差，保留差值符号。",
        "SAGX" or "SAGY" => "调用表面几何矢高函数，取净半口径处的 +X 或 +Y 点；超出几何定义域时明确报错。",
        "NORX" or "NORY" or "NORZ" => "在 Data1/2 的 X/Y 坐标计算局部 +Z 朝向单位法线；Data3(Global)=0 返回局部分量，=1 转换到 GlobalReferenceSurfaceNumber 指定的参考面坐标系，与 GLC/RAG 系列一致。",
        "NORD" => "从给定 X/Y 处沿 +Z 朝向的单位法线与下一面求交，计入表面坐标变换；无有效前向交点时明确报错。",
        "TCVA" => "读取镜头数据中的表面/隔圈 TCE，单位 10⁻⁶/°C，可为负。未知值明确报错；新建面默认 0，旧文件缺项和未验证的 ZMX 数据保留为未知。玻璃目录 Alpha1 使用 GTCE；此属性尚不驱动温度膨胀追迹。",
        "TCGT" => LowerBoundary("表面/隔圈 TCE（10⁻⁶/°C）；缺失数据时报错，玻璃目录值使用 GTCE"),
        "TCLT" => UpperBoundary("表面/隔圈 TCE（10⁻⁶/°C）；缺失数据时报错，玻璃目录值使用 GTCE"),
        "GTCE" => "读取指定表面之后玻璃目录的 Alpha1 热膨胀系数；缺少目录值时报错，不用零替代。",
        "MNPD" => LowerBoundary("指定范围中玻璃目录 ΔPg,F 的最小值；缺失目录数据时报错"),
        "MXPD" => UpperBoundary("指定范围中玻璃目录 ΔPg,F 的最大值；缺失目录数据时报错"),
        "AMAG" => "共享近轴引擎追迹单位物方斜率的主光线，以像方/物方斜率比给出角放大率；当前只接受同轴旋转对称系统。",
        "LINV" => "共享近轴引擎计算边缘光线和最大视场主光线，以 n(y·u主 − y主·u) 给出拉格朗日不变量。",
        "PIMH" => "共享近轴引擎把最大视场主光线延伸到所选波长的近轴焦面，返回像高；无有限焦面时报错。",
        "OBSN" => "有限共轭下使用主波长的近轴边缘光线，返回 |n物·sin(atan(u物))|；无穷远物方时报错。",
        "BLNK" => "空白行不读取参数、不计算数值，当前值和评价函数贡献均为 0。",
        "DMFS" => "评价函数向导生成的说明行，只保存设置说明，不参与数值计算。",
        "GOTO" => "跳转到 Int1 指定的后续操作数行；被跨过的行不执行。目标必须位于当前行之后且在评价函数范围内。",
        "ENDX" => "立即结束有序评价函数求值；其后的行不执行。",
        "OOFF" => "作为控制标记保留，当前实现返回 0 且不产生贡献。",
        "SKIN" => "系统不是旋转对称时跳转到 Int1 指定的后续行，否则继续下一行。",
        "SKIS" => "系统是旋转对称时跳转到 Int1 指定的后续行，否则继续下一行。",
        "USYM" => "把当前评价函数声明为旋转对称，供 SKIN/SKIS 控制流判断；本行不产生贡献。",
        "CONS" => "当前值直接取该行目标值 Target，因此该常数行自身的平方误差为 0。",
        "SINE" => "读取 Int1 指定的已完成前序行；Int2 非零时先把输入从度转换为弧度，再计算 sin(x)。",
        "COSI" => "读取 Int1 指定的已完成前序行；Int2 非零时先把输入从度转换为弧度，再计算 cos(x)。",
        "TANG" => "读取 Int1 指定的已完成前序行；Int2 非零时先把输入从度转换为弧度，再计算 tan(x)。",
        "ASIN" => "读取 Int1 指定前序行并计算 asin(x)，输入必须在 [-1, 1]；Int2 非零时把弧度结果转换为度。",
        "ACOS" => "读取 Int1 指定前序行并计算 acos(x)，输入必须在 [-1, 1]；Int2 非零时把弧度结果转换为度。",
        "ATAN" => "读取 Int1 指定前序行并计算 atan(x)；Int2 非零时把弧度结果转换为度。",
        "ABSO" => "当前值 = |Int1 指定前序行的当前值|。",
        "SQRT" => "当前值 = sqrt(Int1 指定前序行的当前值)；负输入会报告计算错误。",
        "RECI" => "当前值 = 1 / x，其中 x 来自 Int1 指定前序行；零或极小分母会报告错误。",
        "LOGE" => "对 Int1 指定前序行计算自然对数 ln(x)；x <= 0 时当前实现返回 0。",
        "LOGT" => "对 Int1 指定前序行计算常用对数 log10(x)；x <= 0 时当前实现返回 0。",
        "SUMM" => "当前值 = Int1 指定前序行值 + Int2 指定前序行值。",
        "PROD" => "当前值 = Int1 指定前序行值 × Int2 指定前序行值。",
        "PROB" => "当前值 = Int1 指定前序行值 × Data1(Factor)。",
        "DIVB" => "当前值 = Int1 指定前序行值 / Data1(Factor)；Factor 为零或极小时会报告错误。",
        "DIVI" => "当前值 = Int1 指定前序行值 / Int2 指定前序行值；零或极小分母会报告错误。",
        "DIFF" => "当前值 = Int1 指定前序行值 − Int2 指定前序行值。",
        "EQUA" => "读取 Int1 到 Int2 的闭区间前序行，以 Target 作为相等容差；先求平均值，再把超过容差的绝对偏差求和作为当前值。本行贡献 = |Weight| × Value²。",
        "MAXX" => "读取 Int1 到 Int2 的闭区间前序行，当前值取其中最大值。",
        "MINN" => "读取 Int1 到 Int2 的闭区间前序行，当前值取其中最小值。",
        "OSUM" => "读取 Int1 到 Int2 的闭区间前序行，当前值为所有输入当前值之和。",
        "QSUM" => "读取 Int1 到 Int2 的闭区间前序行，当前值 = sqrt(Σ value²)。",
        "OPVA" => "当前值等于 Int1 指定的已完成前序行当前值。",
        "OPGT" => "读取 Int1 指定前序行。值达到或超过 Target 时钳到 Target，使本行贡献为 0；不足部分形成平方误差。",
        "OPLT" => "读取 Int1 指定前序行。值不超过 Target 时钳到 Target，使本行贡献为 0；超出部分形成平方误差。",
        "ABGT" => "先取 Int1 指定前序行值的绝对值；达到或超过 Target 时贡献为 0，不足部分形成平方误差。",
        "ABLT" => "先取 Int1 指定前序行值的绝对值；不超过 Target 时贡献为 0，超出部分形成平方误差。",
        "RSCE" => RmsSpotCalculation("高斯求积瞳孔采样", "强度加权质心"),
        "RSCH" => RmsSpotCalculation("高斯求积瞳孔采样", "主波长主光线"),
        "RSRE" => RmsSpotCalculation("矩形阵列瞳孔采样", "强度加权质心"),
        "RSRH" => RmsSpotCalculation("矩形阵列瞳孔采样", "主波长主光线"),
        "RWFE" => "追迹有效瞳孔光线到目标面，减去光程的算术平均值，计算光程差的 RMS，再除以所选波长，结果单位为波。",
        "OPDX" => "追迹指定视场、波长和瞳孔坐标的光线；从累计光程中减去同一瞳孔采样的强度加权最佳拟合平面（活塞与 X/Y 倾斜），再除以波长。",
        "OPDM" => "追迹指定光线；从累计光程中仅减去同一瞳孔采样的强度加权平均光程，保留波前倾斜，再除以波长。",
        "OPDC" => "追迹指定光线和同视场、同波长的主光线，以两者累计光程之差除以波长；不移除拟合倾斜。",
        "TRAC" => TransverseAberrationCalculation("径向距离", "强度加权像面质心"),
        "TRAR" => TransverseAberrationCalculation("径向距离", "主波长主光线"),
        "TRCX" => TransverseAberrationCalculation("有符号 X 差", "强度加权像面质心"),
        "TRCY" => TransverseAberrationCalculation("有符号 Y 差", "强度加权像面质心"),
        "TRAX" => TransverseAberrationCalculation("有符号 X 差", "主波长主光线"),
        "TRAY" => TransverseAberrationCalculation("有符号 Y 差", "主波长主光线"),
        "ANAC" => AngularAberrationCalculation("方向余弦差的径向模", "强度加权方向余弦质心"),
        "ANAR" => AngularAberrationCalculation("方向余弦差的径向模", "主波长主光线方向"),
        "ANCX" => AngularAberrationCalculation("有符号 X 方向余弦差", "强度加权方向余弦质心"),
        "ANCY" => AngularAberrationCalculation("有符号 Y 方向余弦差", "强度加权方向余弦质心"),
        "ANAX" => AngularAberrationCalculation("有符号 X 方向余弦差", "主波长主光线方向"),
        "ANAY" => AngularAberrationCalculation("有符号 Y 方向余弦差", "主波长主光线方向"),
        "MECA" => MooreElliottCalculation("弧矢与切向差值的算术平均；有符号值可能相消，独立控制两方向应分别使用 MECS/MECT"),
        "MECS" => MooreElliottCalculation("弧矢方向（Px）"),
        "MECT" => MooreElliottCalculation("切向方向（Py）"),
        "REAX" => "追迹 Hx/Hy、Px/Py 和波长指定的实际光线到 Surface，返回交点 X 坐标。Surface=0 时按该操作数语义解析为像面。",
        "REAY" => "追迹 Hx/Hy、Px/Py 和波长指定的实际光线到 Surface，返回交点 Y 坐标。Surface=0 时按该操作数语义解析为像面。",
        "REAR" => "追迹指定实际光线到目标面，当前值 = sqrt(X² + Y²)。",
        "RANG" => "追迹指定实际光线到目标面，根据方向余弦计算 atan2(sqrt(L² + M²), |N|)，结果单位为弧度。",
        "EFFL" => "由当前系统的近轴矩阵估算有效焦距。" + GradientFirstOrderScope,
        "EFLX" or "EFLY" => "对 Int1 起始面到 Int2 终止面的子系统建立近轴矩阵，并计算该范围的有效焦距。当前旋转对称实现中 X/Y 使用同一路径。" + GradientFirstOrderScope,
        "ENPP" => "由当前系统近轴追迹估算入瞳相对位置。" + GradientFirstOrderScope,
        "EPDI" => "由当前系统近轴追迹和孔径定义估算入瞳直径。" + GradientFirstOrderScope,
        "EXPP" => "由当前系统近轴追迹估算出瞳相对位置。" + GradientFirstOrderScope,
        "EXPD" => "由当前系统近轴追迹估算出瞳直径。" + GradientFirstOrderScope,
        "SFNO" or "TFNO" => "按 Field（0=轴上）和 Wave（0=主波长）计算弧矢/子午工作 F 数。共用正式 Core，使用主光线与对应 ±1 边缘光线的平均数值孔径；忽略表面孔径、考虑视场渐晕因子。当前支持有焦轴上/Y 方向视场；全瞳失追迹及零 NA 报错，不缩瞳、不封顶。任意方位和原生 MFE 数值对齐尚待验证。",
        "MNRE" or "MNRI" or "MXRE" or "MXRI" => "七参数 Surf1/Surf2/Wave/Hx/Hy/Px/Py。共用正式追迹的入射/出射方向，与局部面法线夹角取正值（度）；波长 0=主波长，含首尾面。累计各面超限量，上限返回 Target+超限量，下限返回 Target-超限量。无穷远物面、倒置范围、失追迹/渐晕及非法坐标报错。编辑器和 STAROPT 支持完整七项；未经验证的 ZMX 扩展行保持只读原文。",
        "ISFN" or "WFNO" or "FNUM" => "由当前系统近轴边缘光线估算像方 F 数；这些代码当前连接同一个 F 数计算入口。",
        "ISNA" => "追迹所选波长的近轴边缘光线，当前值 = |n_image × sin(atan(u_image))|。",
        "WLEN" => "返回 Int2 指定波长编号的波长值，单位为微米。编号 0 使用主波长。",
        "INDX" => "读取 Int1 指定表面之后的材料，在 Int2 指定波长处计算折射率。",
        "MNIN" => LowerBoundary("Int1 到 Int2 表面范围内玻璃材料的 d 线 Nd 最小值"),
        "MXIN" => UpperBoundary("Int1 到 Int2 表面范围内玻璃材料的 d 线 Nd 最大值"),
        "MNAB" => LowerBoundary("Int1 到 Int2 表面范围内玻璃材料的 Vd 阿贝数最小值"),
        "MXAB" => UpperBoundary("Int1 到 Int2 表面范围内玻璃材料的 Vd 阿贝数最大值"),
        "POWR" => "读取 Int1 指定标准折射表面和 Int2 指定波长；当前值 = (n_after − n_before) / Radius，平面返回 0，非标准面或反射面报告错误。",
        "PMAG" => "仅用于有限物距。以单位物高建立近轴主光线，并在近轴像面求像高；当前值 = 近轴像高 / 单位物高。",
        "PETZ" => "使用与 PETC、赛德尔报告共享的 Petzval 曲率，返回 1/PETC；计入像方折射率，忽略物面和像面半径。曲率为零、非法波长/介质或未支持模型明确报错。",
        "TOTR" => "返回光学系统表面组的总轴向长度 TotalTrack；无穷远物面厚度不作为有限传播距离累加。",
        "RADI" => "返回 Surface 指定表面的曲率半径。",
        "THIC" or "CTVA" => "返回指定表面之后的轴向中心厚度。",
        "CTGT" => LowerBoundary("指定表面之后的轴向中心厚度"),
        "CTLT" => UpperBoundary("指定表面之后的轴向中心厚度"),
        "CVVA" => "返回指定表面的曲率；平面为 0，其他表面为 1 / Radius。",
        "CVGT" => LowerBoundary("指定表面的曲率 1 / Radius（平面为 0）"),
        "CVLT" => UpperBoundary("指定表面的曲率 1 / Radius（平面为 0）"),
        "COVA" => "返回指定表面的圆锥常数。",
        "COGT" => LowerBoundary("指定表面的圆锥常数"),
        "COLT" => UpperBoundary("指定表面的圆锥常数"),
        "ETVA" or "TTVA" => "在指定表面与下一表面各自半口径处，按 Int2 方向代码 0(+Y)、1(+X)、2(−Y)、3(−X) 计算 Thickness + Sag_next − Sag_current。",
        "ETGT" or "TTGT" => LowerBoundary("按指定方向计算的边缘总厚度"),
        "ETLT" or "TTLT" => UpperBoundary("按指定方向计算的边缘总厚度"),
        "FTGT" => LowerBoundary("沿 +Y 从轴上到全口径进行 201 点采样所得的最小厚度"),
        "FTLT" => UpperBoundary("沿 +Y 从轴上到全口径进行 201 点采样所得的最大厚度"),
        "STHI" => "在 Data1=X、Data2=Y 处计算指定表面到下一表面的局部厚度：Thickness + Sag_next(X,Y) − Sag_current(X,Y)。",
        "TTHI" => "从 Int1 起始面到 Int2 终止面（闭区间，包含终止面厚度，可为同一面）累加轴向厚度；正无穷物面厚度不计入。",
        "TGTH" => "在 Int1 到 Int2 的范围内筛选玻璃介质空间并累加中心厚度，不计反射空间与空气空间。",
        "MNCA" => RangeThicknessBoundary("空气中心厚度", "最小值", lower: true),
        "MXCA" => RangeThicknessBoundary("空气中心厚度", "最大值", lower: false),
        "MNCG" => RangeThicknessBoundary("玻璃中心厚度", "最小值", lower: true),
        "MXCG" => RangeThicknessBoundary("玻璃中心厚度", "最大值", lower: false),
        "MNCT" => RangeThicknessBoundary("全部非反射空间中心厚度", "最小值", lower: true),
        "MXCT" => RangeThicknessBoundary("全部非反射空间中心厚度", "最大值", lower: false),
        "MNEA" => RangeThicknessBoundary("空气 +Y 边厚", "最小值", lower: true),
        "MXEA" => RangeThicknessBoundary("空气 +Y 边厚", "最大值", lower: false),
        "MNEG" => RangeThicknessBoundary("玻璃 +Y 边厚", "最小值", lower: true),
        "MXEG" => RangeThicknessBoundary("玻璃 +Y 边厚", "最大值", lower: false),
        "MNET" => RangeThicknessBoundary("全部非反射空间 +Y 边厚", "最小值", lower: true),
        "MXET" => RangeThicknessBoundary("全部非反射空间 +Y 边厚", "最大值", lower: false),
        "XNEA" => PerimeterThicknessBoundary("空气", "最小值", lower: true),
        "XXEA" => PerimeterThicknessBoundary("空气", "最大值", lower: false),
        "XNEG" => PerimeterThicknessBoundary("玻璃", "最小值", lower: true),
        "XXEG" => PerimeterThicknessBoundary("玻璃", "最大值", lower: false),
        "XNET" => PerimeterThicknessBoundary("全部非反射空间", "最小值", lower: true),
        "XXET" => PerimeterThicknessBoundary("全部非反射空间", "最大值", lower: false),
        "MNCV" => RangeScalarBoundary("曲率", "最小值", lower: true),
        "MXCV" => RangeScalarBoundary("曲率", "最大值", lower: false),
        "MNSD" => RangeScalarBoundary("半口径", "最小值", lower: true),
        "MXSD" => RangeScalarBoundary("半口径", "最大值", lower: false),
        _ => "由当前 Workbench 已连接的操作数计算入口求值；具体输入含义以参数表和定义为准。"
    };

    private static string RmsSpotCalculation(string sampling, string reference) =>
        $"使用{sampling}追迹有效光线，以{reference}为参考，按光线强度计算 sqrt(Σ[w × ((X−Xref)² + (Y−Yref)²)] / Σw)。波长编号为 0 时合并全部波长。";

    private static string TransverseAberrationCalculation(string output, string reference) =>
        $"追迹指定实际光线到目标面，相对{reference}计算{output}。质心参考可按设置使用单波长或多波长强度加权。";

    private static string AngularAberrationCalculation(string output, string reference) =>
        $"追迹指定实际光线到目标面，相对{reference}计算{output}。";

    private static string MooreElliottCalculation(string direction) =>
        $"本地六槽 未用/Wave/Field/Freq/Px/Py；原始瞳孔点与沿负 X（弧矢）或负 Y（切向）移位点比较，返回共同主光线参考下的波前 OPD 差，单位 waves；{direction}。使用正式工作 F 数或无焦截止频率，频率单位 cycles/mm 或本程序无焦 cycles/mrad，Wave=0 本地选主波长。出瞳重叠之外、无效频率及失追迹明确报错。MECA 平均规则与原生列、三项原生移位符号及数值未捕获验证；MECA 导入只读。";

    private static string LowerBoundary(string quantity) =>
        $"计算{quantity}。结果达到或超过 Target 时钳到 Target，使贡献为 0；不足部分形成平方误差。";

    private static string UpperBoundary(string quantity) =>
        $"计算{quantity}。结果不超过 Target 时钳到 Target，使贡献为 0；超出部分形成平方误差。";

    private static string RangeThicknessBoundary(
        string quantity,
        string extreme,
        bool lower) =>
        $"在 Int1 到 Int2 表面范围内计算{quantity}的{extreme}；Zone 为 0 时按 1 处理。{BoundarySuffix(lower)}";

    private static string PerimeterThicknessBoundary(
        string material,
        string extreme,
        bool lower) =>
        $"在 Int1 到 Int2 范围内，对{material}的指定 Zone 使用 64 个方位角采样全周边厚并取{extreme}。{BoundarySuffix(lower)}";

    private static string RangeScalarBoundary(
        string quantity,
        string extreme,
        bool lower) =>
        $"在 Int1 到 Int2 的闭区间表面上计算{quantity}{extreme}。{BoundarySuffix(lower)}";

    private static string BoundarySuffix(bool lower) => lower
        ? "结果达到或超过 Target 时贡献为 0。"
        : "结果不超过 Target 时贡献为 0。";
}
