namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Indicates that a required managed prerequisite executable or resource is not available.
/// </summary>
public sealed class PrerequisitesMissingException : Exception
{
    public PrerequisitesMissingException(string message) : base(message)
    {
    }
}
