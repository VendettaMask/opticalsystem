using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.App.Panels;

// Presentation navigation only. These groups never select an evaluator or change parameter semantics.
internal sealed record OperandHelpFamily(string Id, string Category, string Name, string Codes, string Summary = "");

internal static class OperandHelpTaxonomy
{
    internal static IReadOnlyList<OperandHelpFamily> Families { get; } =
    [
        new("focal", "一阶与系统数据", "焦距与光焦度", "EFFL EFLA EFLX EFLY POWR POWF POWP CARD"),
        new("pupil", "一阶与系统数据", "瞳孔、F 数与数值孔径", "ENPP EPDI EXPD EXPP ISFN ISNA SFNO TFNO WFNO"),
        new("conjugate", "一阶与系统数据", "共轭与倍率", "AMAG PMAG PIMH LINV OBSN"),
        new("system", "一阶与系统数据", "波长与系统状态", "WLEN PRIM IMSF CVIG SVIG FDMO FDRE"),
        new("configuration", "一阶与系统数据", "多重结构", "CONF MCOG MCOL MCOV ZTHI"),

        new("real-ray", "光线与坐标", "实际光线位置与方向", "REAX REAY REAZ REAR REAA REAB REAC RENA RENB RENC RETX RETY RANG"),
        new("ray-angle", "光线与坐标", "入射角与出射角", "RAID RAIN RAED RAEN MNAI MXAI MNRE MXRE MNRI MXRI"),
        new("paraxial-ray", "光线与坐标", "近轴光线", "PARX PARY PARZ PARR PARA PARB PARC PATX PATY PANA PANB PANC YNIP"),
        new("global-ray", "光线与坐标", "全局光线坐标", "RAGX RAGY RAGZ RAGA RAGB RAGC"),
        new("global-surface", "光线与坐标", "表面全局位置与姿态", "GLCX GLCY GLCZ GLCA GLCB GLCC GLCR"),
        new("path", "光线与坐标", "光程与路径长度", "OPTH PLEN"),
        new("centroid", "光线与坐标", "光线束质心", "CENX CENY CNPX CNPY CNAX CNAY"),
        new("ray-fan", "光线与坐标", "光线扇导数", "DXDX DXDY DYDX DYDY"),
        new("ray-check", "光线与坐标", "光线条件与良率", "HHCN HYLD",
            "真实光线的追迹条件与良率相关量。HYLD 已接通普通折射界面的角度敏感性惩罚，使用交点处局部折射率；不是良率百分比。HHCN 仍待超半球分支追迹支持。"),

        new("spot", "像质与分析", "点列半径", "RSCE RSCH RSRE RSRH GSCE GSCH GSRE GSRH"),
        new("wavefront", "像质与分析", "波前与光程差", "RWCE RWCH RWRE RWRH MWCE MWCH MWRE MWRH OPDC OPDM OPDX ZERN"),
        new("ray-aberration", "像质与分析", "横向与角度像差", "TRAC TRAR TRAX TRAY TRCX TRCY TRAD TRAE TRAI ANAC ANAR ANAX ANAY ANCX ANCY BSER"),
        new("seidel", "像质与分析", "三阶像差与色差", "SPHA COMA ASTI FCUR PETC PETZ AXCL LACL LONA SPCH"),
        new("distortion", "像质与分析", "畸变与场曲", "ABCD DIMX DISA DISC DISG DIST FCGS FCGT SMIA"),
        new("geometric-mtf", "像质与分析", "几何与 Moore–Elliott MTF", "GMTA GMTS GMTT GMTN GMTX MECA MECS MECT"),
        new("diffraction-mtf", "像质与分析", "衍射 MTF 与方波传递", "MTFA MTFS MTFT MTFN MTFX MTHA MTHS MTHT MTHN MTHX MSWA MSWS MSWT MSWN MSWX"),
        new("psf", "像质与分析", "PSF 与 Strehl", "STRH CEHX CEHY"),
        new("energy", "像质与分析", "圈入能量与边缘响应", "GENC GENF DENC DENF XENC XENF ERFP"),
        new("illumination", "像质与分析", "相对照度与有效 F 数", "RELI EFNO"),
        new("image-analysis", "像质与分析", "成像与专项检测", "IMAE FOUC OSCD BIOC BIOD"),

        new("curvature", "面形与制造", "曲率与圆锥系数", "CVGT CVLT CVVA MNCV MXCV COGT COLT COVA"),
        new("aperture", "面形与制造", "口径与净孔径", "DMGT DMLT DMVA MNCA MXCA MNSD MXSD"),
        new("thickness", "面形与制造", "中心厚度与间隔", "CTGT CTLT CTVA MNCG MXCG MNCT MXCT TGTH TTHI TOTR STHI"),
        new("edge-thickness", "面形与制造", "边缘厚度与比例", "ETGT ETLT ETVA MNEA MXEA MNEG MXEG MNET MXET XNEA XXEA XNEG XXEG XNET XXET TTGT TTLT TTVA FTGT FTLT"),
        new("surface-point", "面形与制造", "指定点面形与法线", "SSAG SSLP SCRV SAGX SAGY SCUR SDRV NORX NORY NORZ NORD TSAG"),
        new("surface-statistics", "面形与制造", "面形统计与拟合球面", "DSAG DSLP DCRV BFSD"),
        new("phase", "面形与制造", "表面相位与相位斜率", "SPHS PSLP DPHS QSLP"),
        new("manufacturing", "面形与制造", "体积、质量与毛坯", "VOLU CVOL TMAS BLTH MNDT MXDT"),
        new("parameters", "面形与制造", "表面参数约束", "PMGT PMLT PMVA"),

        new("glass", "材料与介质", "玻璃折射率与色散", "INDX MNIN MXIN MNAB MXAB MNPD MXPD RGLA"),
        new("glass-properties", "材料与介质", "材料成本与热膨胀", "GCOS GTCE TCGT TCLT TCVA"),
        new("grin-point", "材料与介质", "GRIN 折射率约束", "I1GT I1LT I1VA I2GT I2LT I2VA I3GT I3LT I3VA I4GT I4LT I4VA I5GT I5LT I5VA I6GT I6LT I6VA",
            "6 个位置 × 3 种约束，共 18 个独立代码。1 前顶点、2 前端 +Y、3 前端 +X、4 后顶点、5 后端 +Y、6 后端 +X；GT 为下限，LT 为上限，VA 为目标值。Gradient 1～5 已接入受限材料计算，Gradient 5 可含显式色散；曲面点 Z 约定和原生数值待捕获验证，详细范围见各项说明。"),
        new("grin-profile", "材料与介质", "GRIN 范围与梯度", "DLTN GRMN GRMX LPTD"),

        new("coating", "光束与专项", "镀膜与偏振", "CMGT CMLT CMVA CIGT CILT CIVA CEGT CELT CEVA CODA RRET"),
        new("paraxial-gaussian", "光束与专项", "近轴高斯光束", "GBPD GBPP GBPR GBPS GBPW GBPZ"),
        new("skew-gaussian", "光束与专项", "斜光线高斯光束", "GBSD GBSP GBSR GBSS GBSW"),
        new("physical-optics", "光束与专项", "物理光学传播与光纤耦合", "POPD POPI FICL FICP"),
        new("ghost", "光束与专项", "鬼像聚焦", "GAOI GPIM GPRT GPRX GPRY GPSX GPSY"),
        new("hologram", "光束与专项", "光学制造全息图", "CMFV"),

        new("math", "数学与控制", "数学运算", "ABSO ACOS ASIN ATAN CONS COSI DIFF DIVB DIVI LOGE LOGT MAXX MINN OSUM PROB PROD QSUM RECI SQRT SUMM SINE TANG"),
        new("row-constraint", "数学与控制", "操作数行约束", "ABGT ABLT EQUA OPGT OPLT OPVA"),
        new("flow", "数学与控制", "评价函数控制", "BLNK DMFS ENDX GOTO OOFF SKIN SKIS USYM REQS"),
        new("tolerance", "数学与控制", "公差与扩展程序", "TOLR ZPLM UDOC"),

        new("local", "扩展与保留", "本程序扩展", "FNUM RADI THIC RWFE", "本程序自定义操作数，不属于 Zemax 2026 R1 的 MFE 操作数目录。"),
        new("unused", "扩展与保留", "官方未用名称", "BIPF COSA HACG QOAC TRAN", "官方标记 Unused，仅保存历史文件中的名称与参数，不列入待实现功能。"),
        new("unverified", "扩展与保留", "定义待核实", "OGSS SPHD", "已核实官方 API 名称，公开计算定义尚未核实，当前不执行。")
    ];

    private static readonly IReadOnlyDictionary<string, OperandHelpFamily> ByCode = Families
        .SelectMany(family => family.Codes.Split(' ').Select(code => (code, family)))
        .ToDictionary(pair => pair.code, pair => pair.family, StringComparer.Ordinal);

    internal static OperandHelpFamily FamilyFor(string code) => ByCode.TryGetValue(code, out var family)
        ? family : new("unclassified", "扩展与保留", "待分类", string.Empty);

    internal static string DisplayName(MeritOperandTypeDto operand)
    {
        if (FamilyFor(operand.Code).Id == "grin-point")
        {
            var position = operand.Code[1] switch
            {
                '1' => "前顶点",
                '2' => "前端 +Y",
                '3' => "前端 +X",
                '4' => "后顶点",
                '5' => "后端 +Y",
                _ => "后端 +X"
            };
            var constraint = operand.Code[2..] switch { "GT" => "下限", "LT" => "上限", _ => "目标值" };
            return $"{position}折射率{constraint}";
        }
        return operand.Code == "HYLD" ? "真实光线高良率贡献" : operand.DisplayName;
    }

    internal static string Status(MeritOperandTypeDto operand) => FamilyFor(operand.Code).Id switch
    {
        "unused" => "官方未用 · 仅保留",
        "unverified" => "定义待核实 · 不执行",
        "local" => "本程序扩展 · 可计算",
        _ => operand.CompatibilityOnly ? "未实现 · 兼容保留" : "可计算 · 范围见说明"
    };
}
