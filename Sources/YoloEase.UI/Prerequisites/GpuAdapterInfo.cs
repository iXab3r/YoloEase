namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Describes a display adapter discovered from Windows or vendor tooling.
/// </summary>
public sealed record GpuAdapterInfo
{
    /// <summary>
    /// Human-readable adapter name as reported by Windows or vendor tooling.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Classified vendor family.
    /// </summary>
    public GpuVendor Vendor { get; init; } = GpuVendor.Unknown;

    /// <summary>
    /// Driver version in vendor display form when available.
    /// </summary>
    public string DriverVersion { get; init; } = string.Empty;

    /// <summary>
    /// Source that reported the adapter, for example WMI or nvidia-smi.
    /// </summary>
    public string Source { get; init; } = string.Empty;
}
