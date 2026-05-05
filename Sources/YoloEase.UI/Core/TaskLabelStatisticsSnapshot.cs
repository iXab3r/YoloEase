using System.Linq;
using YoloEase.UI.Dto;

namespace YoloEase.UI.Core;

internal sealed record TaskLabelStatisticsSnapshot
{
    public int TotalFiles { get; init; }

    public int TotalAnnotations { get; init; }

    public int FailedTaskCount { get; init; }

    public IReadOnlyList<TaskLabelStatistics> Tasks { get; init; } = Array.Empty<TaskLabelStatistics>();

    public IReadOnlyList<LabelUsageStatistics> Labels { get; init; } = Array.Empty<LabelUsageStatistics>();

    public static TaskLabelStatisticsSnapshot Create(
        IReadOnlyList<AnnotationTaskInfo> tasks,
        IReadOnlyList<TaskFileInfo> projectFiles,
        IReadOnlyList<AnnotationLabelInfo> projectLabels,
        IReadOnlyDictionary<int, IReadOnlyList<CvatRectangleAnnotation>> annotationsByTask,
        IReadOnlyDictionary<int, string>? taskErrors = null)
    {
        var filesByTask = projectFiles
            .Where(x => x.TaskId != null)
            .GroupBy(x => x.TaskId!.Value)
            .ToDictionary(x => x.Key, x => x.Count());

        var projectLabelsById = projectLabels.ToDictionary(x => x.Id);
        var taskStats = tasks
            .Select(task => CreateTaskStatistics(
                task,
                filesByTask.GetValueOrDefault(task.Id),
                projectLabelsById,
                annotationsByTask.GetValueOrDefault(task.Id) ?? Array.Empty<CvatRectangleAnnotation>(),
                taskErrors?.GetValueOrDefault(task.Id)))
            .ToArray();

        var totalFiles = projectFiles.Count;
        var projectLabelStats = AggregateLabels(
                projectLabelsById,
                totalFiles,
                taskStats.SelectMany(x => x.Labels))
            .ToArray();

        return new TaskLabelStatisticsSnapshot
        {
            TotalFiles = totalFiles,
            TotalAnnotations = taskStats.Sum(x => x.AnnotationCount),
            FailedTaskCount = taskStats.Count(x => !string.IsNullOrWhiteSpace(x.Error)),
            Tasks = taskStats,
            Labels = projectLabelStats,
        };
    }

    private static TaskLabelStatistics CreateTaskStatistics(
        AnnotationTaskInfo task,
        int fileCount,
        IReadOnlyDictionary<int, AnnotationLabelInfo> projectLabelsById,
        IReadOnlyList<CvatRectangleAnnotation> annotations,
        string? error)
    {
        var labelStats = annotations
            .GroupBy(x => x.LabelId)
            .Select(group => CreateLabelStatistics(
                projectLabelsById.GetValueOrDefault(group.Key),
                group.Key,
                fileCount,
                group))
            .OrderByDescending(x => x.AnnotationCount)
            .ThenBy(x => x.LabelName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new TaskLabelStatistics
        {
            TaskId = task.Id,
            FileCount = fileCount,
            AnnotatedFileCount = annotations.Select(x => x.FrameIndex).Distinct().Count(),
            AnnotationCount = annotations.Count,
            Error = error,
            Labels = labelStats,
        };
    }

    private static IEnumerable<LabelUsageStatistics> AggregateLabels(
        IReadOnlyDictionary<int, AnnotationLabelInfo> projectLabelsById,
        int totalFiles,
        IEnumerable<LabelUsageStatistics> taskLabels)
    {
        return taskLabels
            .GroupBy(x => x.LabelId)
            .Select(group =>
            {
                var label = projectLabelsById.GetValueOrDefault(group.Key);
                var annotationCount = group.Sum(x => x.AnnotationCount);
                var fileCount = group.Sum(x => x.FileCount);
                return new LabelUsageStatistics
                {
                    LabelId = group.Key,
                    LabelName = label?.Name ?? $"Label #{group.Key}",
                    Color = NormalizeLabelColor(label, group.Key),
                    AnnotationCount = annotationCount,
                    FileCount = fileCount,
                    FilePercent = CalculatePercent(fileCount, totalFiles),
                };
            })
            .OrderByDescending(x => x.AnnotationCount)
            .ThenBy(x => x.LabelName, StringComparer.OrdinalIgnoreCase);
    }

    private static LabelUsageStatistics CreateLabelStatistics(
        AnnotationLabelInfo? label,
        int labelId,
        int totalFiles,
        IEnumerable<CvatRectangleAnnotation> annotations)
    {
        var annotationArray = annotations.ToArray();
        var fileCount = annotationArray.Select(x => x.FrameIndex).Distinct().Count();
        return new LabelUsageStatistics
        {
            LabelId = labelId,
            LabelName = label?.Name ?? $"Label #{labelId}",
            Color = NormalizeLabelColor(label, labelId),
            AnnotationCount = annotationArray.Length,
            FileCount = fileCount,
            FilePercent = CalculatePercent(fileCount, totalFiles),
        };
    }

    private static double CalculatePercent(int value, int total)
    {
        return total <= 0 ? 0 : Math.Min(1, Math.Max(0, value / (double) total));
    }

    private static string NormalizeLabelColor(AnnotationLabelInfo? label, int labelId)
    {
        return string.IsNullOrWhiteSpace(label?.Color)
            ? AnnotationLabelPalette.PickByLabelId(labelId)
            : label.Color;
    }
}

internal sealed record TaskLabelStatistics
{
    public int TaskId { get; init; }

    public int FileCount { get; init; }

    public int AnnotatedFileCount { get; init; }

    public int AnnotationCount { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<LabelUsageStatistics> Labels { get; init; } = Array.Empty<LabelUsageStatistics>();
}

internal sealed record LabelUsageStatistics
{
    public int LabelId { get; init; }

    public string LabelName { get; init; } = string.Empty;

    public string Color { get; init; } = "#4f7cff";

    public int AnnotationCount { get; init; }

    public int FileCount { get; init; }

    public double FilePercent { get; init; }
}
