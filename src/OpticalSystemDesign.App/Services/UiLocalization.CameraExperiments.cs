namespace OpticalSystemDesign.App.Services;

internal static partial class UiLocalization
{
    private static readonly string[] CameraStatusPrefixes =
        ["实验打开失败，现有配置和 A/B 保留：", "实验保存失败：", "复算失败，原 A/B 保留：", "标定未应用：", "仿真未完成：", "NSC 读取失败，原 A/B 保留："];

    private static bool TryTranslateCameraStatus(string source, string language, out string result)
    {
        foreach (var prefix in CameraStatusPrefixes)
            if (source.StartsWith(prefix, StringComparison.Ordinal))
            {
                result = Text(prefix, language) + Text(source[prefix.Length..], language);
                return true;
            }
        result = source; return false;
    }

    private static void AddCameraExperimentTexts(Action<string, string, string> add)
    {
        add("保存完整实验", "Save Complete Experiment", "完全な実験を保存");
        add("复算 A/B", "Recompute A/B", "A/Bを再計算");
        add("使用实验镜头与输入", "Use Experiment Lens and Inputs", "実験のレンズと入力を使用");
        add("打开实验", "Open Experiment", "実験を開く");
        add("保存完整相机实验", "Save Complete Camera Experiment", "完全なカメラ実験を保存");
        add("打开相机实验", "Open Camera Experiment", "カメラ実験を開く");
        add("相机实验", "Camera Experiment", "カメラ実験");
        add("导入传感器参数", "Import Sensor Parameters", "センサーパラメータを読み込む");
        add("导入绝对 QE 曲线", "Import Absolute QE Curve", "絶対QE曲線を読み込む");
        add("实验文件", "Experiment Files", "実験ファイル");
        add("完整实验已保存：A/B 各自的镜头、输入、光谱、传感器、ISP 和数值结果均已归档。",
            "Complete experiment saved: each A/B lens, input, spectrum, sensor, ISP and numerical result is archived.",
            "完全な実験を保存しました。A/Bそれぞれのレンズ、入力、スペクトル、センサー、ISPと数値結果を保存しています。");
        add("计算输入已切换；A 保留，请运行 B。", "Inputs switched; A is retained. Run B again.", "計算入力を切り替えました。Aを保持し、Bを再実行してください。");
        add("完整实验保存 A/B 各自的镜头快照、输入原文件、光源/材质/传感器/ISP 与结果。打开后可直接复算，也可修改设置再运行 B。勾选时使用实验镜头；取消后使用当前工程和图片路径。",
            "Complete experiments retain each A/B lens snapshot, original input, light, material, sensor, ISP and results. Recompute after opening or edit settings and run B. Select to use the experiment lens; clear to use the current project and image path.",
            "完全な実験にはA/Bそれぞれのレンズ、元の入力、光源、材質、センサー、ISPと結果を保存します。開いて再計算するか、設定を変更してBを実行できます。選択時は実験のレンズ、解除時は現在のプロジェクトと画像パスを使用します。");
        add("实验已打开。使用保存的镜头与输入，不依赖原始文件路径；可复算 A/B 或修改参数运行 B。",
            "Experiment opened with its saved lens and inputs; original file paths are not required. Recompute A/B or edit parameters and run B.",
            "保存されたレンズと入力で実験を開きました。元のファイルパスは不要です。A/Bの再計算、またはパラメータを変更してBを実行できます。");
        add("正在按各自冻结输入复算 A/B…", "Recomputing A/B from their respective frozen inputs…", "A/Bそれぞれの保存入力から再計算中…");
        add("A/B 已按各自镜头、输入和参数复算完成。", "A/B recomputed with their respective lenses, inputs and parameters.", "A/Bそれぞれのレンズ、入力とパラメータで再計算しました。");
        add("复算已取消，原 A/B 保留。", "Recomputation cancelled; original A/B retained.", "再計算を中止し、元のA/Bを保持しました。");
        add("请先完成仿真，再保存完整实验。", "Complete a simulation before saving an experiment.", "シミュレーションを完了してから実験を保存してください。");
        add("计算模型版本不同，可查看结果，不能按当前模型复算。", "Model versions differ; results can be viewed but cannot be recomputed with the current model.", "計算モデルのバージョンが異なります。結果の表示はできますが、現在のモデルで再計算できません。");
        add("请先完成仿真或打开完整实验。", "Complete a simulation or open a complete experiment first.", "先にシミュレーションを完了するか、完全な実験を開いてください。");
        add("使用实验镜头与内嵌输入", "Using Experiment Lens and Embedded Inputs", "実験のレンズと埋め込み入力を使用");
        add("使用当前工程镜头与输入", "Using Current Project Lens and Inputs", "現在のプロジェクトのレンズと入力を使用");
        add("请输入有效数值。", "Enter valid numeric values.", "有効な数値を入力してください。");
        add("已完成。可固定 A、修改参数后运行 B，或保存完整实验。", "Completed. Pin A, edit parameters and run B, or save the complete experiment.", "完了しました。Aを固定し、パラメータを変更してBを実行するか、完全な実験を保存できます。");
        add("导入暗场／平场 RAW 标定 JSON", "Import Dark/Flat RAW Calibration JSON", "暗・フラットRAW校正JSONを読み込む");
        add("暗场／平场标定", "Dark / Flat Calibration", "暗・フラット校正");
        add("暗场／平场参数估计已应用；原始帧与条件可随模板保存，A 保留。请运行 B。",
            "Dark/flat estimates applied; original frames and conditions can be saved with the profile. A is retained. Run B again.",
            "暗・フラット推定値を適用しました。元のフレームと条件はテンプレートに保存できます。Aを保持し、Bを再実行してください。");
        add("标定导入固定温度、已知增益的线性 RAW：两组暗曝光（各至少 2 帧）及长曝光平场（至少 2 帧），估计黑电平、读噪、暗电流、DSNU/PRNU。保留原始帧与条件；不估计 QE/满阱，不做逐像元校正。导入来源不代表已核实测量。",
            "Import linear RAW at a fixed temperature and known gain: two dark exposure groups (at least two frames each) and flats at the longer exposure (at least two). Estimates black level, read noise, dark current and DSNU/PRNU; retains frames and conditions. Does not estimate QE/full well or apply pixel corrections. Imported provenance is not independent measurement verification.",
            "一定温度と既知ゲインの線形RAWを読み込みます。暗露光2群（各2枚以上）と長い露光時間のフラット（2枚以上）から黒レベル、読み出しノイズ、暗電流、DSNU/PRNUを推定します。元のフレームと条件を保存し、QE・飽和容量の推定や画素補正は行いません。出典の読み込みは実測の検証ではありません。");
        add("实验打开失败，现有配置和 A/B 保留：", "Experiment open failed; settings and A/B retained: ", "実験を開けませんでした。設定とA/Bを保持：");
        add("实验保存失败：", "Experiment save failed: ", "実験を保存できませんでした：");
        add("复算失败，原 A/B 保留：", "Recomputation failed; original A/B retained: ", "再計算できませんでした。元のA/Bを保持：");
        add("标定未应用：", "Calibration not applied: ", "校正を適用できませんでした：");
        add("仿真未完成：", "Simulation incomplete: ", "シミュレーション未完了：");
    }
}
