namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Stores application-level prerequisite preferences outside project files.
/// </summary>
public sealed record YoloEaseApplicationConfig : IPoeEyeConfigVersioned
{
    public bool CheckPrerequisitesAtStartup { get; set; } = true;

    public int Version { get; set; }
}
