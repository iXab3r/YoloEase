using SixLabors.ImageSharp.Processing;

namespace YoloEase.UI.Augmentations;

public sealed record FlipImageEffectProperties : ImageEffectProperties
{
    public FlipMode FlipMode { get; set; }
    
    public override int Version { get; set; } = 1;
}