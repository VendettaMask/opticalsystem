if (args.Length != 3 || args[2] != "--energy")
    throw new ArgumentException("repository-root fresh-output-directory --energy");
var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("A fresh output directory is required.");
Directory.CreateDirectory(output);
EnergyWindowDiagnosis.Run(root, output);
