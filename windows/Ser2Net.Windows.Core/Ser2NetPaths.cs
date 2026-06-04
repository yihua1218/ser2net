namespace Ser2Net.Windows.Core;

public sealed record Ser2NetPaths(string InstallDirectory, string DataDirectory)
{
    public string RootDirectory => InstallDirectory;
    public string BinDirectory => Path.Combine(InstallDirectory, "bin");
    public string EtcDirectory => Path.Combine(DataDirectory, "etc", "ser2net");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");
    public string MappingFilePath => Path.Combine(EtcDirectory, "mappings.json");
    public string GeneratedConfigPath => Path.Combine(EtcDirectory, "windows.yml");
    public string Ser2NetExePath => Path.Combine(BinDirectory, "ser2net.exe");

    public static Ser2NetPaths FromRoot(string rootDirectory) => new(rootDirectory, rootDirectory);

    public static Ser2NetPaths FromExecutable()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var installRoot = Directory.GetParent(baseDir)?.FullName ?? baseDir;
        var dataRoot = IsUnderProgramFiles(installRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ser2Net")
            : installRoot;
        return new Ser2NetPaths(installRoot, dataRoot);
    }

    private static bool IsUnderProgramFiles(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return IsChildPath(fullPath, programFiles) || IsChildPath(fullPath, programFilesX86);
    }

    private static bool IsChildPath(string path, string parent)
    {
        if (string.IsNullOrWhiteSpace(parent)) {
            return false;
        }

        var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), parent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
}
