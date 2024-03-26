namespace YoloEase.UI.Yolo;

/// <summary>
/// Represents a comprehensive description of a YOLO (You Only Look Once) model.
/// This record provides a snapshot of the model's key attributes and settings,
/// including its size, dimensions, confidence threshold, overlap threshold, output types,
/// and a list of labels it can detect. It is used both for user information and internal
/// processing during the inference process.
/// </summary>
public sealed record YoloModelDescription
{
    /// <summary>
    /// Gets or sets the size of the model's input window.
    /// This typically represents the dimensions (width and height) to which input images
    /// are resized before being fed into the model.
    /// </summary>
    public WinSize Size { get; set; }

    /// <summary>
    /// Gets or sets the number of dimensions in the model's output.
    /// This typically refers to the dimensions of the feature map produced by the model.
    /// </summary>
    public int Dimensions { get; set; }
    
    /// <summary>
    /// Gets or sets the confidence threshold for detecting objects.
    /// Only objects detected with a confidence level higher than this threshold are considered valid.
    /// This value helps in filtering out less likely detections.
    /// Default is set to 0.20.
    /// </summary>
    public float Confidence { get; set; } = 0.20f;

    /// <summary>
    /// Gets or sets the overlap threshold for detecting objects.
    /// This value is used to handle overlapping bounding boxes and helps in reducing redundant detections.
    /// Default is set to 0.45.
    /// </summary>
    public float Overlap { get; set; } = 0.45f;

    /// <summary>
    /// Gets or sets the array of outputs produced by the model.
    /// These outputs typically include various parameters and metrics relevant to the model's detection process.
    /// </summary>
    public string[] Outputs { get; set; }

    /// <summary>
    /// Gets or sets the list of labels that the model is capable of detecting.
    /// Each label in this list represents a specific class or type of object that the model can identify.
    /// </summary>
    public IList<YoloLabel> Labels { get; set; } = new List<YoloLabel>();
}