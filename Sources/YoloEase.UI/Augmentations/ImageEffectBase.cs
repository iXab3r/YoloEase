using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using Standart.Hash.xxHash;
using YoloEase.Cvat.Shared;
using YoloEase.UI.Scaffolding;

namespace YoloEase.UI.Augmentations;

public abstract class ImageEffectBase<T> : YoloObjectBase<T>, IImageEffect where T : ImageEffectProperties, new()
{
    public bool IsEnabled { get; set; } = true;
    
    public string? Name { get; set; }
    
    public string GetSettingsHash()
    {
        var properties = Properties;
        var propertiesAsString = properties.ToString();

        var hashString = $"{this.GetType()}\n{propertiesAsString}";
        return xxHash64.ComputeHash(hashString).ToString();
    }

    public abstract void Mutate(SharpImage imageFile);
    
    public abstract SharpRectangleF Mutate(SharpSize imageSize, SharpRectangleF bounds);

    public string? Description { get; protected set; } 

    protected override void VisitSave(T target)
    {
        target.IsEnabled = IsEnabled;
    }

    protected override void VisitLoad(T source)
    {
        IsEnabled = source.IsEnabled;
    }
}