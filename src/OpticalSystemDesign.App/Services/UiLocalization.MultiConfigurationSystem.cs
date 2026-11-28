namespace OpticalSystemDesign.App.Services;

internal static partial class UiLocalization
{
    private static void AddMultiConfigurationSystemTexts(Action<string, string, string> add)
    {
        add("表面/编号", "Index", "面/番号");
        add("XFIE · 视场 X", "XFIE · Field X", "XFIE · X 視野");
        add("YFIE · 视场 Y", "YFIE · Field Y", "YFIE · Y 視野");
        add("FLWT · 视场权重", "FLWT · Field weight", "FLWT · 視野の重み");
        add("WAVE · 波长（µm）", "WAVE · Wavelength (µm)", "WAVE · 波長 (µm)");
        add("WLWT · 波长权重", "WLWT · Wavelength weight", "WLWT · 波長の重み");
        add("波长必须至少为 0.001 µm，权重不能为负数，所有值必须有限。", "Wavelength must be at least 0.001 µm, weights must be non-negative, and all values must be finite.", "波長は 0.001 µm 以上、重みは非負、すべての値は有限値である必要があります。");
        add("多配置行必须引用有效的一基视场号或波长号，且不能附带表面参数。", "The row must reference an existing one-based field or wavelength index and cannot include a surface parameter.", "行には有効な1始まりの視野番号または波長番号を指定し、面パラメータを含めることはできません。");
        add("物面多配置行仅支持有限物距。", "Object-surface configuration rows require a finite object distance.", "物体面の構成行では有限の物体距離が必要です。");
        add("删除会改变多配置操作数引用的编号，请先移除相关行。", "Deletion would change an index referenced by a configuration operand. Remove the related rows first.", "削除すると構成オペランドが参照する番号が変わります。先に関連する行を削除してください。");
        add("该参数由多配置拾取控制，请先移除拾取。", "This parameter is controlled by a configuration pickup. Remove the pickup first.", "このパラメータは構成ピックアップで制御されています。先にピックアップを解除してください。");
    }
}
