namespace YoloEase.UI.Yolo;

public sealed record Yolo8ChecksResult
{
    public string YoloVersion { get; init; }
    public string PythonVersion { get; init; }
    public string TorchVersion { get; init; }
    public string DeviceType { get; init; }
    public string DeviceIndex { get; init; }
    public string DeviceName { get; init; }
}