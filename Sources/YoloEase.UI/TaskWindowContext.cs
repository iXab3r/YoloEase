using YoloEase.UI.Core;

namespace YoloEase.UI;

/// <summary>
/// Carries the active project and task into a standalone task annotation window.
/// </summary>
public sealed record TaskWindowContext(YoloEaseProject Project, AnnotationTaskInfo Task);
