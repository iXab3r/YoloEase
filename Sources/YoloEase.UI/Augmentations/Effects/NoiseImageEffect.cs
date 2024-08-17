using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace YoloEase.UI.Augmentations;

public sealed class NoiseImageEffect : ImageEffectBase<NoiseImageEffectProperties>
{
    private static readonly Binder<NoiseImageEffect> Binder = new();

    static NoiseImageEffect()
    {
        Binder.Bind(x => $"{x.Percentage:F0}%").To(x => x.Description);
    }
    
    public NoiseImageEffect()
    {
        Name = "Noise";
        
        Binder.Attach(this).AddTo(Anchors);
    }

    public float Percentage { get; set; }

    public override void Mutate(SharpImage imageFile)
    {
        ApplyNoise(imageFile, Percentage);
    }

    public override SharpRectangleF Mutate(SharpSize imageSize, SharpRectangleF bounds)
    {
        return bounds;
    }

    protected override void VisitSave(NoiseImageEffectProperties target)
    {
        base.VisitSave(target);

        target.Percentage = Percentage;
    }

    protected override void VisitLoad(NoiseImageEffectProperties source)
    {
        base.VisitLoad(source);

        Percentage = source.Percentage;
    }
    
    static void ApplyNoise(Image image, float noisePercentage)
    {
        var random = new Random();

        image.Mutate(ctx =>
        {
            ctx.ProcessPixelRowsAsVector4((row) =>
            {
                for (var x = 0; x < row.Length; x++)
                {
                    if (!(random.NextDouble() < noisePercentage / 100))
                    {
                        continue;
                    }

                    var originalPixel = row[x];

                    // Apply random noise to the pixel
                    var noisyPixel = new Vector4(
                        Math.Clamp(originalPixel.X + (float)(random.NextDouble() * 0.2 - 0.1), 0, 1),
                        Math.Clamp(originalPixel.Y + (float)(random.NextDouble() * 0.2 - 0.1), 0, 1),
                        Math.Clamp(originalPixel.Z + (float)(random.NextDouble() * 0.2 - 0.1), 0, 1),
                        originalPixel.W); // Keep alpha unchanged

                    row[x] = noisyPixel;
                }
            });
        });
    }

    static byte ClampToByte(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
    }
}