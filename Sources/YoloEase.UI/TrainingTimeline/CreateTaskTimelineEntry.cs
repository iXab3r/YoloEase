using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using CvatApi;
using Humanizer;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TrainingTimeline;

public class CreateTaskTimelineEntry : RunnableTimelineEntry<TaskRead>
{
    private readonly AnnotationsAccessor annotationsAccessor;
    private readonly TrainingBatchAccessor trainingBatchAccessor;
    private readonly CvatProjectAccessor cvatProjectAccessor;

    public CreateTaskTimelineEntry(
        CvatProjectAccessor cvatProjectAccessor,
        AnnotationsAccessor annotationsAccessor,
        TrainingBatchAccessor trainingBatchAccessor,
        DatasetPredictInfo datasetPredictions)
    {
        this.cvatProjectAccessor = cvatProjectAccessor;
        this.annotationsAccessor = annotationsAccessor;
        this.trainingBatchAccessor = trainingBatchAccessor;
        DatasetPredictions = datasetPredictions;
    }

    public DatasetPredictInfo DatasetPredictions { get; }

    public TaskRead Task { get; private set; }
    
    public AnnotationsRead Annotations { get; private set; }

    public FileInfo[] TaskFiles { get; private set; }

    public CvatProjectAccessor CvatProjectAccessor => cvatProjectAccessor;

    public bool AutoAnnotate { get; set; }

    public float AutoAnnotateConfidenceThreshold { get; set; }

    protected override async Task<TaskRead> RunInternal(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await trainingBatchAccessor.Refresh();

        var files = AutoAnnotate && DatasetPredictions != null
            ? await PickAnnotated()
            : await PickRandom();

        Text = $"Creating new task with {"file".ToQuantity(files.Length)}";
        var task = await trainingBatchAccessor.CreateNextTask();
        Text = $"Created new task #{task} with {"file".ToQuantity(files.Length)} in {sw.Elapsed.Humanize(culture: CultureInfo.InvariantCulture)}";

        if (AutoAnnotate && DatasetPredictions != null)
        {
            Annotations = await UploadAnnotations(files, task, sw);
        }

        TaskFiles = files;
        Task = task;
        return task;
    }

    private async Task<FileInfo[]> PickAnnotated()
    {
        var unannotatedFiles = trainingBatchAccessor.UnannotatedFiles.Items.ToArray();
        Text = $"Picking best files for the next batch";

        var predictions = this.DatasetPredictions.Predictions
            .Select(x => x with
            {
                Labels = x.Labels.EmptyIfNull().Where(y => y.Confidence >= AutoAnnotateConfidenceThreshold).ToArray()
            })
            .Where(x => x.Labels.Any())
            .ToDictionary(x => x.File.Name);

        var files = unannotatedFiles
            .Select(x =>
            {
                var predictionsForFile = predictions.GetValueOrDefault(x.Name);
                return new { File = x, Labels = predictionsForFile?.Labels ?? Array.Empty<YoloLabel>(), Score = predictionsForFile?.Labels.Max(y => y.Confidence)};
            })
            .OrderByDescending(x => x.Score)
            .ToArray();

        var batchFiles = files.Take(trainingBatchAccessor.BatchSize).ToArray();

        return await trainingBatchAccessor.PrepareNextBatchFiles(batchFiles.Select(x => x.File).ToArray());
    }

    private async Task<FileInfo[]> PickRandom()
    {
        var files = await trainingBatchAccessor.PrepareNextBatchFiles();
        return files;
    }

    private async Task<AnnotationsRead> UploadAnnotations(FileInfo[] files, TaskRead task, Stopwatch sw)
    {
        var taskMetadata = await cvatProjectAccessor.RetrieveMetadata(task.Id.Value);

        var taskFramesByFileName = taskMetadata.Frames
            .EmptyIfNull()
            .Select((x, frameIdx) => new {Frame = x, FrameIdx = frameIdx})
            .ToDictionary(x => x.Frame.Name, StringComparer.OrdinalIgnoreCase);

        Text = $"Annotating the task #{task.Id}";
        var predictions = this.DatasetPredictions.Predictions.ToDictionary(x => x.File.Name);

        var projectLabelsByName = cvatProjectAccessor.Labels.Items
            .ToDictionary(x => x.Name, x => x);
        
        var yoloLabelsById = projectLabelsByName
            .OrderBy(x => x.Value.Id)
            .Select(x => x.Value.Name)
            .Select((labelName, idx) => new {x = labelName, idx})
            .ToDictionary(x => x.idx, x => x.x);

        var labels = files
            .Select(x => predictions.GetValueOrDefault(x.Name))
            .Where(x => x != null)
            .Select(x => x with
            {
                Labels = x.Labels.EmptyIfNull().Where(y => y.Confidence >= AutoAnnotateConfidenceThreshold).ToArray()
            })
            .Where(x => x.Labels.Any())
            .Select(x => x.Labels.Select(label =>
            {
                if (!taskFramesByFileName.TryGetValue(x.File.Name, out var taskFrame))
                {
                    throw new InvalidStateException($"Failed to resolve frame using name {x.File.Name}");
                }

                if (!yoloLabelsById.TryGetValue(label.Id, out var labelName))
                {
                    throw new InvalidStateException($"Failed to resolve Yolo label using Id {label.Id}, known labels: {yoloLabelsById.DumpToString()}");
                }

                if (!projectLabelsByName.TryGetValue(labelName, out var cvatLabel))
                {
                    throw new InvalidStateException($"Failed to resolve CVAT label using Name {labelName}, known labels: {projectLabelsByName.DumpToString()}");
                }

                return new CvatRectangleAnnotation()
                {
                    BoundingBox = label.BoundingBox,
                    LabelId = cvatLabel.Id.Value,
                    FrameIndex = taskFrame.FrameIdx
                };
            }))
            .SelectMany(x => x)
            .ToArray();

        Text = $"Uploading {"label".ToQuantity(labels.Count())} for {"frame".ToQuantity(taskFramesByFileName.Count)}";
        var annotations = await annotationsAccessor.UploadAnnotations(task.Id.Value, labels);
        Text = $"Created task #{task.Id} with {"frame".ToQuantity(taskFramesByFileName.Count)} and {labels.Length} labels in {sw.Elapsed.Humanize(culture: CultureInfo.InvariantCulture)}";
        return annotations;
    }
}