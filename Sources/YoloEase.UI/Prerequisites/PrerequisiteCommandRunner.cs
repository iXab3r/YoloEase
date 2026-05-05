using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CliWrap;
using CliWrap.EventStream;
using PoeShared.Logging;

namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Executes prerequisite commands with timeout handling, redacted logs, and captured output.
/// </summary>
public sealed partial class PrerequisiteCommandRunner : DisposableReactiveObjectWithLogger, IPrerequisiteCommandRunner
{
    private readonly IPrerequisitesToolchain toolchain;

    public PrerequisiteCommandRunner(IPrerequisitesToolchain toolchain)
    {
        this.toolchain = toolchain;
    }

    public async Task<PrerequisiteCommandResult> RunAsync(
        Command command,
        string logName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        Action<string>? outputHandler = null)
    {
        toolchain.EnsureBaseDirectories();
        var pipCacheDirectory = new DirectoryInfo(Path.Combine(toolchain.DownloadsDirectory.FullName, "pip-cache"));
        Directory.CreateDirectory(pipCacheDirectory.FullName);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        var output = new StringBuilder();
        var error = new StringBuilder();
        var commandLog = new StringBuilder();
        int? exitCode = null;

        try
        {
            var preparedCommand = command
                .WithEnvironmentVariables(x =>
                {
                    x.Set("PYTHONUTF8", "1");
                    x.Set("PYTHONIOENCODING", "utf-8");
                    x.Set("PIP_DISABLE_PIP_VERSION_CHECK", "1");
                    x.Set("PIP_CACHE_DIR", pipCacheDirectory.FullName);
                })
                .WithValidation(CommandResultValidation.None);
            var log = Log.WithSuffix(SanitizeLogName(logName));
            var commandText = Redact(preparedCommand.ToString());
            log.Info($"Running prerequisite command: {commandText}");
            commandLog.AppendLine($"> {commandText}");
            outputHandler?.Invoke($"> {commandText}");

            await foreach (var cmdEvent in preparedCommand.ListenAndLogAsync(log, linkedCts.Token))
            {
                switch (cmdEvent)
                {
                    case StandardOutputCommandEvent stdOut:
                        var stdOutText = Redact(stdOut.Text);
                        AppendOutput(output, stdOutText);
                        AppendOutput(commandLog, stdOutText);
                        outputHandler?.Invoke(stdOutText);
                        break;
                    case StandardErrorCommandEvent stdErr:
                        var stdErrText = Redact(stdErr.Text);
                        AppendOutput(error, stdErrText);
                        AppendOutput(commandLog, stdErrText);
                        outputHandler?.Invoke(stdErrText);
                        break;
                    case ExitedCommandEvent exited:
                        exitCode = exited.ExitCode;
                        commandLog.AppendLine($"< exited with code {exitCode}");
                        outputHandler?.Invoke($"< exited with code {exitCode}");
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Command timed out after {timeout.TotalSeconds:F0}s: {Redact(command.ToString())}");
        }

        var result = new PrerequisiteCommandResult
        {
            ExitCode = exitCode ?? -1,
            StandardOutput = output.ToString(),
            StandardError = error.ToString()
        };

        var logFile = new FileInfo(Path.Combine(toolchain.LogsDirectory.FullName, $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{SanitizeLogName(logName)}.log"));
        await File.WriteAllTextAsync(logFile.FullName, commandLog.ToString(), cancellationToken);

        return result;
    }

    private static void AppendOutput(StringBuilder builder, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        builder.AppendLine(text);
    }

    private static string SanitizeLogName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(x => invalid.Contains(x) ? '-' : x).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "command" : sanitized;
    }

    private static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redacted = PasswordAssignmentRegex().Replace(value, "${prefix}<redacted>");
        redacted = PasswordOptionRegex().Replace(redacted, "${prefix}<redacted>");
        redacted = AuthorizationHeaderRegex().Replace(redacted, "${prefix}<redacted>");
        return redacted;
    }

    [GeneratedRegex(@"(?<prefix>\bpassword\s*=\s*)[^\s;]+", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordAssignmentRegex();

    [GeneratedRegex(@"(?<prefix>--password(?:\s+|=))(?:""[^""]*""|'[^']*'|[^\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordOptionRegex();

    [GeneratedRegex(@"(?<prefix>Authorization:\s*)[^\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeaderRegex();
}
