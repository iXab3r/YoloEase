using System.Reactive.Disposables;
using PoeShared.Logging;
using PropertyChanged;

namespace YoloEase.UI.Augmentations;

public abstract class YoloObjectBase : DisposableReactiveObjectWithLogger, IYoloObject
{
    private static readonly Binder<YoloObjectBase> Binder = new();

    static YoloObjectBase()
    {
    }

    protected YoloObjectBase()
    {
        Log.Debug("New object is being created");
        
        Disposable.Create(() =>
        {
            if (Log.IsDebugEnabled)
            {
                Log.Debug($"Disposed");
            }
        }).AddTo(Anchors);

        Binder.Attach(this).AddTo(Anchors);
    }

    public IPoeEyeConfigVersioned Properties
    {
        get => SavePropertiesInternal(Log);
        set => LoadPropertiesInternal(Log, value);
    }

    private IPoeEyeConfigVersioned SavePropertiesInternal(IFluentLog log)
    {
        log.Debug("Saving properties");

        var result = SaveProperties();

        log.Debug("Saved properties");
        return result;
    }

    private void LoadPropertiesInternal(IFluentLog log, IPoeEyeConfigVersioned properties)
    {
        log.Debug("Loading properties");

        if (properties == null)
        {
            throw new ArgumentNullException(nameof(properties), $"Properties must be specified for object {GetType()}");
        }

        LoadProperties(properties);

        log.Debug("Loaded properties");
    }

    protected abstract void LoadProperties(IPoeEyeConfigVersioned source);

    protected abstract IPoeEyeConfigVersioned SaveProperties();
}

public abstract class YoloObjectBase<T> : YoloObjectBase where T : IPoeEyeConfigVersioned, new()
{
    [DoNotNotify]
    public new T Properties
    {
        get => (T) base.Properties;
        set => base.Properties = value;
    }

    protected abstract void VisitSave(T target);

    protected abstract void VisitLoad(T source);

    protected override IPoeEyeConfigVersioned SaveProperties()
    {
        var result = new T();
        VisitSave(result);
        return result;
    }

    protected override void LoadProperties(IPoeEyeConfigVersioned source)
    {
        if (!(source is T typedSource))
        {
            throw new ArgumentException(
                $"Invalid Properties source, expected value of type {typeof(T)}, got {(source == null ? "null" : source.GetType().FullName)} instead");
        }

        VisitLoad(typedSource);
    }
}