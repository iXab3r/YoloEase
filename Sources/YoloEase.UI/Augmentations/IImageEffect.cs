namespace YoloEase.UI.Augmentations;

/// <summary>
/// Defines an augmentation effect that can transform images and their annotation rectangles.
/// </summary>
public interface IImageEffect : IYoloObject
{
    bool IsEnabled { get; set; }
    
    string Name { get; set; }
    
    string Description { get; }

    string GetSettingsHash();
    

    void Mutate(SharpImage imageFile);
    
    SharpRectangleF Mutate(SharpSize imageSize, SharpRectangleF bounds);
}
