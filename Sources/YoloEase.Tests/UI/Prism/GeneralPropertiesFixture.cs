using Shouldly;
using YoloEase.UI.Prism;

namespace YoloEase.Tests.UI.Prism;

/// <summary>
/// Covers persisted project/application configuration defaults.
/// </summary>
public class GeneralPropertiesFixture
{
    /// <summary>
    /// WHAT: Verifies that new projects default to the current small YOLO base model.
    /// HOW: Creates the default config record and checks the base model path.
    /// </summary>
    [Test]
    public void ShouldUseYolo11SmallAsDefaultBaseModel()
    {
        // Given / When
        var config = new GeneralProperties();

        // Then
        config.BaseModelPath.ShouldBe("yolo11s.pt");
    }
}
