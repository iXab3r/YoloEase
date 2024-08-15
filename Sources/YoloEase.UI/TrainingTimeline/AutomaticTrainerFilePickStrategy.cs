namespace YoloEase.UI.TrainingTimeline;

public enum AutomaticTrainerFilePickStrategy
{
    Random,
    ActiveLearning
}


public enum AutomaticTrainerPredictionStrategy
{
    Unlabeled,
    AllFiles,
    Disabled,
}