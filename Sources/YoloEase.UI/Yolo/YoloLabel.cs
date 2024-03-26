namespace YoloEase.UI.Yolo;

/// <summary>
/// Represents a single label as identified by the YOLO (You Only Look Once) object detection system.
/// This record structure encapsulates the unique attributes of a detected object's label,
/// including its identifier, name, kind, and associated color.
/// </summary>
public readonly record struct YoloLabel
{
    /// <summary>
    /// Gets the unique identifier for this label.
    /// The ID is an integer value that uniquely represents a specific class or type of object
    /// detected by the YOLO system.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the name of the label.
    /// This is a human-readable string that describes the class or type of the object detected,
    /// such as 'car', 'person', etc.
    /// </summary>
    public string Name { get; init; }
    
    /// <summary>
    /// Gets the color associated with this label.
    /// This color is typically used for visualization purposes, such as drawing bounding boxes
    /// or segmentation masks in the color corresponding to the detected object's class.
    /// </summary>
    public WinColor Color { get; init; }
}