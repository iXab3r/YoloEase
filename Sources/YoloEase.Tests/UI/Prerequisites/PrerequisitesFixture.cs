using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using CliWrap;
using Moq;
using PoeShared.Modularity;
using Shouldly;
using YoloEase.UI.Cvat;
using YoloEase.UI.Prerequisites;
using YoloEase.UI.Yolo;

namespace YoloEase.Tests.UI.Prerequisites;

/// <summary>
/// Covers prerequisite checks, managed toolchain path resolution, and wrapper integration points.
/// </summary>
public class PrerequisitesFixture
{
    /// <summary>
    /// WHAT: Verifies that prerequisite checks are enabled for fresh application configuration.
    /// HOW: Creates the default config record and checks the startup flag value.
    /// </summary>
    [Test]
    public void ShouldEnableStartupChecksByDefault()
    {
        // Given
        var config = new YoloEaseApplicationConfig();

        // When
        var checkAtStartup = config.CheckPrerequisitesAtStartup;

        // Then
        checkAtStartup.ShouldBeTrue();
    }

    /// <summary>
    /// WHAT: Verifies that the prerequisites view model persists the startup-check preference.
    /// HOW: Toggles the setting through the view model and inspects the backing config provider.
    /// </summary>
    [Test]
    public async Task ShouldSaveStartupCheckPreference()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var suite = new CheckSuite();
        var configProvider = new TestConfigProvider(new YoloEaseApplicationConfig());
        var viewModel = CreateViewModel(configProvider, suite, temp);

        // When
        await viewModel.SetCheckPrerequisitesAtStartup(false);

        // Then
        configProvider.ActualConfig.CheckPrerequisitesAtStartup.ShouldBeFalse();
        viewModel.CheckPrerequisitesAtStartup.ShouldBeFalse();
    }

    /// <summary>
    /// WHAT: Verifies that managed prerequisite paths stay inside the application data tools directory.
    /// HOW: Resolves a toolchain from a temporary app data root and checks each derived path.
    /// </summary>
    [Test]
    public void ShouldResolveManagedToolchainUnderAppDataDirectory()
    {
        // Given
        using var temp = new TemporaryDirectory();

        // When
        var toolchain = CreateToolchain(temp);

        // Then
        toolchain.ToolsRoot.FullName.ShouldBe(Path.Combine(temp.Path, "tools"));
        toolchain.DownloadsDirectory.FullName.ShouldBe(Path.Combine(temp.Path, "tools", "downloads"));
        toolchain.PythonDirectory.FullName.ShouldBe(Path.Combine(temp.Path, "tools", "python-3.11"));
        toolchain.VenvDirectory.FullName.ShouldBe(Path.Combine(temp.Path, "tools", "venv"));
        toolchain.LogsDirectory.FullName.ShouldBe(Path.Combine(temp.Path, "tools", "logs"));
        toolchain.YoloExecutable.FullName.ShouldBe(Path.Combine(temp.Path, "tools", "venv", "Scripts", "yolo.exe"));
        toolchain.CvatCliExecutable.FullName.ShouldBe(Path.Combine(temp.Path, "tools", "venv", "Scripts", "cvat-cli.exe"));
        toolchain.PythonArchiveUri.AbsoluteUri.ShouldContain("python-build-standalone");
        toolchain.PythonArchiveUri.AbsoluteUri.ShouldContain("cpython-3.11.9%2B20240726-x86_64-pc-windows-msvc");
        toolchain.PythonArchiveSha256.ShouldBe("2e67e46b1e59d12583f3079c97dba46de3c8a158c9a83234a31613e969d0fd90");

        // When
        toolchain.EnsureBaseDirectories();

        // Then
        toolchain.DownloadsDirectory.Refresh();
        toolchain.LogsDirectory.Refresh();
        toolchain.DownloadsDirectory.Exists.ShouldBeTrue();
        toolchain.LogsDirectory.Exists.ShouldBeTrue();
    }

    /// <summary>
    /// WHAT: Verifies that destructive managed-tool operations cannot escape the tools root.
    /// HOW: Allows a known managed path and rejects the temporary app data root itself.
    /// </summary>
    [Test]
    public void ShouldRejectManagedOperationsOutsideToolsRoot()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var toolchain = CreateToolchain(temp);

        // When / Then
        Should.NotThrow(() => toolchain.EnsureManagedPath(toolchain.VenvDirectory));
        Should.Throw<InvalidOperationException>(() => toolchain.EnsureManagedPath(new DirectoryInfo(temp.Path)));
    }

    /// <summary>
    /// WHAT: Verifies that missing managed executables are reported as prerequisite failures.
    /// HOW: Requests the YOLO executable from an empty temporary toolchain root.
    /// </summary>
    [Test]
    public void ShouldRequireManagedExecutables()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var toolchain = CreateToolchain(temp);

        // When
        var error = Should.Throw<PrerequisitesMissingException>(() => toolchain.RequireYoloExecutable());

        // Then
        error.Message.ShouldContain("managed environment");
    }

    /// <summary>
    /// WHAT: Verifies GPU vendor, NVIDIA driver, and PyTorch probe parsing used by prerequisite decisions.
    /// HOW: Parses representative NVIDIA/AMD and PyTorch CUDA diagnostic strings without touching real hardware.
    /// </summary>
    [Test]
    public void ShouldParseGpuAndPytorchRuntimeDiagnostics()
    {
        // When / Then
        GpuRuntimeDetector.ClassifyVendor("NVIDIA GeForce RTX 5070", "NVIDIA", "PCI\\VEN_10DE").ShouldBe(GpuVendor.Nvidia);
        GpuRuntimeDetector.ClassifyVendor("AMD Radeon RX 7900 XTX", "Advanced Micro Devices", "PCI\\VEN_1002").ShouldBe(GpuVendor.Amd);
        GpuRuntimeDetector.NormalizeNvidiaDriverVersion("32.0.15.8180").ShouldBe("581.80");
        GpuRuntimeDetector.IsNvidiaCuda13DriverCompatible("581.80").ShouldBeTrue();
        GpuRuntimeDetector.IsNvidiaCuda13DriverCompatible("579.99").ShouldBeFalse();

        // When
        var pytorch = GpuRuntimeDetector.ParsePyTorchProbeOutput("""
            torch_version=2.11.0+cu130
            torch_cuda=13.0
            cuda_available=True
            cuda_device_count=1
            cuda_device_name=NVIDIA GeForce RTX 5070
            """);

        // Then
        pytorch.IsInstalled.ShouldBeTrue();
        pytorch.IsCudaBuild.ShouldBeTrue();
        pytorch.CudaAvailable.ShouldBeTrue();
        pytorch.DeviceName.ShouldBe("NVIDIA GeForce RTX 5070");
    }

    /// <summary>
    /// WHAT: Verifies YOLO training device argument selection from the probed PyTorch runtime.
    /// HOW: Checks CUDA, explicit user override, and CPU-only runtime branches.
    /// </summary>
    [Test]
    public void ShouldResolveYoloTrainingDeviceArgument()
    {
        // Given
        var cudaRuntime = new PytorchRuntimeInfo
        {
            IsInstalled = true,
            Version = "2.11.0+cu130",
            CudaVersion = "13.0",
            CudaAvailable = true
        };
        var cpuRuntime = cudaRuntime with
        {
            Version = "2.11.0+cpu",
            CudaVersion = string.Empty,
            CudaAvailable = false
        };

        // When / Then
        GpuRuntimeDetector.ResolveYoloTrainingDeviceArgument(cudaRuntime, null).ShouldBe("device=0");
        GpuRuntimeDetector.ResolveYoloTrainingDeviceArgument(cudaRuntime, "epochs=1 device=cpu").ShouldBeNull();
        GpuRuntimeDetector.ResolveYoloTrainingDeviceArgument(cpuRuntime, null).ShouldBeNull();
    }

    /// <summary>
    /// WHAT: Verifies that startup checks run immediately when enabled.
    /// HOW: Adds a counting check to the suite and invokes the startup request path.
    /// </summary>
    [Test]
    public async Task ShouldRunStartupCheckWhenEnabled()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var calls = 0;
        var suite = new CheckSuite();
        suite.AddCheck(new CheckItem {Name = "python", Title = "Python"}
            .WithEvaluation((_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult<bool?>(true);
            }));
        var viewModel = CreateViewModel(new TestConfigProvider(new YoloEaseApplicationConfig
        {
            CheckPrerequisitesAtStartup = true
        }), suite, temp);

        // When
        await viewModel.RequestStartupCheckAsync();

        // Then
        calls.ShouldBe(1);
        viewModel.HasEverEvaluated.ShouldBeTrue();
        viewModel.HasMissingRequired.ShouldBeFalse();
    }

    /// <summary>
    /// WHAT: Verifies that disabled startup checks wait until the prerequisites tab is opened.
    /// HOW: Disables the startup flag, requests startup evaluation, then activates the tab.
    /// </summary>
    [Test]
    public async Task ShouldDeferCheckUntilTabActivationWhenStartupDisabled()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var calls = 0;
        var suite = new CheckSuite();
        suite.AddCheck(new CheckItem {Name = "python", Title = "Python"}
            .WithEvaluation((_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult<bool?>(true);
            }));
        var viewModel = CreateViewModel(new TestConfigProvider(new YoloEaseApplicationConfig
        {
            CheckPrerequisitesAtStartup = false
        }), suite, temp);

        // When
        await viewModel.RequestStartupCheckAsync();

        // Then
        calls.ShouldBe(0);
        viewModel.HasEverEvaluated.ShouldBeFalse();

        // When
        await viewModel.NotifyTabActivated();

        // Then
        calls.ShouldBe(1);
        viewModel.HasEverEvaluated.ShouldBeTrue();
    }

    /// <summary>
    /// WHAT: Verifies that concurrent evaluations of the same check share one in-flight operation.
    /// HOW: Blocks the first evaluation, starts a second, then releases both and counts executions.
    /// </summary>
    [Test]
    public async Task ShouldSingleFlightCheckItemEvaluation()
    {
        // Given
        var calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new CheckItem {Name = "single-flight", Title = "Single flight"}
            .WithEvaluation(async (_, _) =>
            {
                Interlocked.Increment(ref calls);
                started.TrySetResult();
                await release.Task;
                return true;
            });

        // When
        var first = check.EvaluateAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = check.EvaluateAsync();
        await Task.Delay(50);
        release.SetResult();
        await Task.WhenAll(first, second);

        // Then
        calls.ShouldBe(1);
        check.IsSatisfied.ShouldBe(true);
    }

    /// <summary>
    /// WHAT: Verifies that evaluation exceptions are surfaced in check diagnostics.
    /// HOW: Runs a check that throws and inspects the stored error and expansion state.
    /// </summary>
    [Test]
    public async Task ShouldCaptureCheckErrors()
    {
        // Given
        var check = new CheckItem {Name = "broken", Title = "Broken"}
            .WithEvaluation((_, _) => throw new InvalidOperationException("boom"));

        // When
        var result = await check.EvaluateAsync();

        // Then
        result.ShouldBe(false);
        check.LastError.ShouldNotBeNull();
        check.LastError.Value.Message.ShouldContain("boom");
        check.IsExpanded.ShouldBeTrue();
    }

    /// <summary>
    /// WHAT: Verifies that dependent checks do not run while required checks are missing.
    /// HOW: Creates a failing Python check and confirms the dependent Pip check short-circuits.
    /// </summary>
    [Test]
    public async Task ShouldBlockDependentChecksUntilDependenciesAreReady()
    {
        // Given
        var dependentChecks = 0;
        var remediationCalls = 0;
        var python = new CheckItem {Name = "python", Title = "Python"}
            .WithEvaluation((_, _) => Task.FromResult<bool?>(false));
        var pip = new CheckItem {Name = "pip", Title = "Pip"}
            .WithEvaluation((_, _) =>
            {
                Interlocked.Increment(ref dependentChecks);
                return Task.FromResult<bool?>(true);
            })
            .WithRemediation((_, _) =>
            {
                Interlocked.Increment(ref remediationCalls);
                return Task.CompletedTask;
            })
            .DependsOn(python);

        pip.IsBlocked.ShouldBeTrue();
        pip.CanEvaluate.ShouldBeFalse();
        pip.CanRemediate.ShouldBeFalse();

        // When
        var stopwatch = Stopwatch.StartNew();
        var result = await pip.EvaluateAsync();
        await pip.RemediateAsync();

        // Then
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(500);
        result.ShouldBe(false);
        dependentChecks.ShouldBe(0);
        remediationCalls.ShouldBe(0);
        pip.DependencyStatusText.ShouldContain("Python");
    }

    /// <summary>
    /// WHAT: Verifies that a dependent check becomes available after its dependency passes.
    /// HOW: Evaluates the Python dependency first, then evaluates the Pip check.
    /// </summary>
    [Test]
    public async Task ShouldEnableDependentChecksAfterDependencyPasses()
    {
        // Given
        var dependentChecks = 0;
        var python = new CheckItem {Name = "python", Title = "Python"}
            .WithEvaluation((_, _) => Task.FromResult<bool?>(true));
        var pip = new CheckItem {Name = "pip", Title = "Pip"}
            .WithEvaluation((_, _) =>
            {
                Interlocked.Increment(ref dependentChecks);
                return Task.FromResult<bool?>(true);
            })
            .DependsOn(python);

        // When
        await python.EvaluateAsync();

        // Then
        pip.IsBlocked.ShouldBeFalse();
        pip.CanEvaluate.ShouldBeTrue();

        // When
        var result = await pip.EvaluateAsync();

        // Then
        result.ShouldBe(true);
        dependentChecks.ShouldBe(1);
    }

    /// <summary>
    /// WHAT: Verifies that check rows stay visibly busy long enough for UI feedback.
    /// HOW: Times a successful check whose actual work completes immediately.
    /// </summary>
    [Test]
    public async Task ShouldKeepChecksBusyForAtLeastOneSecond()
    {
        // Given
        var check = new CheckItem {Name = "duration", Title = "Duration"}
            .WithEvaluation((_, _) => Task.FromResult<bool?>(true));

        // When
        var stopwatch = Stopwatch.StartNew();
        await check.EvaluateAsync();

        // Then
        stopwatch.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(950);
    }

    /// <summary>
    /// WHAT: Verifies that manual remediation rows are not executed by bulk installation.
    /// HOW: Adds one manual and one automatic check, then runs suite remediation.
    /// </summary>
    [Test]
    public async Task ShouldExcludeManualChecksFromBulkInstall()
    {
        // Given
        var manualCalls = 0;
        var automaticCalls = 0;
        var suite = new CheckSuite();
        suite.AddCheck(new CheckItem {Name = "manual", Title = "Manual"}
            .WithEvaluation((_, _) => Task.FromResult<bool?>(false))
            .WithRemediation((_, _) =>
            {
                Interlocked.Increment(ref manualCalls);
                return Task.CompletedTask;
            }, "Open help", includeInBulkInstall: false));
        suite.AddCheck(new CheckItem {Name = "automatic", Title = "Automatic"}
            .WithEvaluation((_, _) => Task.FromResult<bool?>(false))
            .WithRemediation((_, _) =>
            {
                Interlocked.Increment(ref automaticCalls);
                return Task.CompletedTask;
            }));

        // When
        await suite.RemediateFailedAsync();

        // Then
        manualCalls.ShouldBe(0);
        automaticCalls.ShouldBe(1);
    }

    /// <summary>
    /// WHAT: Verifies that view-model-triggered prerequisite checks leave the caller thread.
    /// HOW: Starts evaluation from a dedicated thread and records the thread that ran the check action.
    /// </summary>
    [Test]
    public void ShouldRunViewModelChecksOffCallingThread()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var suite = new CheckSuite();
        var callerThreadId = -1;
        var checkThreadId = -1;
        Exception? error = null;
        suite.AddCheck(new CheckItem {Name = "background", Title = "Background"}
            .WithEvaluation((_, _) =>
            {
                checkThreadId = Environment.CurrentManagedThreadId;
                return Task.FromResult<bool?>(true);
            }));
        var viewModel = CreateViewModel(new TestConfigProvider(new YoloEaseApplicationConfig()), suite, temp);

        // When
        var callerThread = new Thread(() =>
        {
            callerThreadId = Environment.CurrentManagedThreadId;
            try
            {
                viewModel.EvaluateAllAsync().GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        callerThread.Start();

        // Then
        callerThread.Join(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        error.ShouldBeNull();
        checkThreadId.ShouldNotBe(callerThreadId);
    }

    /// <summary>
    /// WHAT: Verifies that pressing Fix for one prerequisite always verifies it afterward.
    /// HOW: Runs a single-row remediation through the view model and counts the follow-up evaluation.
    /// </summary>
    [Test]
    public async Task ShouldCheckAfterSinglePrerequisiteFix()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var suite = new CheckSuite();
        var evaluationCalls = 0;
        var remediationCalls = 0;
        var check = new CheckItem {Name = "fix-then-check", Title = "Fix then check"}
            .WithEvaluation((_, _) =>
            {
                Interlocked.Increment(ref evaluationCalls);
                return Task.FromResult<bool?>(true);
            })
            .WithRemediation((_, _) =>
            {
                Interlocked.Increment(ref remediationCalls);
                return Task.CompletedTask;
            });
        suite.AddCheck(check);
        var viewModel = CreateViewModel(new TestConfigProvider(new YoloEaseApplicationConfig()), suite, temp);

        // When
        await viewModel.RemediateCheckAsync(check);

        // Then
        remediationCalls.ShouldBe(1);
        evaluationCalls.ShouldBe(1);
        check.IsSatisfied.ShouldBe(true);
    }

    /// <summary>
    /// WHAT: Verifies that bulk remediation verifies an automatic fix afterward.
    /// HOW: Runs suite remediation and asserts the check action ran before and after the fix.
    /// </summary>
    [Test]
    public async Task ShouldCheckAfterBulkPrerequisiteFix()
    {
        // Given
        var suite = new CheckSuite();
        var evaluationCalls = 0;
        var isFixed = false;
        var check = new CheckItem {Name = "bulk-fix", Title = "Bulk fix"}
            .WithEvaluation((_, _) =>
            {
                Interlocked.Increment(ref evaluationCalls);
                return Task.FromResult<bool?>(isFixed);
            })
            .WithRemediation((_, _) =>
            {
                isFixed = true;
                return Task.CompletedTask;
            });
        suite.AddCheck(check);

        // When
        await suite.RemediateFailedAsync();

        // Then
        evaluationCalls.ShouldBe(2);
        check.IsSatisfied.ShouldBe(true);
    }

    /// <summary>
    /// WHAT: Verifies that prerequisite command logs capture output while redacting secrets.
    /// HOW: Runs a shell command that writes sensitive-looking stdout and stderr values.
    /// </summary>
    [Test]
    public async Task ShouldCaptureAndRedactCommandOutput()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var toolchain = CreateToolchain(temp);
        var runner = new PrerequisiteCommandRunner(toolchain);
        var cmd = Cli.Wrap(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
            .WithArguments(new[] {"/c", "echo password=secret && echo Authorization: Bearer abc 1>&2"});

        // When
        var result = await runner.RunAsync(cmd, "redaction:test", TimeSpan.FromSeconds(10));

        // Then
        result.StandardOutput.ShouldContain("password=<redacted>");
        result.StandardOutput.ShouldNotContain("secret");
        result.StandardError.ShouldContain("Authorization: <redacted>");
        result.StandardError.ShouldNotContain("Bearer abc");
        Directory.GetFiles(toolchain.LogsDirectory.FullName, "*redaction-test*.log").Length.ShouldBe(1);
    }

    /// <summary>
    /// WHAT: Verifies prerequisite commands prefer the managed Python toolchain over machine Python state.
    /// HOW: Runs a shell command through the prerequisite runner and inspects the injected process environment.
    /// </summary>
    [Test]
    public async Task ShouldRunPrerequisiteCommandsWithManagedPythonEnvironment()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var toolchain = CreateToolchain(temp);
        var runner = new PrerequisiteCommandRunner(toolchain);
        var expectedVenvScripts = Path.Combine(toolchain.VenvDirectory.FullName, "Scripts");
        var expectedManagedPython = toolchain.PythonDirectory.FullName;
        var cmd = Cli.Wrap(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
            .WithArguments(new[]
            {
                "/c",
                "echo PATH=%PATH% && echo PYTHONNOUSERSITE=%PYTHONNOUSERSITE% && echo PIP_REQUIRE_VIRTUALENV=%PIP_REQUIRE_VIRTUALENV% && echo PYTHONPATH=%PYTHONPATH% && echo PYTHONHOME=%PYTHONHOME%"
            });

        // When
        var result = await runner.RunAsync(cmd, "managed-python-env", TimeSpan.FromSeconds(10));

        // Then
        result.CombinedOutput.ShouldContain($"PATH={expectedVenvScripts}");
        result.CombinedOutput.ShouldContain(expectedManagedPython);
        result.CombinedOutput.ShouldContain("PYTHONNOUSERSITE=1");
        result.CombinedOutput.ShouldContain("PIP_REQUIRE_VIRTUALENV=1");
        result.CombinedOutput.ShouldContain("PYTHONPATH=");
        result.CombinedOutput.ShouldContain("PYTHONHOME=");
    }

    /// <summary>
    /// WHAT: Verifies that Python remediation installs from the pinned portable archive without invoking the normal installer.
    /// HOW: Creates a tiny cached tar.gz archive, verifies its hash, extracts it, and checks the managed executable.
    /// </summary>
    [Test]
    public async Task ShouldInstallPythonFromPinnedArchive()
    {
        // Given
        using var appData = new TemporaryDirectory();
        var toolsRoot = new DirectoryInfo(Path.Combine(appData.Path, "tools"));
        var downloadsDirectory = new DirectoryInfo(Path.Combine(toolsRoot.FullName, "downloads"));
        var pythonDirectory = new DirectoryInfo(Path.Combine(toolsRoot.FullName, "python-3.11"));
        var pythonExecutable = new FileInfo(Path.Combine(pythonDirectory.FullName, "python.exe"));
        var archiveUri = new Uri("https://example.test/cpython-test.tar.gz");

        Directory.CreateDirectory(downloadsDirectory.FullName);
        var archive = new FileInfo(Path.Combine(downloadsDirectory.FullName, "cpython-test.tar.gz"));
        CreatePythonArchive(archive);
        var archiveSha256 = ComputeSha256(archive);

        var toolchain = new Mock<IPrerequisitesToolchain>(MockBehavior.Strict);
        toolchain
            .SetupGet(x => x.DownloadsDirectory)
            .Returns(downloadsDirectory);
        toolchain
            .SetupGet(x => x.PythonDirectory)
            .Returns(pythonDirectory);
        toolchain
            .SetupGet(x => x.PythonExecutable)
            .Returns(pythonExecutable);
        toolchain
            .SetupGet(x => x.PythonArchiveUri)
            .Returns(archiveUri);
        toolchain
            .SetupGet(x => x.PythonArchiveSha256)
            .Returns(archiveSha256);
        toolchain
            .Setup(x => x.EnsureBaseDirectories())
            .Callback(() =>
            {
                Directory.CreateDirectory(toolsRoot.FullName);
                Directory.CreateDirectory(downloadsDirectory.FullName);
            });
        toolchain
            .Setup(x => x.EnsureManagedPath(It.IsAny<FileSystemInfo>()));

        var runner = new Mock<IPrerequisiteCommandRunner>(MockBehavior.Strict);
        runner
            .Setup(x => x.RunAsync(
                It.IsAny<Command>(),
                "python-archive-extract",
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>>()))
            .Callback(() =>
            {
                var extractionDirectory = downloadsDirectory.EnumerateDirectories("python-extract-*").Single();
                var extractedPythonDirectory = Path.Combine(extractionDirectory.FullName, "python");
                Directory.CreateDirectory(Path.Combine(extractedPythonDirectory, "Lib"));
                File.WriteAllText(Path.Combine(extractedPythonDirectory, "python.exe"), "fake executable");
                File.WriteAllText(Path.Combine(extractedPythonDirectory, "Lib", "os.py"), "fake library");
            })
            .ReturnsAsync(new PrerequisiteCommandResult {ExitCode = 0});
        runner
            .Setup(x => x.RunAsync(
                It.IsAny<Command>(),
                "python-version",
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>>()))
            .ReturnsAsync(new PrerequisiteCommandResult {ExitCode = 0, StandardOutput = "Python 3.11.9"});

        var installer = new PrerequisitesInstaller(
            toolchain.Object,
            runner.Object,
            Mock.Of<IGpuRuntimeDetector>());
        var check = new CheckItem {Name = "python", Title = "Python"};

        // When
        await installer.InstallPythonAsync(check, CancellationToken.None);

        // Then
        pythonExecutable.Refresh();
        pythonExecutable.Exists.ShouldBeTrue();
        File.Exists(Path.Combine(pythonDirectory.FullName, "Lib", "os.py")).ShouldBeTrue();
        check.LastOutput.ShouldContain("Using cached Python archive");
        check.LastOutput.ShouldContain("Extracting Python archive");
        check.LastOutput.ShouldContain("Managed Python installed");
        runner.VerifyAll();
        toolchain.VerifyAll();
    }

    /// <summary>
    /// WHAT: Verifies that venv verification sees files created after the toolchain was resolved.
    /// HOW: Caches a missing FileInfo state, creates the venv Python launcher, then checks the environment.
    /// </summary>
    [Test]
    public async Task ShouldRefreshManagedPythonEnvironmentExecutableBeforeCheck()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var toolchain = CreateToolchain(temp);
        toolchain.VenvPythonExecutable.Exists.ShouldBeFalse();
        Directory.CreateDirectory(toolchain.VenvPythonExecutable.Directory!.FullName);
        File.WriteAllText(toolchain.VenvPythonExecutable.FullName, string.Empty);

        var runner = new Mock<IPrerequisiteCommandRunner>(MockBehavior.Strict);
        runner
            .Setup(x => x.RunAsync(
                It.IsAny<Command>(),
                "venv-python-version",
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>>()))
            .ReturnsAsync(new PrerequisiteCommandResult {ExitCode = 0, StandardOutput = "Python 3.11.9"});
        var installer = new PrerequisitesInstaller(
            toolchain,
            runner.Object,
            Mock.Of<IGpuRuntimeDetector>());
        var check = new CheckItem {Name = "venv", Title = "Python environment"};

        // When
        var result = await installer.CheckVenvAsync(check, CancellationToken.None);

        // Then
        result.ShouldBe(true);
        check.LastOutput.ShouldNotContain("Expected at");
        runner.VerifyAll();
    }

    /// <summary>
    /// WHAT: Verifies that a successful venv command still must create the expected Python launcher.
    /// HOW: Mocks a zero-exit venv creation without writing Scripts\python.exe and checks the clear failure.
    /// </summary>
    [Test]
    public async Task ShouldReportMissingPythonLauncherAfterVenvCreate()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var toolchain = CreateToolchain(temp);
        Directory.CreateDirectory(toolchain.PythonExecutable.Directory!.FullName);
        File.WriteAllText(toolchain.PythonExecutable.FullName, string.Empty);

        var runner = new Mock<IPrerequisiteCommandRunner>(MockBehavior.Strict);
        runner
            .Setup(x => x.RunAsync(
                It.IsAny<Command>(),
                "venv-create",
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>>()))
            .ReturnsAsync(new PrerequisiteCommandResult {ExitCode = 0});
        var installer = new PrerequisitesInstaller(
            toolchain,
            runner.Object,
            Mock.Of<IGpuRuntimeDetector>());
        var check = new CheckItem {Name = "venv", Title = "Python environment"};

        // When
        var error = await Should.ThrowAsync<InvalidOperationException>(() => installer.CreateVenvAsync(check, CancellationToken.None));

        // Then
        error.Message.ShouldContain("expected Python executable was not created");
        error.Message.ShouldContain(toolchain.VenvPythonExecutable.FullName);
        runner.VerifyAll();
    }

    /// <summary>
    /// WHAT: Verifies that a failed single-row prerequisite fix remains visible instead of being hidden by verification.
    /// HOW: Runs a fix that throws and confirms no follow-up check clears the recorded error.
    /// </summary>
    [Test]
    public async Task ShouldKeepInstallErrorVisibleAfterSinglePrerequisiteFixFailure()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var suite = new CheckSuite();
        var evaluationCalls = 0;
        var check = new CheckItem {Name = "broken-install", Title = "Broken install"}
            .WithEvaluation((_, _) =>
            {
                Interlocked.Increment(ref evaluationCalls);
                return Task.FromResult<bool?>(false);
            })
            .WithRemediation((_, _) => throw new InvalidOperationException("installer exploded"));
        suite.AddCheck(check);
        var viewModel = CreateViewModel(new TestConfigProvider(new YoloEaseApplicationConfig()), suite, temp);

        // When
        await viewModel.RemediateCheckAsync(check);

        // Then
        check.IsSatisfied.ShouldBe(false);
        check.LastError.ShouldNotBeNull();
        check.LastError.Value.Message.ShouldContain("installer exploded");
        check.LastOutput.ShouldContain("Error: installer exploded");
        evaluationCalls.ShouldBe(0);
        viewModel.OperationFailed.ShouldBeTrue();
    }

    /// <summary>
    /// WHAT: Verifies that bulk prerequisite installation preserves remediation failures for the failing row.
    /// HOW: Runs suite remediation with a throwing fix and confirms verification is skipped for that row.
    /// </summary>
    [Test]
    public async Task ShouldKeepInstallErrorVisibleAfterBulkPrerequisiteFixFailure()
    {
        // Given
        var suite = new CheckSuite();
        var evaluationCalls = 0;
        var progressMessages = new List<string>();
        var check = new CheckItem {Name = "broken-bulk-install", Title = "Broken bulk install"}
            .WithEvaluation((_, _) =>
            {
                Interlocked.Increment(ref evaluationCalls);
                return Task.FromResult<bool?>(false);
            })
            .WithRemediation((_, _) => throw new InvalidOperationException("bulk installer exploded"));
        suite.AddCheck(check);

        // When
        await suite.RemediateFailedAsync(x => progressMessages.Add(x.Message));

        // Then
        check.IsSatisfied.ShouldBe(false);
        check.LastError.ShouldNotBeNull();
        check.LastError.Value.Message.ShouldContain("bulk installer exploded");
        check.LastOutput.ShouldContain("Error: bulk installer exploded");
        evaluationCalls.ShouldBe(1);
        progressMessages.ShouldContain("Install failed: Broken bulk install");
        progressMessages.ShouldContain("Verification skipped: Broken bulk install");
    }

    /// <summary>
    /// WHAT: Verifies that a CPU-only torch install is missing when a compatible NVIDIA driver exists.
    /// HOW: Mocks detector output for an RTX GPU with driver 581.80 and CPU PyTorch.
    /// </summary>
    [Test]
    public async Task ShouldRequireCudaPytorchWhenCompatibleNvidiaHasCpuTorch()
    {
        // Given
        var detector = new Mock<IGpuRuntimeDetector>();
        detector
            .Setup(x => x.DetectAsync(It.IsAny<Action<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GpuRuntimeInfo
            {
                Adapters = new[]
                {
                    new GpuAdapterInfo
                    {
                        Name = "NVIDIA GeForce RTX 5070",
                        Vendor = GpuVendor.Nvidia,
                        DriverVersion = "581.80",
                        Source = "test"
                    }
                },
                PyTorch = new PytorchRuntimeInfo
                {
                    IsInstalled = true,
                    Version = "2.11.0+cpu"
                }
            });
        var installer = new PrerequisitesInstaller(
            Mock.Of<IPrerequisitesToolchain>(),
            Mock.Of<IPrerequisiteCommandRunner>(),
            detector.Object);
        var check = new CheckItem {Name = "pytorch", Title = "PyTorch"};

        // When
        var result = await installer.CheckPytorchRuntimeAsync(check, CancellationToken.None);

        // Then
        result.ShouldBe(false);
        check.LastOutput.ShouldContain("CPU-only");
    }

    /// <summary>
    /// WHAT: Verifies that CUDA PyTorch installation uses managed Python and the CUDA 13.0 wheel index.
    /// HOW: Mocks detector and command runner, then inspects the generated pip command.
    /// </summary>
    [Test]
    public async Task ShouldInstallCudaPytorchWithManagedPythonAndCu130Index()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var pythonPath = Path.Combine(temp.Path, "tools", "venv", "Scripts", "python.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(pythonPath)!);
        File.WriteAllText(pythonPath, string.Empty);

        var toolchain = new Mock<IPrerequisitesToolchain>(MockBehavior.Strict);
        toolchain
            .Setup(x => x.RequireVenvPythonExecutable())
            .Returns(new FileInfo(pythonPath));

        var detector = new Mock<IGpuRuntimeDetector>(MockBehavior.Strict);
        detector
            .Setup(x => x.DetectAsync(It.IsAny<Action<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GpuRuntimeInfo
            {
                Adapters = new[]
                {
                    new GpuAdapterInfo
                    {
                        Name = "NVIDIA GeForce RTX 5070",
                        Vendor = GpuVendor.Nvidia,
                        DriverVersion = "581.80",
                        Source = "test"
                    }
                }
            });

        Command capturedCommand = default!;
        var runner = new Mock<IPrerequisiteCommandRunner>(MockBehavior.Strict);
        runner
            .Setup(x => x.RunAsync(
                It.IsAny<Command>(),
                "pytorch-runtime-install",
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>>()))
            .Callback<Command, string, TimeSpan, CancellationToken, Action<string>>((command, _, _, _, _) => capturedCommand = command)
            .ReturnsAsync(new PrerequisiteCommandResult {ExitCode = 0});
        var installer = new PrerequisitesInstaller(toolchain.Object, runner.Object, detector.Object);

        // When
        await installer.InstallPytorchRuntimeAsync(new CheckItem {Name = "pytorch", Title = "PyTorch"}, CancellationToken.None);

        // Then
        var commandText = capturedCommand.ToString();
        commandText.ShouldContain(pythonPath);
        commandText.ShouldContain("--index-url");
        commandText.ShouldContain(GpuRuntimeDetector.CudaPyTorchIndexUrl);
        commandText.ShouldContain($"torch=={GpuRuntimeDetector.TorchVersion}");
        commandText.ShouldContain($"torchvision=={GpuRuntimeDetector.TorchVisionVersion}");
    }

    /// <summary>
    /// WHAT: Verifies that the prerequisite suite installs PyTorch before app packages and keeps driver help manual.
    /// HOW: Builds the suite and inspects row ordering, dependency links, and manual remediation flags.
    /// </summary>
    [Test]
    public void ShouldBuildPrerequisiteSuiteInRuntimeInstallOrder()
    {
        // Given
        var installer = new PrerequisitesInstaller(
            Mock.Of<IPrerequisitesToolchain>(),
            Mock.Of<IPrerequisiteCommandRunner>(),
            Mock.Of<IGpuRuntimeDetector>());

        // When
        var suite = new PrerequisitesSuiteFactory(installer).Create();

        // Then
        suite.Checks.Items.Select(x => x.Name).ToArray().ShouldBe(new[]
        {
            "python",
            "venv",
            "pip",
            "gpu-driver",
            "pytorch-runtime",
            "packages",
            "yolo",
            "gpu"
        });
        suite.Checks.Items.Single(x => x.Name == "packages").Dependencies.Single().Name.ShouldBe("pytorch-runtime");
        var gpuDriver = suite.Checks.Items.Single(x => x.Name == "gpu-driver");
        gpuDriver.IsRequired.ShouldBeFalse();
        gpuDriver.IncludeInBulkInstall.ShouldBeFalse();
        gpuDriver.RemediationLabel.ShouldBe("Open help");
    }

    /// <summary>
    /// WHAT: Verifies that the app package prerequisite explicitly checks ONNX Runtime.
    /// HOW: Captures the generated Python import command and inspects the package row output.
    /// </summary>
    [Test]
    public async Task ShouldVerifyOnnxRuntimeInPythonPackages()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var pythonPath = Path.Combine(temp.Path, "tools", "venv", "Scripts", "python.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(pythonPath)!);
        File.WriteAllText(pythonPath, string.Empty);

        var toolchain = new Mock<IPrerequisitesToolchain>(MockBehavior.Strict);
        toolchain
            .SetupGet(x => x.VenvPythonExecutable)
            .Returns(new FileInfo(pythonPath));

        Command capturedCommand = default!;
        var runner = new Mock<IPrerequisiteCommandRunner>(MockBehavior.Strict);
        runner
            .Setup(x => x.RunAsync(
                It.IsAny<Command>(),
                "python-packages-check",
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Action<string>>()))
            .Callback<Command, string, TimeSpan, CancellationToken, Action<string>>((command, _, _, _, _) => capturedCommand = command)
            .ReturnsAsync(new PrerequisiteCommandResult {ExitCode = 0});

        var installer = new PrerequisitesInstaller(
            toolchain.Object,
            runner.Object,
            Mock.Of<IGpuRuntimeDetector>());
        var check = new CheckItem {Name = "packages", Title = "Python packages"};

        // When
        var result = await installer.CheckPackagesAsync(check, CancellationToken.None);

        // Then
        result.ShouldBe(true);
        capturedCommand.ToString().ShouldContain("onnxruntime");
        capturedCommand.ToString().ShouldNotContain("cvat_sdk");
        check.LastOutput.ShouldContain("onnxruntime");
        check.LastOutput.ShouldNotContain("cvat_sdk");
    }

    /// <summary>
    /// WHAT: Verifies that the managed requirements file installs ONNX Runtime.
    /// HOW: Reads the requirements file used by the toolchain from the test output.
    /// </summary>
    [Test]
    public void ShouldInstallOnnxRuntimeFromManagedRequirements()
    {
        // Given
        using var temp = new TemporaryDirectory();
        var toolchain = CreateToolchain(temp);

        // When
        var requirements = File.ReadAllText(toolchain.RequirementsFile.FullName);

        // Then
        requirements.ShouldContain("onnxruntime");
        requirements.ShouldNotContain("cvat-cli");
        requirements.ShouldNotContain("cvat_sdk");
    }

    /// <summary>
    /// WHAT: Verifies that YOLO package updates use the managed virtual-environment Python.
    /// HOW: Mocks the toolchain to fail when that executable is requested and verifies the call.
    /// </summary>
    [Test]
    public async Task YoloWrapperShouldUseManagedPythonForPip()
    {
        // Given
        EnsureWrapperScripts();
        var toolchain = new Mock<IPrerequisitesToolchain>(MockBehavior.Strict);
        toolchain
            .Setup(x => x.RequireVenvPythonExecutable())
            .Throws(new PrerequisitesMissingException("managed python"));
        var wrapper = new Yolo8CliWrapper(toolchain.Object, Mock.Of<IGpuRuntimeDetector>());

        // When / Then
        await Should.ThrowAsync<PrerequisitesMissingException>(() => wrapper.UpdateYolo());

        toolchain.Verify(x => x.RequireVenvPythonExecutable(), Times.Once);
        toolchain.VerifyNoOtherCalls();
    }

    /// <summary>
    /// WHAT: Verifies that YOLO checks use the managed yolo executable.
    /// HOW: Mocks the toolchain to fail when the yolo executable is requested and verifies the call.
    /// </summary>
    [Test]
    public async Task YoloWrapperShouldUseManagedYoloExecutable()
    {
        // Given
        EnsureWrapperScripts();
        var toolchain = new Mock<IPrerequisitesToolchain>(MockBehavior.Strict);
        toolchain
            .Setup(x => x.RequireYoloExecutable())
            .Throws(new PrerequisitesMissingException("managed yolo"));
        var wrapper = new Yolo8CliWrapper(toolchain.Object, Mock.Of<IGpuRuntimeDetector>());

        // When / Then
        await Should.ThrowAsync<PrerequisitesMissingException>(() => wrapper.RunChecks());

        toolchain.Verify(x => x.RequireYoloExecutable(), Times.Once);
        toolchain.VerifyNoOtherCalls();
    }

    /// <summary>
    /// WHAT: Verifies that CVAT CLI operations use managed Python and cvat-cli executables.
    /// HOW: Provides managed Python but fails cvat-cli resolution, then verifies both toolchain requests.
    /// </summary>
    [Test]
    public async Task CvatWrapperShouldUseManagedExecutables()
    {
        // Given
        EnsureWrapperScripts();
        using var temp = new TemporaryDirectory();
        var toolchain = new Mock<IPrerequisitesToolchain>(MockBehavior.Strict);
        toolchain
            .Setup(x => x.RequireVenvPythonExecutable())
            .Returns(new FileInfo(Path.Combine(temp.Path, "tools", "venv", "Scripts", "python.exe")));
        toolchain
            .Setup(x => x.RequireCvatCliExecutable())
            .Throws(new PrerequisitesMissingException("managed cvat-cli"));
        var wrapper = new CvatCliWrapper(toolchain.Object);

        // When / Then
        await Should.ThrowAsync<PrerequisitesMissingException>(() => wrapper.EnsureInstalled());

        toolchain.Verify(x => x.RequireVenvPythonExecutable(), Times.Once);
        toolchain.Verify(x => x.RequireCvatCliExecutable(), Times.Once);
        toolchain.VerifyNoOtherCalls();
    }

    private static PrerequisitesViewModel CreateViewModel(
        TestConfigProvider configProvider,
        CheckSuite suite,
        TemporaryDirectory temp)
    {
        var toolchain = new Mock<IPrerequisitesToolchain>();
        toolchain
            .SetupGet(x => x.ToolsRoot)
            .Returns(new DirectoryInfo(Path.Combine(temp.Path, "tools")));
        return new PrerequisitesViewModel(configProvider, toolchain.Object, suite);
    }

    private static PrerequisitesToolchain CreateToolchain(TemporaryDirectory temp)
    {
        var appArguments = new Mock<IAppArguments>();
        appArguments.SetupGet(x => x.AppDataDirectory).Returns(temp.Path);
        return new PrerequisitesToolchain(appArguments.Object);
    }

    private static void EnsureWrapperScripts()
    {
        var scriptsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts");
        Directory.CreateDirectory(scriptsDirectory);
        File.WriteAllText(Path.Combine(scriptsDirectory, "ConvertCVATtoYolo8.py"), string.Empty);
        File.WriteAllText(Path.Combine(scriptsDirectory, "CVATWrapper.py"), string.Empty);
    }

    private static void CreatePythonArchive(FileInfo archive)
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "YoloEasePrerequisitesTests", Guid.NewGuid().ToString("N"));
        try
        {
            var pythonRoot = Path.Combine(sourceRoot, "python");
            Directory.CreateDirectory(Path.Combine(pythonRoot, "Lib"));
            File.WriteAllText(Path.Combine(pythonRoot, "python.exe"), "fake executable");
            File.WriteAllText(Path.Combine(pythonRoot, "Lib", "os.py"), "fake library");

            Directory.CreateDirectory(archive.Directory!.FullName);
            using var target = new FileStream(archive.FullName, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var gzip = new GZipStream(target, CompressionLevel.SmallestSize);
            TarFile.CreateFromDirectory(sourceRoot, gzip, includeBaseDirectory: false);
        }
        finally
        {
            if (Directory.Exists(sourceRoot))
            {
                Directory.Delete(sourceRoot, recursive: true);
            }
        }
    }

    private static string ComputeSha256(FileInfo file)
    {
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }

    private sealed class TestConfigProvider : IConfigProvider<YoloEaseApplicationConfig>, IDisposable
    {
        private readonly BehaviorSubject<YoloEaseApplicationConfig> whenChanged;

        public TestConfigProvider(YoloEaseApplicationConfig config)
        {
            ActualConfig = config;
            whenChanged = new BehaviorSubject<YoloEaseApplicationConfig>(config);
        }

        public YoloEaseApplicationConfig ActualConfig { get; private set; }

        public IObservable<YoloEaseApplicationConfig> WhenChanged => whenChanged;

        public void Save(YoloEaseApplicationConfig config)
        {
            ActualConfig = config;
            whenChanged.OnNext(config);
        }

        public IObservable<T> ListenTo<T>(Expression<Func<YoloEaseApplicationConfig, T>> fieldToMonitor)
        {
            var getter = fieldToMonitor.Compile();
            return whenChanged.Select(getter).StartWith(getter(ActualConfig));
        }

        public void Dispose()
        {
            whenChanged.Dispose();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "YoloEasePrerequisitesTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
