using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Humanizer;
using YoloEase.UI.Core;
using YoloEase.UI.Dto;

namespace YoloEase.UI.TrainingTimeline;

/// <summary>
/// Timeline step that creates a new annotation task from local project assets.
/// </summary>
public class CreateTaskTimelineEntry : RunnableTimelineEntry<AnnotationTaskInfo>
{
    private readonly AnnotationsAccessor annotationsAccessor;
    private readonly TrainingBatchAccessor trainingBatchAccessor;
    private readonly AnnotationProjectAccessor annotationProjectAccessor;

    public CreateTaskTimelineEntry(
        AnnotationProjectAccessor annotationProjectAccessor,
        AnnotationsAccessor annotationsAccessor,
        TrainingBatchAccessor trainingBatchAccessor,
        IReadOnlyList<FileLabel> labeledFiles)
    {
        LabeledFiles = labeledFiles;
        this.annotationProjectAccessor = annotationProjectAccessor;
        this.annotationsAccessor = annotationsAccessor;
        this.trainingBatchAccessor = trainingBatchAccessor;
    }

    public AnnotationTaskInfo? Task { get; private set; }

    public AnnotationUpdateResult? Annotations { get; private set; }

    public IReadOnlyList<FileLabel> LabeledFiles { get; init; }

    public bool AutoAnnotate { get; init; }

    public FileInfo[] TaskFiles { get; private set; }

    public AnnotationProjectAccessor ProjectAccessor => annotationProjectAccessor;

    protected override async Task<AnnotationTaskInfo> RunInternal(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await trainingBatchAccessor.Refresh();

        var files = AutoAnnotate && LabeledFiles != null && LabeledFiles.Any()
            ? await PickAnnotated()
            : await PickRandom();

        AppendTextLine($"Creating new task with {"file".ToQuantity(files.Length)}");
        var task = await trainingBatchAccessor.CreateNextTask();
        AppendTextLine($"Created new task #{task} with {"file".ToQuantity(files.Length)} in {sw.Elapsed.Humanize(culture: CultureInfo.InvariantCulture)}");

        if (AutoAnnotate && LabeledFiles != null)
        {
            Annotations = await UploadAnnotations(files, task, sw);
        }

        await trainingBatchAccessor.Project.NavigateToTask(task.Id);

        TaskFiles = files;
        Task = task;
        return task;
    }

    private async Task<FileInfo[]> PickAnnotated()
    {
        var unannotatedFiles = trainingBatchAccessor.UnannotatedFiles.Items.ToArray();
        AppendTextLine($"Picking best files for the next batch");

        var filesToPick = new List<FileInfo>();
        var alreadyUsed = new HashSet<string>();

        var unannotatedByFileName = unannotatedFiles
            .ToDictionary(x => Path.GetFileNameWithoutExtension(x.Name), x => x);
        
        foreach (var labeledFile in LabeledFiles)
        {
            if (!unannotatedByFileName.TryGetValue(Path.GetFileNameWithoutExtension(labeledFile.File.Name), out var file))
            {
                //file is not in unannotated files list - already completed?
                continue;
            }
            
            if (alreadyUsed.Contains(file.FullName))
            {
                continue;
            }

            filesToPick.Add(file);
            alreadyUsed.Add(file.FullName);
        }

        foreach (var file in unannotatedFiles.Randomize())
        {
            if (alreadyUsed.Contains(file.FullName))
            {
                continue;
            }

            filesToPick.Add(file);
            alreadyUsed.Add(file.FullName);
        }

        var batchFiles = filesToPick.Take(trainingBatchAccessor.BatchSize).ToArray();
        return await trainingBatchAccessor.PrepareNextBatchFiles(batchFiles);
    }

    private async Task<FileInfo[]> PickRandom()
    {
        var files = await trainingBatchAccessor.PrepareNextBatchFiles();
        return files.Randomize().ToArray();
    }

    private async Task<AnnotationUpdateResult> UploadAnnotations(
        FileInfo[] files,
        AnnotationTaskInfo task,
        Stopwatch sw)
    {
        var taskMetadata = await annotationProjectAccessor.RetrieveMetadata(task.Id);

        var taskFramesByFileName = taskMetadata.Frames
            .ToDictionary(x => Path.GetFileNameWithoutExtension(x.Name), x => x.Index, StringComparer.OrdinalIgnoreCase);

        AppendTextLine($"Annotating the task #{task.Id}");
        var predictions = LabeledFiles
            .GroupBy(x => x.File)
            .Select(x => new PredictInfo() {File = x.Key, Labels = x.Select(y => y.Label).ToArray()})
            .ToDictionary(x => Path.GetFileNameWithoutExtension(x.File.Name), x => x);

        var projectLabelsByName = annotationProjectAccessor.Labels.Items
            .ToDictionary(x => x.Name, x => x);

        var labels = files
            .Select(x => predictions.GetValueOrDefault(Path.GetFileNameWithoutExtension(x.Name)))
            .Where(x => x != null)
            .Where(x => x.Labels.Length != 0)
            .Select(x => x.Labels.Select(prediction =>
            {
                var fileName = Path.GetFileNameWithoutExtension(x.File.Name);
                if (!taskFramesByFileName.TryGetValue(fileName, out var taskFrameIndex))
                {
                    throw new InvalidStateException($"Failed to resolve frame using name {x.File.Name}");
                }

                if (!projectLabelsByName.TryGetValue(prediction.Label.Name, out var cvatLabel))
                {
                    throw new InvalidStateException($"Failed to resolve annotation label using Name {prediction.Label.Name}, known labels: {projectLabelsByName.DumpToString()}");
                }

                return new CvatRectangleAnnotation()
                {
                    BoundingBox = prediction.BoundingBox,
                    LabelId = cvatLabel.Id,
                    FrameIndex = taskFrameIndex,
                    Source = "automatic",
                };
            }))
            .SelectMany(x => x)
            .ToArray();

        AppendTextLine( $"Uploading {"label".ToQuantity(labels.Count())} for {"frame".ToQuantity(taskFramesByFileName.Count)}");
        var annotations = await annotationsAccessor.UploadAnnotations(task.Id, labels);
        AppendTextLine( $"Created task #{task.Id} with {"frame".ToQuantity(taskFramesByFileName.Count)} and {labels.Length} labels in {sw.Elapsed.Humanize(culture: CultureInfo.InvariantCulture)}");
        return annotations;
    }
}
