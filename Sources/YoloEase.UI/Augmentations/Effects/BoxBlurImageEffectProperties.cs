namespace YoloEase.UI.Augmentations;

/// <summary>
/// Persists the box blur augmentation settings.
/// </summary>
public sealed record BoxBlurImageEffectProperties : ImageEffectProperties
{
    public int Radius { get; set; }
    
    public override int Version { get; set; } = 1;
}
