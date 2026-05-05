using System.Collections.Concurrent;
using System.Linq;
using System.Reactive;
using System.Threading;

namespace YoloEase.UI.Core;

/// <summary>
/// Tracks local input directories that supply images or videos for annotation tasks.
/// </summary>
public class DataSourcesProvider : RefreshableReactiveObject
{
    private readonly SourceCacheEx<DirectoryInfo, string> inputDirectoriesSource = new(x => x.FullName);

    public DataSourcesProvider()
    {
    }

    public ISourceCacheEx<DirectoryInfo, string> InputDirectories => inputDirectoriesSource;

    protected override async Task RefreshInternal(IProgressReporter? progressReporter = default)
    {
    }
}
