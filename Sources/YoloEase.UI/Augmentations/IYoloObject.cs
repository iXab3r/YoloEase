namespace YoloEase.UI.Augmentations;

/// <summary>
/// Base contract for persisted, configurable YOLO pipeline objects.
/// </summary>
public interface IYoloObject : IDisposableReactiveObject
{
    IPoeEyeConfigVersioned Properties { get; set; }
}
