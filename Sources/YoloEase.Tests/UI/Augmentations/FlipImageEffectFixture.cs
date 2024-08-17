using PoeShared.Tests.Helpers;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using YoloEase.UI.Augmentations;

namespace YoloEase.Tests.UI.Augmentations;

public class FlipImageEffectFixture
{
    [Test]
    [TestCaseSource(nameof(ShouldMutateBoundingBoxCases))]
    public void ShouldMutateBoundingBox(FlipMode flipMode, Size imageSize, RectangleF initialBox, RectangleF expected)
    {
        //Given
        var instance = CreateInstance();
        instance.FlipMode = flipMode;

        //When
        var result = instance.Mutate(imageSize, initialBox);


        //Then
        result.ShouldBe(expected);
    }

 public static IEnumerable<NamedTestCaseData> ShouldMutateBoundingBoxCases()
{
    // Flip horizontal
    yield return new NamedTestCaseData(FlipMode.Horizontal, new Size(10, 10), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "Horizontal - In-place" };
    yield return new NamedTestCaseData(FlipMode.Horizontal, new Size(20, 10), new RectangleF(0, 0, 10, 10), new RectangleF(10, 0, 10, 10)) { TestName = "Horizontal - Landscape" };
    yield return new NamedTestCaseData(FlipMode.Horizontal, new Size(20, 20), new RectangleF(0, 0, 10, 10), new RectangleF(10, 0, 10, 10)) { TestName = "Horizontal - Square" };
    yield return new NamedTestCaseData(FlipMode.Horizontal, new Size(10, 20), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "Horizontal - Portrait" };

    // Flip vertical
    yield return new NamedTestCaseData(FlipMode.Vertical, new Size(10, 10), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "Vertical - In-place" };
    yield return new NamedTestCaseData(FlipMode.Vertical, new Size(20, 10), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "Vertical - Landscape" };
    yield return new NamedTestCaseData(FlipMode.Vertical, new Size(20, 20), new RectangleF(0, 0, 10, 10), new RectangleF(0, 10, 10, 10)) { TestName = "Vertical - Square" };
    yield return new NamedTestCaseData(FlipMode.Vertical, new Size(10, 20), new RectangleF(0, 0, 10, 10), new RectangleF(0, 10, 10, 10)) { TestName = "Vertical - Portrait" };

    // No flip
    yield return new NamedTestCaseData(FlipMode.None, new Size(10, 10), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "None - No rotation" };
    yield return new NamedTestCaseData(FlipMode.None, new Size(20, 10), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "None - Landscape" };
    yield return new NamedTestCaseData(FlipMode.None, new Size(20, 20), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "None - Square" };
    yield return new NamedTestCaseData(FlipMode.None, new Size(10, 20), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "None - Portrait" };
}

    private FlipImageEffect CreateInstance()
    {
        return new FlipImageEffect();
    }
}