namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Captures the managed Python environment's PyTorch runtime state.
/// </summary>
public sealed record PytorchRuntimeInfo
{
    /// <summary>
    /// True when PyTorch can be imported from the managed virtual environment.
    /// </summary>
    public bool IsInstalled { get; init; }

    /// <summary>
    /// PyTorch package version, including local build suffix such as +cpu or +cu130.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// CUDA runtime version reported by PyTorch, or empty for CPU builds.
    /// </summary>
    public string CudaVersion { get; init; } = string.Empty;

    /// <summary>
    /// True when PyTorch can access a CUDA-compatible compute device.
    /// </summary>
    public bool CudaAvailable { get; init; }

    /// <summary>
    /// Number of CUDA devices visible to PyTorch.
    /// </summary>
    public int DeviceCount { get; init; }

    /// <summary>
    /// First CUDA device name when available.
    /// </summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>
    /// Raw sanitized probe output used for diagnostics.
    /// </summary>
    public string RawOutput { get; init; } = string.Empty;

    public bool IsCudaBuild => !string.IsNullOrWhiteSpace(CudaVersion) || Version.Contains("+cu", StringComparison.OrdinalIgnoreCase);

    public bool IsCpuBuild => IsInstalled && !IsCudaBuild;

    public string Summary
    {
        get
        {
            if (!IsInstalled)
            {
                return "PyTorch is not installed";
            }

            var flavor = IsCudaBuild ? $"CUDA {CudaVersion}" : "CPU";
            var device = CudaAvailable
                ? $", device: {DeviceName}"
                : string.Empty;
            return $"PyTorch {Version} ({flavor}{device})";
        }
    }
}
