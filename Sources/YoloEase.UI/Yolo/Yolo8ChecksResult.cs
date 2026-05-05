namespace YoloEase.UI.Yolo;

/// <summary>
/// Contains the environment details parsed from <c>yolo checks</c> output.
/// </summary>
public sealed record Yolo8ChecksResult
{
    public string YoloVersion { get; init; }
    public string PythonVersion { get; init; }
    public string TorchVersion { get; init; }
    public string DeviceType { get; init; }
    public string DeviceIndex { get; init; }
    public string DeviceName { get; init; }
}
