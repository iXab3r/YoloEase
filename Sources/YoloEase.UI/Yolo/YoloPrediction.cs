using YoloEase.UI.Dto;

namespace YoloEase.UI.Yolo;

public readonly record struct YoloPrediction
{
    /// <summary>
    /// The confidence score of the prediction, indicating the likelihood that the prediction is correct.
    /// </summary>
    public required float Score { get; init; }

    /// <summary>
    /// The bounding rectangle of the detected object in the image.
    /// </summary>
    public required RectangleD BoundingBox { get; init; }

    /// <summary>
    /// The label of the detected object, providing information about the type or class of the object.
    /// </summary>
    public YoloLabel Label { get; init; }
}