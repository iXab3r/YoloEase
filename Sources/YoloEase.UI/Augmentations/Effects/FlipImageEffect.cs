using System.Linq;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace YoloEase.UI.Augmentations;

public sealed class FlipImageEffect : ImageEffectBase<FlipImageEffectProperties>
{
    private static readonly Binder<FlipImageEffect> Binder = new();

    static FlipImageEffect()
    {
        Binder.Bind(x => $"{x.FlipMode}").To(x => x.Description);
    }
    
    public FlipImageEffect()
    {
        Name = "Flip";
        
        Binder.Attach(this).AddTo(Anchors);
    }

    public FlipMode FlipMode { get; set; } = FlipMode.None;

    public override void Mutate(SharpImage imageFile)
    {
        imageFile.Mutate(x => x.Flip(FlipMode));
    }

    public override SharpRectangleF Mutate(SharpSize imageSize, SharpRectangleF bounds)
    {
        return FlipMode switch
        {
            FlipMode.Vertical => new SharpRectangleF(
                bounds.Left,
                imageSize.Height - (bounds.Top + bounds.Height),
                bounds.Width,
                bounds.Height),

            FlipMode.Horizontal => new SharpRectangleF(
                imageSize.Width - (bounds.Left + bounds.Width),
                bounds.Top,
                bounds.Width,
                bounds.Height),

            _ => bounds // If no flip is specified, return the original bounds
        };
    }

    protected override void VisitSave(FlipImageEffectProperties target)
    {
        base.VisitSave(target);

        target.FlipMode = FlipMode;
    }

    protected override void VisitLoad(FlipImageEffectProperties source)
    {
        base.VisitLoad(source);

        FlipMode = source.FlipMode;
    }
}