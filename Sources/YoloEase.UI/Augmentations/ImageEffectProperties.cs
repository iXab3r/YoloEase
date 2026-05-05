namespace YoloEase.UI.Augmentations;

/// <summary>
/// Persists common image-effect configuration such as enabled state and version.
/// </summary>
public abstract record ImageEffectProperties : IPoeEyeConfigVersioned
{
    public bool IsEnabled { get; set; } = true;
    
    public abstract int Version { get; set; } 
}
