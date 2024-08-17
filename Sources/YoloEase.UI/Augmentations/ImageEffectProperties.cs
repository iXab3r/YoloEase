namespace YoloEase.UI.Augmentations;

public abstract record ImageEffectProperties : IPoeEyeConfigVersioned
{
    public bool IsEnabled { get; set; } = true;
    
    public abstract int Version { get; set; } 
}