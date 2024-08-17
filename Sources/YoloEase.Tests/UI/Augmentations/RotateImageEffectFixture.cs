using PoeShared.Tests.Helpers;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using YoloEase.UI.Augmentations;

namespace YoloEase.Tests.UI.Augmentations;

public class RotateImageEffectFixture
{
    [Test]
    [TestCaseSource(nameof(ShouldMutateBoundingBoxCases))]
    public void ShouldMutateBoundingBox(RotateMode rotateMode, Size imageSize, RectangleF initialBox, RectangleF expected)
    {
        //Given
        var instance = CreateInstance();
        instance.Rotation = rotateMode;

        //When
        var result = instance.Mutate(imageSize, initialBox);


        //Then
        result.ShouldBe(expected);
    }

 public static IEnumerable<NamedTestCaseData> ShouldMutateBoundingBoxCases()
{
    // Rotate 90 degrees
    yield return new NamedTestCaseData(RotateMode.Rotate90, new Size(10, 10), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "90 degrees - In-place" };
    yield return new NamedTestCaseData(RotateMode.Rotate90, new Size(20, 10), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "90 degrees - Landscape" };
    yield return new NamedTestCaseData(RotateMode.Rotate90, new Size(20, 20), new RectangleF(0, 0, 10, 10), new RectangleF(10, 0, 10, 10)) { TestName = "90 degrees - Square" };
    yield return new NamedTestCaseData(RotateMode.Rotate90, new Size(10, 20), new RectangleF(0, 0, 10, 10), new RectangleF(10, 0, 10, 10)) { TestName = "90 degrees - Portrait" };

    // Rotate 180 degrees
    yield return new NamedTestCaseData(RotateMode.Rotate180, new Size(10, 10), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "180 degrees - In-place" };
    yield return new NamedTestCaseData(RotateMode.Rotate180, new Size(20, 10), new RectangleF(0, 0, 10, 10), new RectangleF(10, 0, 10, 10)) { TestName = "180 degrees - Landscape" };
    yield return new NamedTestCaseData(RotateMode.Rotate180, new Size(20, 20), new RectangleF(0, 0, 10, 10), new RectangleF(10, 10, 10, 10)) { TestName = "180 degrees - Square" };
    yield return new NamedTestCaseData(RotateMode.Rotate180, new Size(10, 20), new RectangleF(0, 0, 10, 10), new RectangleF(0, 10, 10, 10)) { TestName = "180 degrees - Portrait" };

    // Rotate 270 degrees
    yield return new NamedTestCaseData(RotateMode.Rotate270, new Size(10, 10), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "270 degrees - In-place" };
    yield return new NamedTestCaseData(RotateMode.Rotate270, new Size(20, 10), new RectangleF(0, 0, 10, 10), new RectangleF(0, 10, 10, 10)) { TestName = "270 degrees - Landscape" };
    yield return new NamedTestCaseData(RotateMode.Rotate270, new Size(20, 20), new RectangleF(0, 0, 10, 10), new RectangleF(0, 10, 10, 10)) { TestName = "270 degrees - Square" };
    yield return new NamedTestCaseData(RotateMode.Rotate270, new Size(10, 20), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "270 degrees - Portrait" };

    // Rotate 0 degrees (no rotation)
    yield return new NamedTestCaseData(RotateMode.None, new Size(10, 10), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "0 degrees - No rotation" };
    yield return new NamedTestCaseData(RotateMode.None, new Size(20, 10), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "0 degrees - Landscape" };
    yield return new NamedTestCaseData(RotateMode.None, new Size(20, 20), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "0 degrees - Square" };
    yield return new NamedTestCaseData(RotateMode.None, new Size(10, 20), new RectangleF(0, 0, 10, 10), new RectangleF(0, 0, 10, 10)) { TestName = "0 degrees - Portrait" };
}

    private RotateImageEffect CreateInstance()
    {
        return new RotateImageEffect();
    }
}