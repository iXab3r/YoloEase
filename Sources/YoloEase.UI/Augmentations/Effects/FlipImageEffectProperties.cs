using SixLabors.ImageSharp.Processing;

namespace YoloEase.UI.Augmentations;

/// <summary>
/// Persists the flip augmentation mode.
/// </summary>
public sealed record FlipImageEffectProperties : ImageEffectProperties
{
    public FlipMode FlipMode { get; set; }
    
    public override int Version { get; set; } = 1;
}
