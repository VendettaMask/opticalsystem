namespace OpticalSystemDesign.App.Services;

internal static partial class UiLocalization
{
    private static void AddMaterialLibraryTexts(Action<string, string, string> add)
    {
        add("目录资料", "Catalog Details", "カタログ情報");
        add("熔融频率", "Melt Frequency", "溶解頻度");
        add("参考成本", "Relative Cost", "相対コスト");
        add("相对于 BK7 的目录参考成本，不是实时报价。", "Catalog cost relative to BK7, not a current price quote.", "BK7 に対するカタログ相対コストです。現在の見積価格ではありません。");
        add("问号表示原目录未提供该参数。", "A question mark means the source catalog does not provide this value.", "疑問符は出典カタログに値がないことを示します。");
    }
}
