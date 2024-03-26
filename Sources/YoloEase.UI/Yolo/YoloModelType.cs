namespace YoloEase.UI.Yolo;

/// <summary>
/// Represents the different types of tasks that the Yolo model can perform.
/// </summary>
public enum YoloModelType
{
    /// <summary>
    /// Object Detection: Identifies objects in an image and their locations. Often used to recognize multiple objects and their boundaries.
    /// </summary>
    ObjectDetection,

    /// <summary>
    /// Segmentation: Divides an image into segments to simplify the representation of an image into something more meaningful and easier to analyze.
    /// </summary>
    Segmentation,

    /// <summary>
    /// Classification: Assigns a class label to the whole image or specific objects within it, based on the learned features from the training data.
    /// </summary>
    Classification,

    /// <summary>
    /// Pose Detection: Identifies the position and orientation of one or more individuals in an image, typically used to understand human body positions.
    /// </summary>
    PoseDetection
}