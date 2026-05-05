namespace YoloEase.UI.Prerequisites;

/// <summary>
/// Builds the ordered prerequisite suite for managed Python, packages, YOLO, CVAT CLI, and GPU detection.
/// </summary>
public sealed class PrerequisitesSuiteFactory
{
    private readonly PrerequisitesInstaller installer;

    public PrerequisitesSuiteFactory(PrerequisitesInstaller installer)
    {
        this.installer = installer;
    }

    public CheckSuite Create()
    {
        var suite = new CheckSuite();

        var python = new CheckItem
        {
            Name = "python",
            Title = "Managed Python 3.11",
            Details = "YoloEase uses its own Python runtime under the application data directory, separate from global PATH."
        }
        .WithEvaluation(installer.CheckPythonAsync)
        .WithRemediation(installer.InstallPythonAsync);
        suite.AddCheck(python);

        var venv = new CheckItem
        {
            Name = "venv",
            Title = "Python environment",
            Details = "The managed virtual environment keeps YoloEase packages isolated from the rest of the machine."
        }
        .WithEvaluation(installer.CheckVenvAsync)
        .WithRemediation(installer.CreateVenvAsync)
        .DependsOn(python);
        suite.AddCheck(venv);

        var pip = new CheckItem
        {
            Name = "pip",
            Title = "Package installer",
            Details = "pip, wheel, and setuptools must be available inside the managed environment."
        }
        .WithEvaluation(installer.CheckPipAsync)
        .WithRemediation(installer.UpgradePipAsync)
        .DependsOn(venv);
        suite.AddCheck(pip);

        var gpuDriver = new CheckItem
        {
            Name = "gpu-driver",
            Title = "GPU driver helper",
            Details = "Shows NVIDIA or AMD driver guidance. Driver installation is never part of automatic prerequisite installation.",
            IsRequired = false,
            ShowEvaluateButton = true
        }
        .WithEvaluation(installer.CheckGpuDriverHelpAsync)
        .WithRemediation(installer.OpenGpuDriverHelpAsync, "Open help", includeInBulkInstall: false);
        suite.AddCheck(gpuDriver);

        var pytorchRuntime = new CheckItem
        {
            Name = "pytorch-runtime",
            Title = "PyTorch runtime",
            Details = "Installs CPU PyTorch by default, or CUDA 13.0 PyTorch wheels when a compatible NVIDIA driver is detected."
        }
        .WithEvaluation(installer.CheckPytorchRuntimeAsync)
        .WithRemediation(installer.InstallPytorchRuntimeAsync)
        .DependsOn(pip);
        suite.AddCheck(pytorchRuntime);

        var packages = new CheckItem
        {
            Name = "packages",
            Title = "Python packages",
            Details = "Installs and verifies Ultralytics, CVAT SDK/CLI, OpenCV, ONNX Runtime, NumPy, Matplotlib, and Shapely."
        }
        .WithEvaluation(installer.CheckPackagesAsync)
        .WithRemediation(installer.InstallPackagesAsync)
        .DependsOn(pytorchRuntime);
        suite.AddCheck(packages);

        var yolo = new CheckItem
        {
            Name = "yolo",
            Title = "Yolo CLI",
            Details = "Runs yolo checks from the managed environment and verifies that Ultralytics can start."
        }
        .WithEvaluation(installer.CheckYoloAsync)
        .WithRemediation(installer.InstallPackagesAsync)
        .DependsOn(packages);
        suite.AddCheck(yolo);

        var cvatCli = new CheckItem
        {
            Name = "cvat-cli",
            Title = "CVAT CLI",
            Details = "Verifies the cvat-cli executable used for CVAT annotation download and task operations."
        }
        .WithEvaluation(installer.CheckCvatCliAsync)
        .WithRemediation(installer.InstallPackagesAsync)
        .DependsOn(packages);
        suite.AddCheck(cvatCli);

        suite.AddCheck(new CheckItem
        {
            Name = "gpu",
            Title = "GPU acceleration",
            Details = "GPU is detected through PyTorch. CPU-only training is allowed; this row is informational.",
            IsRequired = false,
            ShowEvaluateButton = true
        }
        .WithEvaluation(installer.CheckGpuAsync)
        .DependsOn(packages));

        return suite;
    }
}
