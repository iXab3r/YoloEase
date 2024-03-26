using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using Humanizer;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TrainingTimeline;

public class PredictTimelineEntry : RunnableTimelineEntry<DatasetPredictInfo>
{
    private readonly TrainedModelFileInfo trainedModelFileInfo;
    private readonly Yolo8PredictAccessor predictAccessor;

    public PredictTimelineEntry(
        TrainedModelFileInfo trainedModelFileInfo, 
        DirectoryInfo inputDirectory,
        Yolo8PredictAccessor predictAccessor)
    {
        InputDirectory = inputDirectory;
        this.trainedModelFileInfo = trainedModelFileInfo;
        this.predictAccessor = predictAccessor;
    }
    
    public DirectoryInfo InputDirectory { get; }
    
    public DatasetPredictInfo DatasetPredictions { get; private set; }

    protected override async Task<DatasetPredictInfo> RunInternal(CancellationToken cancellationToken)
    {
        using var progressAnchor = Disposable.Create(() => ProgressPercent = null);
        var sw = Stopwatch.StartNew();
        ProgressPercent = 0;
        Text = "Starting prediction process";

        try
        {
            var predictionResults = await predictAccessor.Predict(trainedModelFileInfo,
                InputDirectory,
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
            
            Text = $"Prediction completed in {sw.Elapsed.Humanize(culture: CultureInfo.InvariantCulture)}, images: {predictionResults.Predictions.Length}, labels: {labelsById.Select(x => $"Id: {x.Id}, Count: {x.Count}, AvgConf: {x.AvgConfidence}").DumpToString()}";

            return predictionResults;
        }
        finally
        {
            ProgressPercent = null;
        }
    }
}