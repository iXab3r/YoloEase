using System.Threading;
using YoloDotNet.Enums;
using YoloDotNet.Models;
using YoloEngine = YoloDotNet.Yolo;

namespace YoloEase.UI.TaskAnnotation;

public interface IYoloEngineRepository : IHasErrorProvider
{
    IObservableCache<YoloEngineHandle, string> Engines { get; }

    Task<YoloEngineHandle> GetOrLoad(AutoAnnotationModelResolution resolution, CancellationToken cancellationToken = default);

    void Remove(string key);
}

public sealed class YoloEngineHandle : IDisposable
{
    private readonly YoloEngine yolo;

    public YoloEngineHandle(
        string key,
        AutoAnnotationModelResolution resolution,
        YoloEngine yolo)
    {
        Key = key;
        Resolution = resolution;
        this.yolo = yolo;
        ModelType = yolo.OnnxModel.ModelType;
        Labels = yolo.OnnxModel.Labels;
    }

    public string Key { get; }

    public AutoAnnotationModelResolution Resolution { get; }

    public ModelType ModelType { get; }

    public IReadOnlyList<LabelModel> Labels { get; }

    public YoloEngine Yolo => yolo;

    public void Dispose()
    {
        yolo.Dispose();
    }
}
