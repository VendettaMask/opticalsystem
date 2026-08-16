using OptilandWorkbench.ZemaxLibraryImporter;

if (args.Length == 0 || args.Any(argument => argument is "-h" or "--help" or "/?"))
{
    PrintUsage();
    return args.Length == 0 ? 2 : 0;
}

try
{
    var parsed = ParseArguments(args);
    var repositoryRoot = parsed.RepositoryRoot ?? FindRepositoryRoot();
    var exampleDirectory = parsed.ExampleDirectory
        ?? Path.Combine(repositoryRoot, "samples", "lenses");
    var lensLibraryDirectory = parsed.LensLibraryDirectory
        ?? Path.Combine(
            repositoryRoot,
            "src",
            "OptilandWorkbench.App",
            "Assets",
            "LensLibrary");
    var result = await new ZemaxLibraryInstaller().InstallAsync(
        new ZemaxLibraryInstallOptions(
            parsed.InputPath,
            exampleDirectory,
            lensLibraryDirectory,
            parsed.SourceId,
            parsed.SourceName,
            parsed.Category,
            parsed.SourceUrl,
            parsed.License,
            parsed.Name,
            parsed.Id,
            parsed.ExampleFileName,
            parsed.LensType,
            parsed.Application,
            parsed.DesignOrganization));

    Console.WriteLine(result.UpdatedExistingEntry ? "已更新现有镜头条目。" : "已新增镜头条目。");
    Console.WriteLine($"ID: {result.Id}");
    Console.WriteLine($"名称: {result.Name}");
    Console.WriteLine($"配置数: {result.ConfigurationCount}");
    Console.WriteLine($"示例库: {result.ExampleProjectPath}");
    Console.WriteLine($"数据库镜头库: {result.LibraryProjectPath}");
    Console.WriteLine($"镜头库索引: {result.CatalogPath}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"转换失败：{exception.Message}");
    return 1;
}

static ParsedArguments ParseArguments(IReadOnlyList<string> arguments)
{
    string? inputPath = null;
    string? repositoryRoot = null;
    string? exampleDirectory = null;
    string? lensLibraryDirectory = null;
    string sourceId = "user-examples";
    string sourceName = "STAR Labs 用户示例";
    string category = "示例镜头";
    string sourceUrl = string.Empty;
    string license = "用户提供";
    string? name = null;
    string? id = null;
    string? exampleFileName = null;
    string? lensType = null;
    string? application = null;
    string? designOrganization = null;

    for (var index = 0; index < arguments.Count; index++)
    {
        var argument = arguments[index];
        if (!argument.StartsWith("-", StringComparison.Ordinal))
        {
            if (inputPath is not null)
            {
                throw new ArgumentException("只能指定一个 Zemax 输入文件。");
            }

            inputPath = argument;
            continue;
        }

        var value = NextValue(argument);
        switch (argument)
        {
            case "--repo-root":
                repositoryRoot = Path.GetFullPath(value);
                break;
            case "--examples":
                exampleDirectory = Path.GetFullPath(value);
                break;
            case "--library":
                lensLibraryDirectory = Path.GetFullPath(value);
                break;
            case "--source-id":
                sourceId = value;
                break;
            case "--source-name":
                sourceName = value;
                break;
            case "--category":
                category = value;
                break;
            case "--source-url":
                sourceUrl = value;
                break;
            case "--license":
                license = value;
                break;
            case "--name":
                name = value;
                break;
            case "--id":
                id = value;
                break;
            case "--example-file":
                exampleFileName = value;
                break;
            case "--lens-type":
                lensType = value;
                break;
            case "--application":
                application = value;
                break;
            case "--design-organization":
                designOrganization = value;
                break;
            default:
                throw new ArgumentException($"未知参数：{argument}");
        }

        string NextValue(string option)
        {
            if (++index >= arguments.Count)
            {
                throw new ArgumentException($"参数 {option} 缺少值。");
            }

            return arguments[index];
        }
    }

    return new ParsedArguments(
        inputPath ?? throw new ArgumentException("必须指定一个 Zemax .zmx 输入文件。"),
        repositoryRoot,
        exampleDirectory,
        lensLibraryDirectory,
        sourceId,
        sourceName,
        category,
        sourceUrl,
        license,
        name,
        id,
        exampleFileName,
        lensType,
        application,
        designOrganization);
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException(
        "无法定位项目根目录，请使用 --repo-root、--examples 和 --library 指定输出位置。");
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        ZemaxLibraryImporter - 将单个 Zemax ZMX 转换为 STAROPT，并同步加入示例库和数据库镜头库

        用法:
          ZemaxLibraryImporter <file.zmx> [选项]

        默认输出:
          示例库: samples/lenses/<原文件名>.staropt
          镜头库: src/OptilandWorkbench.App/Assets/LensLibrary/projects/<稳定ID>.staropt
          索引:   src/OptilandWorkbench.App/Assets/LensLibrary/index.json

        选项:
          --name <名称>             镜头显示名称
          --category <分类>         默认“示例镜头”
          --source-id <ID>          默认“user-examples”
          --source-name <名称>      默认“STAR Labs 用户示例”
          --source-url <URL>        来源地址
          --license <说明>          默认“用户提供”
          --lens-type <类型>        镜头类型；未提供时按分类生成保守值
          --application <用途>      应用场景；未提供时按分类生成保守值
          --design-organization <单位> 设计单位；未知时记录“未注明”
          --id <ID>                 自定义稳定镜头 ID
          --example-file <文件名>   示例库中的 STAROPT 文件名
          --repo-root <目录>        项目根目录
          --examples <目录>         自定义示例库目录
          --library <目录>          自定义数据库镜头库目录
          -h, --help                显示帮助

        重复导入同一来源文件时会按稳定 ID 更新原条目，不会生成重复记录。
        """);
}

internal sealed record ParsedArguments(
    string InputPath,
    string? RepositoryRoot,
    string? ExampleDirectory,
    string? LensLibraryDirectory,
    string SourceId,
    string SourceName,
    string Category,
    string SourceUrl,
    string License,
    string? Name,
    string? Id,
    string? ExampleFileName,
    string? LensType,
    string? Application,
    string? DesignOrganization);
