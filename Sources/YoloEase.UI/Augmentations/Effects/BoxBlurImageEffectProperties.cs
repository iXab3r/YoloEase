namespace YoloEase.UI.Augmentations;

public sealed record BoxBlurImageEffectProperties : ImageEffectProperties
{
    public int Radius { get; set; }
    
    public override int Version { get; set; } = 1;
}