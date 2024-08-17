using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading;
using Humanizer;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TrainingTimeline;

public sealed record PredictArgs
{
    public required TrainedModelFileInfo Model { get; init; } 
    
    public required FileInfo[] Files { get; init; }
}

public class PredictTimelineEntry : RunnableTimelineEntry<DatasetPredictInfo>
{
    private readonly TrainedModelFileInfo trainedModelFileInfo;
    private readonly PredictArgs predictArgs;
    private readonly Yolo8PredictAccessor predictAccessor;

    public PredictTimelineEntry(
        PredictArgs predictArgs,
        Yolo8PredictAccessor predictAccessor)
    {
        this.predictArgs = predictArgs;
        this.predictAccessor = predictAccessor;
    }
    
    public DatasetPredictInfo DatasetPredictions { get; private set; }

    protected override async Task<DatasetPredictInfo> RunInternal(CancellationToken cancellationToken)
    {
        using var progressAnchor = Disposable.Create(() => ProgressPercent = null);
        var sw = Stopwatch.StartNew();
        ProgressPercent = 0;
        AppendTextLine("Starting prediction process");

        try
        {
            var predictionResults = await predictAccessor.Predict(predictArgs, 
                update =>
            {
                ProgressPercent = (int) update.ProgressPercentage;
                Text = $"Processed predictions for image {update.ImageCurrent}/{update.ImageMax}";
            }, cancellationToken: cancellationToken);
            var images = predictionResults.Predictions.Randomize().Take(5).ToArray();
            Images.AddRange(images.Select(x => x.File));
            DatasetPredictions = predictionResults;

            var labelsById = predictionResults.Predictions
                .Select(x => x.Labels)
                .SelectMany(x => x)
                .GroupBy(x => x.Label.Name)
                .Select(x => new { Id = x.Key, Count = x.Count(), AvgConfidence = x.Average(y => y.Score) })
                .ToArray();

            var text = new StringBuilder($"Prediction completed in {sw.Elapsed.Humanize(culture: CultureInfo.InvariantCulture)}\n");
            text.AppendLine($"Images: {predictionResults.Predictions.Length}, Labels: {labelsById.Select(x => x.Count).Sum()}");
            foreach (var label in labelsById)
            {
                text.AppendLine($"Id: {label.Id}, Count: {label.Count}, AvgConf: {label.AvgConfidence}");
            }
            Text = text.ToString();

            return predictionResults;
        }
        finally
        {
            ProgressPercent = null;
        }
    }
}