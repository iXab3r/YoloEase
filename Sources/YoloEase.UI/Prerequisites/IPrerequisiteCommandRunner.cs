using System.Threading;
using CliWrap;

namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Runs external prerequisite commands and returns sanitized process output.
/// </summary>
public interface IPrerequisiteCommandRunner
{
    Task<PrerequisiteCommandResult> RunAsync(
        Command command,
        string logName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        Action<string>? outputHandler = null);
}
