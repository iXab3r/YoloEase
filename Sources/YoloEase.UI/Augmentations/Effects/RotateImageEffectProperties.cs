using SixLabors.ImageSharp.Processing;

namespace YoloEase.UI.Augmentations;

/// <summary>
/// Persists the rotate augmentation mode.
/// </summary>
public sealed record RotateImageEffectProperties : ImageEffectProperties
{
    public RotateMode RotateMode { get; set; }
    
    public override int Version { get; set; } = 1;
}
