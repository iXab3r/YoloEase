using YoloEase.UI.Dto;

namespace YoloEase.UI.Yolo;

public readonly record struct YoloPredictionInfo
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
    /// The index of the class in the Yolo model that corresponds to this prediction.
    /// </summary>
    public int ClassIdx { get; init; }
}