namespace YoloEase.UI.Augmentations;

public interface IYoloObject : IDisposableReactiveObject
{
    IPoeEyeConfigVersioned Properties { get; set; }
}