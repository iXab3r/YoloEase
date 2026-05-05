namespace YoloEase.UI.Augmentations;

/// <summary>
/// Persists the noise augmentation settings.
/// </summary>
public sealed record NoiseImageEffectProperties : ImageEffectProperties
{
    public float Percentage { get; set; }
    
    public override int Version { get; set; } = 1;
}
