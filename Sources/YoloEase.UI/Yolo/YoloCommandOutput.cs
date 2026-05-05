namespace YoloEase.UI.Yolo;

public enum YoloCommandOutputKind
{
    Info,
    Output,
    Error
}

public sealed record YoloCommandOutput
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public YoloCommandOutputKind Kind { get; init; }

    public string Text { get; init; } = string.Empty;

    public static YoloCommandOutput Info(string text)
    {
        return new YoloCommandOutput
        {
            Kind = YoloCommandOutputKind.Info,
            Text = text
        };
    }

    public static YoloCommandOutput Output(string text)
    {
        return new YoloCommandOutput
        {
            Kind = YoloCommandOutputKind.Output,
            Text = text
        };
    }

    public static YoloCommandOutput Error(string text)
    {
        return new YoloCommandOutput
        {
            Kind = YoloCommandOutputKind.Error,
            Text = text
        };
    }
}
