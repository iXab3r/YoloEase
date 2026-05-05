using System.Linq;

namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Aggregates GPU adapter, driver, and PyTorch runtime information for prerequisite decisions.
/// </summary>
public sealed record GpuRuntimeInfo
{
    /// <summary>
    /// Display adapters discovered on the host machine.
    /// </summary>
    public IReadOnlyList<GpuAdapterInfo> Adapters { get; init; } = Array.Empty<GpuAdapterInfo>();

    /// <summary>
    /// Managed virtual environment's PyTorch runtime state.
    /// </summary>
    public PytorchRuntimeInfo PyTorch { get; init; } = new();

    public bool HasNvidiaGpu => Adapters.Any(x => x.Vendor == GpuVendor.Nvidia);

    public bool HasAmdGpu => Adapters.Any(x => x.Vendor == GpuVendor.Amd);

    public GpuAdapterInfo? PrimaryNvidiaGpu => Adapters.FirstOrDefault(x => x.Vendor == GpuVendor.Nvidia);

    public GpuAdapterInfo? PrimaryAmdGpu => Adapters.FirstOrDefault(x => x.Vendor == GpuVendor.Amd);

    public bool HasCompatibleNvidiaDriver => PrimaryNvidiaGpu != null && GpuRuntimeDetector.IsNvidiaCuda13DriverCompatible(PrimaryNvidiaGpu.DriverVersion);

    public bool ShouldInstallCudaPyTorch => HasCompatibleNvidiaDriver;
}
