namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Exposes managed prerequisite locations and guards for toolchain operations.
/// </summary>
public interface IPrerequisitesToolchain
{
    DirectoryInfo ToolsRoot { get; }

    DirectoryInfo DownloadsDirectory { get; }

    DirectoryInfo PythonDirectory { get; }

    DirectoryInfo VenvDirectory { get; }

    DirectoryInfo LogsDirectory { get; }

    FileInfo PythonExecutable { get; }

    FileInfo VenvPythonExecutable { get; }

    FileInfo VenvPipExecutable { get; }

    FileInfo YoloExecutable { get; }

    FileInfo CvatCliExecutable { get; }

    FileInfo RequirementsFile { get; }

    Uri PythonArchiveUri { get; }

    string PythonArchiveSha256 { get; }

    void EnsureBaseDirectories();

    FileInfo RequirePythonExecutable();

    FileInfo RequireVenvPythonExecutable();

    FileInfo RequireYoloExecutable();

    FileInfo RequireCvatCliExecutable();

    void EnsureManagedPath(FileSystemInfo fileSystemInfo);
}
