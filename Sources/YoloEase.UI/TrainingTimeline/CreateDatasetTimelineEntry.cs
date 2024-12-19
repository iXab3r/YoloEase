using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using Humanizer;
using YoloEase.UI.Augmentations;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TrainingTimeline;

public class CreateDatasetTimelineEntry : RunnableTimelineEntry<DatasetInfo>
{
    private readonly AnnotationsAccessor annotationsAccessor;
    private readonly AugmentationsAccessor augmentationsAccessor;

    public CreateDatasetTimelineEntry(
        AnnotationsAccessor annotationsAccessor, 
        AugmentationsAccessor augmentationsAccessor)
    {
        this.annotationsAccessor = annotationsAccessor;
        this.augmentationsAccessor = augmentationsAccessor;
    }
    
    public DatasetInfo? Dataset { get; private set; }

    protected override async Task<DatasetInfo> RunInternal(CancellationToken cancellationToken)
    {
        using var progressTracker = new ComplexProgressTracker();
        using var progressTrackerUpdates = progressTracker
            .WhenAnyValue(x => x.ProgressPercent)
            .Subscribe(x =>
            {
                ProgressPercent = x;
            });
        using var progressAnchor = Disposable.Create(() => ProgressPercent = null);
        
        var missingAnnotations = annotationsAccessor.AnnotatedTasks.Count - annotationsAccessor.Annotations.Count;
        if (missingAnnotations > 0)
        {
            AppendTextLine($"Downloading {"missing annotation".ToQuantity(missingAnnotations)}");
            await annotationsAccessor.DownloadAnnotations();
        }
        
        var annotationFiles = annotationsAccessor.Annotations
            .Items
            .Select(x =>
            {
                if (string.IsNullOrEmpty(x.FilePath))
                {
                    return null;
                }

                return new FileInfo(x.FilePath);
            })
            .Where(x => x != null)
            .ToArray();

        FileInfo[] preparedAnnotations;
        
        if (augmentationsAccessor.EnabledEffects.Count > 0)
        {
            AppendTextLine($"Applying {"augmentation".ToQuantity(augmentationsAccessor.EnabledEffects.Count)} to {"annotation file".ToQuantity(annotationFiles.Count())}");

            preparedAnnotations = await augmentationsAccessor.PrepareAnnotationsWithAugmentations(annotationFiles, augmentationsAccessor.EnabledEffects.Items.ToArray(), progressTracker);
        }
        else
        {
            preparedAnnotations = annotationFiles;
        }

        AppendTextLine($"Building dataset out of {preparedAnnotations.Count()} annotation files");
        var datasetInfo = await annotationsAccessor.CreateAnnotatedDataset(preparedAnnotations);
        AppendTextLine($"Dataset: {"image".ToQuantity(datasetInfo.ImagesCount)} (train {datasetInfo.ImagesTrainingCount}/val {datasetInfo.ImagesValidationCount})");

        Dataset = datasetInfo;
        return datasetInfo;
    }
}