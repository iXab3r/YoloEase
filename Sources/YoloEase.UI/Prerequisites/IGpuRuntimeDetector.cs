using System.Threading;

namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Detects GPU hardware, driver state, and managed PyTorch runtime capabilities.
/// </summary>
public interface IGpuRuntimeDetector
{
    /// <summary>
    /// Detects GPU adapters and probes PyTorch in the managed virtual environment.
    /// </summary>
    Task<GpuRuntimeInfo> DetectAsync(Action<string>? outputHandler = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Probes only the managed virtual environment's PyTorch runtime.
    /// </summary>
    Task<PytorchRuntimeInfo> ProbePyTorchAsync(Action<string>? outputHandler = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when YOLO CLI additional arguments already include a device selection.
    /// </summary>
    bool HasExplicitDeviceArgument(string? additionalArguments);

    /// <summary>
    /// Builds vendor-specific manual driver guidance for diagnostics.
    /// </summary>
    string BuildDriverGuidance(GpuRuntimeInfo runtimeInfo);

    /// <summary>
    /// Gets a vendor-specific driver help URL when a known GPU vendor is detected.
    /// </summary>
    string? GetDriverHelpUri(GpuRuntimeInfo runtimeInfo);
}
