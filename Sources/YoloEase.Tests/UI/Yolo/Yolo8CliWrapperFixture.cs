using Shouldly;
using YoloEase.UI.Yolo;

namespace YoloEase.Tests.UI.Yolo;

/// <summary>
/// Covers managed YOLO command-building decisions.
/// </summary>
public class Yolo8CliWrapperFixture
{
    /// <summary>
    /// WHAT: Verifies that training disables Ultralytics dataloader child processes by default.
    /// HOW: Resolves the managed training workers argument without any user override.
    /// </summary>
    [Test]
    public void ShouldUseSingleProcessTrainingWorkersByDefault()
    {
        // Given
        var additionalArguments = string.Empty;

        // When
        var workersArgument = Yolo8CliWrapper.ResolveDefaultTrainingWorkersArgument(additionalArguments);

        // Then
        workersArgument.ShouldBe("workers=0");
    }

    /// <summary>
    /// WHAT: Verifies that explicit user workers settings are preserved.
    /// HOW: Resolves the managed training workers argument when additional arguments already contain workers.
    /// </summary>
    [TestCase("workers=4")]
    [TestCase("epochs=1 workers=2")]
    [TestCase("--workers 1")]
    public void ShouldNotOverrideExplicitTrainingWorkers(string additionalArguments)
    {
        // Given / When
        var workersArgument = Yolo8CliWrapper.ResolveDefaultTrainingWorkersArgument(additionalArguments);

        // Then
        workersArgument.ShouldBeNull();
    }
}
