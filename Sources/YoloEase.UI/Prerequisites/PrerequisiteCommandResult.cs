using System.Linq;

namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Captures the exit code and sanitized output streams from a prerequisite command.
/// </summary>
public sealed record PrerequisiteCommandResult
{
    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public string CombinedOutput => string.Join(Environment.NewLine, new[] {StandardOutput, StandardError}.Where(x => !string.IsNullOrWhiteSpace(x)));

    public bool IsSuccess => ExitCode == 0;
}
