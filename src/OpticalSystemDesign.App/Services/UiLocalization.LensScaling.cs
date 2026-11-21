namespace OpticalSystemDesign.App.Services;

internal static partial class UiLocalization
{
    private static void AddLensScalingTexts(Action<string, string, string> add)
    {
        add("镜头缩放", "Scale Lens", "レンズのスケーリング");
        add("镜头工具", "Lens Tools", "レンズツール");
        add("因子缩放", "Scale by factor", "倍率で拡大・縮小");
        add("单位换算", "Convert units", "単位を換算");
        add("起始面", "First surface", "開始面");
        add("终止面", "Last surface", "終了面");
        add("当前单位", "Current unit", "現在の単位");
        add("应用于全部配置", "Apply to all configurations", "すべての構成に適用");
        add("厘米", "Centimeters", "センチメートル");
        add("米", "Meters", "メートル");
        add("英寸", "Inches", "インチ");
        add("按因子改变所选表面的尺寸；选择全部表面时，同时缩放系统孔径与高度视场。波长、材料和评价目标保持原值。", "Scale the dimensions of the selected surfaces. Selecting all surfaces also scales the system aperture and height fields. Wavelengths, materials and merit targets keep their values.", "選択した面の寸法を拡大・縮小します。全ての面を選ぶとシステム開口と高さ視野も変わります。波長、材料、評価目標は維持されます。");
        add("单位换算保持实际尺寸不变，镜头数据表使用新单位；分析使用各自标注的单位。", "Unit conversion keeps physical dimensions unchanged. The lens data editor uses the new units; each analysis keeps its stated units.", "単位換算では実際の寸法は変わりません。レンズデータ表には新しい単位を使い、解析は各表示単位を維持します。");
        add("缩放因子必须是大于零的有限数值。", "Enter a finite scale factor greater than zero.", "倍率にはゼロより大きい有限値を入力してください。");
        add("请选择起始面和终止面。", "Select the first and last surfaces.", "開始面と終了面を選択してください。");
        add("工程已变化，请重新打开镜头缩放。", "The project has changed. Reopen Scale Lens.", "プロジェクトが変更されました。レンズのスケーリングを開き直してください。");
        add("起始面和终止面必须存在，且起始面不能晚于终止面。", "Choose existing surfaces with the first surface no later than the last.", "実在する面を選択し、開始面を終了面より後にしないでください。");
        add("缩放后的数值超出有效范围，请调整缩放因子。", "The scaled values are out of range. Adjust the scale factor.", "スケーリング後の値が有効範囲外です。倍率を調整してください。");
    }
}
