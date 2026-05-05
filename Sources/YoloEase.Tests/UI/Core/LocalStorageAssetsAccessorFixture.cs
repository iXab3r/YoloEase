using DynamicData;
using Shouldly;
using YoloEase.UI.Core;

namespace YoloEase.Tests.UI.Core;

/// <summary>
/// Verifies local project asset synchronization against stale or missing file-system sources.
/// </summary>
public class LocalStorageAssetsAccessorFixture
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Test]
    public async Task ShouldSkipMissingDataSourceWithoutFailingRefresh()
    {
        using var temp = new TemporaryDirectory();
        var dataSources = new DataSourcesProvider();
        var missingDataSource = new DirectoryInfo(Path.Combine(temp.Path, "missing-source"));
        dataSources.InputDirectories.AddOrUpdate(missingDataSource);

        var instance = new LocalStorageAssetsAccessor(dataSources)
        {
            StorageDirectory = new DirectoryInfo(Path.Combine(temp.Path, "storage")),
        };

        await instance.Refresh().WaitAsync(Timeout);

        dataSources.InputDirectories.Items.ShouldContain(x => x.FullName == missingDataSource.FullName);
        instance.Files.Count.ShouldBe(0);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "YoloEaseTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (!Directory.Exists(Path))
            {
                return;
            }

            Directory.Delete(Path, recursive: true);
        }
    }
}
