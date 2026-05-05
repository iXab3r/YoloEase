using System.Linq;
using CliWrap.Builders;

namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Applies environment variables that keep Python-related child processes inside the managed toolchain.
/// </summary>
internal static class ManagedPythonEnvironment
{
    public static void Apply(IPrerequisitesToolchain toolchain, EnvironmentVariablesBuilder environment, bool activateVenv = false)
    {
        toolchain.EnsureBaseDirectories();
        toolchain.EnsureManagedPath(toolchain.DownloadsDirectory);

        var pipCacheDirectory = new DirectoryInfo(Path.Combine(toolchain.DownloadsDirectory.FullName, "pip-cache"));
        toolchain.EnsureManagedPath(pipCacheDirectory);
        Directory.CreateDirectory(pipCacheDirectory.FullName);

        var managedPathEntries = new[]
        {
            Path.Combine(toolchain.VenvDirectory.FullName, "Scripts"),
            toolchain.PythonDirectory.FullName
        };
        var currentPath = Environment.GetEnvironmentVariable("PATH");
        var path = string.Join(
            Path.PathSeparator.ToString(),
            managedPathEntries
                .Append(currentPath)
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        environment.Set("PATH", path);
        environment.Set("PYTHONHOME", string.Empty);
        environment.Set("PYTHONPATH", string.Empty);
        environment.Set("PYTHONUTF8", "1");
        environment.Set("PYTHONIOENCODING", "utf-8");
        environment.Set("PYTHONNOUSERSITE", "1");
        environment.Set("PIP_DISABLE_PIP_VERSION_CHECK", "1");
        environment.Set("PIP_REQUIRE_VIRTUALENV", "1");
        environment.Set("PIP_CACHE_DIR", pipCacheDirectory.FullName);
        if (activateVenv)
        {
            environment.Set("VIRTUAL_ENV", toolchain.VenvDirectory.FullName);
        }
    }
}
