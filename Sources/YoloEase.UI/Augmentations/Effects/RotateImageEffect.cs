using System.Linq;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace YoloEase.UI.Augmentations;

public sealed class RotateImageEffect : ImageEffectBase<RotateImageEffectProperties>
{
    private static readonly Binder<RotateImageEffect> Binder = new();

    static RotateImageEffect()
    {
        Binder.Bind(x => $"{x.Rotation}").To(x => x.Description);
    }
    
    public RotateImageEffect()
    {
        Name = "Rotate";
        
        Binder.Attach(this).AddTo(Anchors);
    }

    public RotateMode Rotation { get; set; } = RotateMode.Rotate90;

    public override void Mutate(SharpImage imageFile)
    {
        imageFile.Mutate(x => x.Rotate(Rotation));
    }

    public override SharpRectangleF Mutate(SharpSize imageSize, SharpRectangleF bounds)
    {
        return Rotation switch
        {
            RotateMode.Rotate90 => new SharpRectangleF(
                imageSize.Height - bounds.Bottom,
                bounds.Left,
                bounds.Height,
                bounds.Width),
            RotateMode.Rotate180 => new SharpRectangleF(
                imageSize.Width - bounds.Right,
                imageSize.Height - bounds.Bottom,
                bounds.Width,
                bounds.Height),
            RotateMode.Rotate270 => new SharpRectangleF(
                bounds.Top,
                imageSize.Width - bounds.Right,
                bounds.Height,
                bounds.Width),
            _ => bounds // 0 degrees rotation means no change
        };
    }

    protected override void VisitSave(RotateImageEffectProperties target)
    {
        base.VisitSave(target);

        target.RotateMode = Rotation;
    }

    protected override void VisitLoad(RotateImageEffectProperties source)
    {
        base.VisitLoad(source);

        Rotation = source.RotateMode;
    }
}