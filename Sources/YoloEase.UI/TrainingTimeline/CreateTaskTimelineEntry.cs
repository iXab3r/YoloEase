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
        IReadOnlyList<FileLabel> labeledFiles)
    {
        LabeledFiles = labeledFiles;
        this.cvatProjectAccessor = cvatProjectAccessor;
        this.annotationsAccessor = annotationsAccessor;
        this.trainingBatchAccessor = trainingBatchAccessor;
    }

    public TaskRead Task { get; private set; }

    public AnnotationsRead Annotations { get; private set; }

    public IReadOnlyList<FileLabel> LabeledFiles { get; init; }

    public bool AutoAnnotate { get; init; }

    public FileInfo[] TaskFiles { get; private set; }

    public CvatProjectAccessor CvatProjectAccessor => cvatProjectAccessor;

    protected override async Task<TaskRead> RunInternal(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await trainingBatchAccessor.Refresh();

        var files = AutoAnnotate && LabeledFiles != null && LabeledFiles.Any()
            ? await PickAnnotated()
            : await PickRandom();

        Text = $"Creating new task with {"file".ToQuantity(files.Length)}";
        var task = await trainingBatchAccessor.CreateNextTask();
        Text = $"Created new task #{task} with {"file".ToQuantity(files.Length)} in {sw.Elapsed.Humanize(culture: CultureInfo.InvariantCulture)}";

        if (AutoAnnotate && LabeledFiles != null)
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

        var filesToPick = new List<FileInfo>();
        var alreadyUsed = new HashSet<string>();

        var unannotatedByFileName = unannotatedFiles
            .ToDictionary(x => x.Name, x => x);
        
        foreach (var labeledFile in LabeledFiles)
        {
            if (!unannotatedByFileName.TryGetValue(labeledFile.File.Name, out var file))
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

    private async Task<AnnotationsRead> UploadAnnotations(
        FileInfo[] files,
        TaskRead task,
        Stopwatch sw)
    {
        var taskMetadata = await cvatProjectAccessor.RetrieveMetadata(task.Id.Value);

        var taskFramesByFileName = taskMetadata.Frames
            .EmptyIfNull()
            .Select((x, frameIdx) => new {Frame = x, FrameIdx = frameIdx})
            .ToDictionary(x => x.Frame.Name, StringComparer.OrdinalIgnoreCase);

        Text = $"Annotating the task #{task.Id}";
        var predictions = LabeledFiles
            .GroupBy(x => x.File)
            .Select(x => new PredictInfo() {File = x.Key, Labels = x.Select(y => y.Label).ToArray()})
            .ToDictionary(x => x.File.Name, x => x);

        var projectLabelsByName = cvatProjectAccessor.Labels.Items
            .ToDictionary(x => x.Name, x => x);

        var labels = files
            .Select(x => predictions.GetValueOrDefault(x.Name))
            .Where(x => x != null)
            .Where(x => x.Labels.Length != 0)
            .Select(x => x.Labels.Select(prediction =>
            {
                if (!taskFramesByFileName.TryGetValue(x.File.Name, out var taskFrame))
                {
                    throw new InvalidStateException($"Failed to resolve frame using name {x.File.Name}");
                }

                if (!projectLabelsByName.TryGetValue(prediction.Label.Name, out var cvatLabel))
                {
                    throw new InvalidStateException($"Failed to resolve CVAT label using Name {prediction.Label.Name}, known labels: {projectLabelsByName.DumpToString()}");
                }

                return new CvatRectangleAnnotation()
                {
                    BoundingBox = prediction.BoundingBox,
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