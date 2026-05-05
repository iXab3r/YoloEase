namespace YoloEase.UI.TrainingTimeline;

/// <summary>
/// Selects how prediction results influence automatic training steps.
/// </summary>
public enum AutomaticTrainerPredictionStrategy
{
    Unlabeled,
    AllFiles,
    Disabled,
}
