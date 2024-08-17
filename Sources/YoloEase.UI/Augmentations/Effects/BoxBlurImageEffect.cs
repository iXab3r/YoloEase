using SixLabors.ImageSharp.Processing;

namespace YoloEase.UI.Augmentations;

public sealed class BoxBlurImageEffect : ImageEffectBase<BoxBlurImageEffectProperties>
{
    private static readonly Binder<BoxBlurImageEffect> Binder = new();

    static BoxBlurImageEffect()
    {
        Binder.Bind(x => $"{x.Radius}px radius").To(x => x.Description);
    }
    
    public BoxBlurImageEffect()
    {
        Name = "Box Blur";
        
        Binder.Attach(this).AddTo(Anchors);
    }

    public int Radius { get; set; }

    public override void Mutate(SharpImage imageFile)
    {
        imageFile.Mutate(x => x.BoxBlur(Radius));
    }

    public override SharpRectangleF Mutate(SharpSize imageSize, SharpRectangleF bounds)
    {
        return bounds;
    }

    protected override void VisitSave(BoxBlurImageEffectProperties target)
    {
        base.VisitSave(target);

        target.Radius = Radius;
    }

    protected override void VisitLoad(BoxBlurImageEffectProperties source)
    {
        base.VisitLoad(source);

        Radius = source.Radius;
    }
}