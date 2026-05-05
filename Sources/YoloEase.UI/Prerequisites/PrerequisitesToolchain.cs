using System.Linq;

namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Resolves and validates the managed Python, YOLO, CVAT CLI, download, and log paths.
/// </summary>
public sealed class PrerequisitesToolchain : IPrerequisitesToolchain
{
    private const string PythonInstallerVersion = "3.11.9";

    public PrerequisitesToolchain(IAppArguments appArguments)
    {
        if (string.IsNullOrWhiteSpace(appArguments.AppDataDirectory))
        {
            throw new InvalidOperationException("Application data directory is not available");
        }

        ToolsRoot = new DirectoryInfo(Path.Combine(appArguments.AppDataDirectory, "tools"));
        DownloadsDirectory = new DirectoryInfo(Path.Combine(ToolsRoot.FullName, "downloads"));
        PythonDirectory = new DirectoryInfo(Path.Combine(ToolsRoot.FullName, "python-3.11"));
        VenvDirectory = new DirectoryInfo(Path.Combine(ToolsRoot.FullName, "venv"));
        LogsDirectory = new DirectoryInfo(Path.Combine(ToolsRoot.FullName, "logs"));

        PythonExecutable = new FileInfo(Path.Combine(PythonDirectory.FullName, "python.exe"));
        VenvPythonExecutable = new FileInfo(Path.Combine(VenvDirectory.FullName, "Scripts", "python.exe"));
        VenvPipExecutable = new FileInfo(Path.Combine(VenvDirectory.FullName, "Scripts", "pip.exe"));
        YoloExecutable = new FileInfo(Path.Combine(VenvDirectory.FullName, "Scripts", "yolo.exe"));
        CvatCliExecutable = new FileInfo(Path.Combine(VenvDirectory.FullName, "Scripts", "cvat-cli.exe"));
        RequirementsFile = new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "requirements.txt"));
        PythonInstallerUri = new Uri($"https://www.python.org/ftp/python/{PythonInstallerVersion}/python-{PythonInstallerVersion}-amd64.exe");
    }

    public DirectoryInfo ToolsRoot { get; }

    public DirectoryInfo DownloadsDirectory { get; }

    public DirectoryInfo PythonDirectory { get; }

    public DirectoryInfo VenvDirectory { get; }

    public DirectoryInfo LogsDirectory { get; }

    public FileInfo PythonExecutable { get; }

    public FileInfo VenvPythonExecutable { get; }

    public FileInfo VenvPipExecutable { get; }

    public FileInfo YoloExecutable { get; }

    public FileInfo CvatCliExecutable { get; }

    public FileInfo RequirementsFile { get; }

    public Uri PythonInstallerUri { get; }

    public void EnsureBaseDirectories()
    {
        Directory.CreateDirectory(ToolsRoot.FullName);
        Directory.CreateDirectory(DownloadsDirectory.FullName);
        Directory.CreateDirectory(LogsDirectory.FullName);
    }

    public FileInfo RequirePythonExecutable()
    {
        return RequireFile(PythonExecutable, "Managed Python is not installed. Open the Prerequisites tab and install missing tools.");
    }

    public FileInfo RequireVenvPythonExecutable()
    {
        return RequireFile(VenvPythonExecutable, "Managed Python environment is not ready. Open the Prerequisites tab and install missing tools.");
    }

    public FileInfo RequireYoloExecutable()
    {
        return RequireFile(YoloExecutable, "Yolo CLI is not installed in the managed environment. Open the Prerequisites tab and install missing tools.");
    }

    public FileInfo RequireCvatCliExecutable()
    {
        return RequireFile(CvatCliExecutable, "CVAT CLI is not installed in the managed environment. Open the Prerequisites tab and install missing tools.");
    }

    public void EnsureManagedPath(FileSystemInfo fileSystemInfo)
    {
        var fullName = Path.GetFullPath(fileSystemInfo.FullName);
        var root = Path.GetFullPath(ToolsRoot.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullName.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !string.Equals(fullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to modify path outside managed tools root: {fullName}");
        }
    }

    private static FileInfo RequireFile(FileInfo file, string message)
    {
        file.Refresh();
        if (!file.Exists)
        {
            throw new PrerequisitesMissingException(message);
        }

        return file;
    }
}
