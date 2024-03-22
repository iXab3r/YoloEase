using System.Threading;
using Humanizer;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TrainingTimeline;

public class CreateDatasetTimelineEntry : RunnableTimelineEntry<DatasetInfo>
{
    private readonly AnnotationsAccessor annotationsAccessor;

    public CreateDatasetTimelineEntry(AnnotationsAccessor annotationsAccessor)
    {
        this.annotationsAccessor = annotationsAccessor;
    }
    
    public DatasetInfo Dataset { get; private set; }

    protected override async Task<DatasetInfo> RunInternal(CancellationToken cancellationToken)
    {
        var missingAnnotations = annotationsAccessor.AnnotatedTasks.Count - annotationsAccessor.Annotations.Count;
        if (missingAnnotations > 0)
        {
            Text = $"Downloading {"missing annotation".ToQuantity(missingAnnotations)}";
            await annotationsAccessor.DownloadAnnotations();
        }

        Text = $"Building dataset out of {annotationsAccessor.Annotations.Count} annotations";
        var datasetInfo = await annotationsAccessor.CreateAnnotatedDataset();
        Text = $"Dataset: {"image".ToQuantity(datasetInfo.ImagesCount)} (train {datasetInfo.ImagesTrainingCount}/val {datasetInfo.ImagesValidationCount})";
        Dataset = datasetInfo;
        return datasetInfo;
    }
}