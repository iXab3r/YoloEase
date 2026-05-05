using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CliWrap;

namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Default GPU detector used by prerequisites and training device selection.
/// </summary>
public sealed partial class GpuRuntimeDetector : IGpuRuntimeDetector
{
    public const string CudaPyTorchIndexUrl = "https://download.pytorch.org/whl/cu130";
    public const string CpuPyTorchIndexUrl = "https://download.pytorch.org/whl/cpu";
    public const string TorchVersion = "2.11.0";
    public const string TorchVisionVersion = "0.26.0";
    public const int MinimumNvidiaCuda13DriverMajor = 580;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
    private readonly IPrerequisitesToolchain toolchain;
    private readonly IPrerequisiteCommandRunner commandRunner;

    public GpuRuntimeDetector(IPrerequisitesToolchain toolchain, IPrerequisiteCommandRunner commandRunner)
    {
        this.toolchain = toolchain;
        this.commandRunner = commandRunner;
    }

    public async Task<GpuRuntimeInfo> DetectAsync(Action<string>? outputHandler = null, CancellationToken cancellationToken = default)
    {
        var adapters = GetWindowsGpuAdapters(outputHandler).ToList();
        foreach (var nvidiaAdapter in await QueryNvidiaSmiAsync(outputHandler, cancellationToken))
        {
            MergeAdapter(adapters, nvidiaAdapter);
        }

        var pytorch = await ProbePyTorchAsync(outputHandler, cancellationToken);
        return new GpuRuntimeInfo
        {
            Adapters = adapters,
            PyTorch = pytorch
        };
    }

    public async Task<PytorchRuntimeInfo> ProbePyTorchAsync(Action<string>? outputHandler = null, CancellationToken cancellationToken = default)
    {
        toolchain.VenvPythonExecutable.Refresh();
        if (!toolchain.VenvPythonExecutable.Exists)
        {
            outputHandler?.Invoke($"Managed Python environment is not ready: {toolchain.VenvPythonExecutable.FullName}");
            return new PytorchRuntimeInfo
            {
                RawOutput = "Managed Python environment is not ready"
            };
        }

        const string code =
            "import torch\n" +
            "print('torch_version=' + str(torch.__version__))\n" +
            "print('torch_cuda=' + str(torch.version.cuda))\n" +
            "available=torch.cuda.is_available()\n" +
            "print('cuda_available=' + str(available))\n" +
            "print('cuda_device_count=' + str(torch.cuda.device_count()))\n" +
            "print('cuda_device_name=' + (torch.cuda.get_device_name(0) if available else ''))\n";

        var result = await commandRunner.RunAsync(
            Cli.Wrap(toolchain.VenvPythonExecutable.FullName).WithArguments(x =>
            {
                x.Add("-c");
                x.Add(code);
            }),
            "pytorch-runtime-probe",
            ProbeTimeout,
            cancellationToken,
            outputHandler);

        if (!result.IsSuccess)
        {
            return new PytorchRuntimeInfo
            {
                RawOutput = result.CombinedOutput
            };
        }

        return ParsePyTorchProbeOutput(result.CombinedOutput);
    }

    public bool HasExplicitDeviceArgument(string? additionalArguments)
    {
        return ExplicitDeviceArgumentRegex().IsMatch(additionalArguments ?? string.Empty);
    }

    public string BuildDriverGuidance(GpuRuntimeInfo runtimeInfo)
    {
        var builder = new StringBuilder();
        if (runtimeInfo.PrimaryNvidiaGpu != null)
        {
            var gpu = runtimeInfo.PrimaryNvidiaGpu;
            builder.AppendLine($"NVIDIA GPU detected: {gpu.Name}");
            builder.AppendLine(string.IsNullOrWhiteSpace(gpu.DriverVersion)
                ? "Driver version was not reported. Install the latest NVIDIA Game Ready or Studio driver."
                : $"Driver version: {gpu.DriverVersion}");
            builder.AppendLine(runtimeInfo.HasCompatibleNvidiaDriver
                ? $"Driver is compatible with CUDA 13.x PyTorch wheels (minimum branch {MinimumNvidiaCuda13DriverMajor})."
                : $"Update the NVIDIA driver to branch {MinimumNvidiaCuda13DriverMajor} or newer for CUDA 13.x PyTorch wheels.");
            builder.AppendLine("YoloEase will not install display drivers automatically because that may require administrator rights and a reboot.");
            builder.AppendLine("Driver page: https://www.nvidia.com/Download/index.aspx");
        }
        else if (runtimeInfo.PrimaryAmdGpu != null)
        {
            var gpu = runtimeInfo.PrimaryAmdGpu;
            builder.AppendLine($"AMD GPU detected: {gpu.Name}");
            builder.AppendLine(string.IsNullOrWhiteSpace(gpu.DriverVersion)
                ? "Driver version was not reported. Install the latest AMD Software driver for your GPU."
                : $"Driver version: {gpu.DriverVersion}");
            builder.AppendLine("YoloEase does not automate AMD ROCm PyTorch installation in this Python 3.11 toolchain.");
            builder.AppendLine("Current AMD ROCm-on-Windows PyTorch guidance uses Python 3.12 wheels and a limited hardware support matrix.");
            builder.AppendLine("Driver page: https://www.amd.com/en/support/download/drivers.html");
        }
        else
        {
            builder.AppendLine("No NVIDIA or AMD training GPU was detected.");
            builder.AppendLine("CPU training remains available.");
        }

        return builder.ToString().TrimEnd();
    }

    public string? GetDriverHelpUri(GpuRuntimeInfo runtimeInfo)
    {
        if (runtimeInfo.PrimaryNvidiaGpu != null)
        {
            return "https://www.nvidia.com/Download/index.aspx";
        }

        if (runtimeInfo.PrimaryAmdGpu != null)
        {
            return "https://www.amd.com/en/support/download/drivers.html";
        }

        return null;
    }

    public static bool IsNvidiaCuda13DriverCompatible(string? driverVersion)
    {
        if (!TryParseDriverMajor(driverVersion, out var major))
        {
            return false;
        }

        return major >= MinimumNvidiaCuda13DriverMajor;
    }

    public static string? ResolveYoloTrainingDeviceArgument(PytorchRuntimeInfo pytorchRuntimeInfo, string? additionalArguments)
    {
        if (ExplicitDeviceArgumentRegex().IsMatch(additionalArguments ?? string.Empty))
        {
            return null;
        }

        return pytorchRuntimeInfo.CudaAvailable ? "device=0" : null;
    }

    public static string NormalizeNvidiaDriverVersion(string? driverVersion)
    {
        if (string.IsNullOrWhiteSpace(driverVersion))
        {
            return string.Empty;
        }

        var trimmed = driverVersion.Trim();
        if (NvidiaDisplayDriverRegex().IsMatch(trimmed))
        {
            return trimmed;
        }

        var parts = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 && int.TryParse(parts[^2], out _) && int.TryParse(parts[^1], out _))
        {
            var encoded = $"{parts[^2]}{parts[^1].PadLeft(4, '0')}";
            if (encoded.Length >= 5)
            {
                var lastFive = encoded[^5..];
                return $"{lastFive[..3]}.{lastFive[3..]}";
            }
        }

        return trimmed;
    }

    public static GpuVendor ClassifyVendor(string? name, string? adapterCompatibility = null, string? pnpDeviceId = null)
    {
        var text = $"{name} {adapterCompatibility} {pnpDeviceId}";
        if (text.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || text.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Nvidia;
        }

        if (text.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Amd;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return GpuVendor.Unknown;
        }

        return GpuVendor.Other;
    }

    public static PytorchRuntimeInfo ParsePyTorchProbeOutput(string? output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (output ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            values[key] = value;
        }

        var version = values.GetValueOrDefault("torch_version") ?? string.Empty;
        var cudaVersion = values.GetValueOrDefault("torch_cuda") ?? string.Empty;
        if (cudaVersion.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            cudaVersion = string.Empty;
        }

        return new PytorchRuntimeInfo
        {
            IsInstalled = !string.IsNullOrWhiteSpace(version),
            Version = version,
            CudaVersion = cudaVersion,
            CudaAvailable = bool.TryParse(values.GetValueOrDefault("cuda_available"), out var cudaAvailable) && cudaAvailable,
            DeviceCount = int.TryParse(values.GetValueOrDefault("cuda_device_count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var deviceCount) ? deviceCount : 0,
            DeviceName = values.GetValueOrDefault("cuda_device_name") ?? string.Empty,
            RawOutput = output ?? string.Empty
        };
    }

    private IEnumerable<GpuAdapterInfo> GetWindowsGpuAdapters(Action<string>? outputHandler)
    {
        var adapters = new List<GpuAdapterInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility, DriverVersion, PNPDeviceID FROM Win32_VideoController");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var name = Convert.ToString(item["Name"]) ?? string.Empty;
                var adapterCompatibility = Convert.ToString(item["AdapterCompatibility"]) ?? string.Empty;
                var driverVersion = Convert.ToString(item["DriverVersion"]) ?? string.Empty;
                var pnpDeviceId = Convert.ToString(item["PNPDeviceID"]) ?? string.Empty;
                var vendor = ClassifyVendor(name, adapterCompatibility, pnpDeviceId);
                if (vendor is GpuVendor.Unknown or GpuVendor.Other)
                {
                    continue;
                }

                adapters.Add(new GpuAdapterInfo
                {
                    Name = name,
                    Vendor = vendor,
                    DriverVersion = vendor == GpuVendor.Nvidia ? NormalizeNvidiaDriverVersion(driverVersion) : driverVersion,
                    Source = "WMI"
                });
            }
        }
        catch (Exception e)
        {
            outputHandler?.Invoke($"Unable to query Windows display adapters: {e.Message}");
        }

        return adapters;
    }

    private async Task<IReadOnlyList<GpuAdapterInfo>> QueryNvidiaSmiAsync(Action<string>? outputHandler, CancellationToken cancellationToken)
    {
        var executable = ResolveNvidiaSmiExecutable();
        if (string.IsNullOrWhiteSpace(executable))
        {
            outputHandler?.Invoke("nvidia-smi was not found");
            return Array.Empty<GpuAdapterInfo>();
        }

        PrerequisiteCommandResult result;
        try
        {
            result = await commandRunner.RunAsync(
                Cli.Wrap(executable).WithArguments("--query-gpu=name,driver_version --format=csv,noheader"),
                "nvidia-smi-query",
                ProbeTimeout,
                cancellationToken,
                outputHandler);
        }
        catch (Exception e)
        {
            outputHandler?.Invoke($"Unable to run nvidia-smi: {e.Message}");
            return Array.Empty<GpuAdapterInfo>();
        }

        if (!result.IsSuccess)
        {
            return Array.Empty<GpuAdapterInfo>();
        }

        var adapters = new List<GpuAdapterInfo>();
        foreach (var line in result.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(',', 2);
            if (parts.Length < 2)
            {
                continue;
            }

            adapters.Add(new GpuAdapterInfo
            {
                Name = parts[0].Trim(),
                Vendor = GpuVendor.Nvidia,
                DriverVersion = NormalizeNvidiaDriverVersion(parts[1]),
                Source = "nvidia-smi"
            });
        }

        return adapters;
    }

    private static void MergeAdapter(List<GpuAdapterInfo> adapters, GpuAdapterInfo adapter)
    {
        var index = adapters.FindIndex(x =>
            x.Vendor == adapter.Vendor &&
            (string.Equals(x.Name, adapter.Name, StringComparison.OrdinalIgnoreCase) ||
             x.Name.Contains(adapter.Name, StringComparison.OrdinalIgnoreCase) ||
             adapter.Name.Contains(x.Name, StringComparison.OrdinalIgnoreCase)));

        if (index < 0)
        {
            adapters.Add(adapter);
            return;
        }

        var existing = adapters[index];
        adapters[index] = existing with
        {
            DriverVersion = string.IsNullOrWhiteSpace(adapter.DriverVersion) ? existing.DriverVersion : adapter.DriverVersion,
            Source = $"{existing.Source}+{adapter.Source}"
        };
    }

    private static string? ResolveNvidiaSmiExecutable()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemExecutable = Path.Combine(windowsDirectory, "System32", "nvidia-smi.exe");
        if (File.Exists(systemExecutable))
        {
            return systemExecutable;
        }

        return "nvidia-smi";
    }

    private static bool TryParseDriverMajor(string? driverVersion, out int major)
    {
        major = 0;
        var normalized = NormalizeNvidiaDriverVersion(driverVersion);
        var match = NvidiaDisplayDriverRegex().Match(normalized);
        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Groups["major"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out major);
    }

    [GeneratedRegex(@"(?:^|\s)device\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ExplicitDeviceArgumentRegex();

    [GeneratedRegex(@"^(?'major'\d{3,})(?:\.\d+)?$", RegexOptions.Compiled)]
    private static partial Regex NvidiaDisplayDriverRegex();
}
