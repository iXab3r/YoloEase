using System.Linq;
using YoloEase.UI.Dto;
using YoloEase.UI.Yolo;

namespace YoloEase.UI.TrainingTimeline;

public sealed class PredictLabelItem : DisposableReactiveObject
{
    private static readonly Binder<PredictLabelItem> Binder = new();

    static PredictLabelItem()
    {
    }

    public PredictLabelItem(FileLabel[] labels)
    {
        if (labels.Length <= 0)
        {
            throw new ArgumentException($"No labels specified");
        }
        IsEnabled = true;
        var allLabels = labels
            .Select(x => x.Label.Label)
            .Distinct()
            .ToArray();
       
        if (allLabels.Length > 1)
        {
            throw new ArgumentException($"Multiple labels detected: {allLabels.DumpToString()}");
        }

        Label = allLabels.Single();

        UpdateLabels(labels);
        
        Binder.Attach(this).AddTo(Anchors);
    }

    public YoloLabel Label { get; }

    public bool IsEnabled { get; set; }
    
    public float ScoreThreshold { get; set; }
    
    public FileLabel[] Labels { get; private set; }
    
    public float ScoreMin { get; private set; }
    
    public float ScoreAvg { get; private set; }
    
    public float ScoreMax { get; private set; }

    public void UpdateLabels(FileLabel[] labels)
    {
        Labels = labels;
        
        ScoreMin = labels.Any() ? labels.Select(x => x.Label.Score).Min() : 0;
        ScoreAvg = labels.Any() ? labels.Select(x => x.Label.Score).Average() : 0;
        ScoreMax = labels.Any() ? labels.Select(x => x.Label.Score).Max() : 0;
    }

    public IEnumerable<FileLabel> EnumerateLabels(float scoreThreshold)
    {
        if (!IsEnabled)
        {
            return Array.Empty<FileLabel>();
        }

        return Labels.Where(x => x.Label.Score >= scoreThreshold);
    }
    
    public static PredictLabelItem FromPredicts(int labelId, PredictInfo[] labels)
    {
        var filtered = labels
            .SelectMany(x => x.Labels.Where(y => y.Label.Id == labelId).Select(y => new FileLabel(x.File, y)))
            .ToArray();

        return new PredictLabelItem(filtered);
    }
}