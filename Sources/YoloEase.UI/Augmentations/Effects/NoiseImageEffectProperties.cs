namespace YoloEase.UI.Augmentations;

public sealed record NoiseImageEffectProperties : ImageEffectProperties
{
    public float Percentage { get; set; }
    
    public override int Version { get; set; } = 1;
}