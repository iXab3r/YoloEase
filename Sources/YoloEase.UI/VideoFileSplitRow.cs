using System.Threading;
using AntDesign;
using PoeShared.Logging;
using PoeShared.Services;
using YoloEase.Cvat.Shared.Services;

namespace YoloEase.UI;

public sealed class VideoFileSplitRow : DisposableReactiveObject, IHasError
{
    private static readonly Binder<VideoFileSplitRow> Binder = new();
    private static readonly IFluentLog Log = typeof(VideoFileSplitRow).PrepareLogger();
    private static int FrameDivider = 60;

    private readonly SharedResourceLatch isBusyLatch = new SharedResourceLatch();

    static VideoFileSplitRow()
    {
        Binder.Bind(x => ((double) (x.EndFrameIdx - x.StartFrameIdx) / (x.FrameNth > 0 ? x.FrameNth : 1)))
            .To(x => x.ExpectedFrameCount);
        
        Binder.Bind(x => x.isBusyLatch.IsBusy).To(x => x.IsBusy);
        
        Binder.Bind(x => new SliderMark[] {new(0, "0"), new(x.FrameCount, $"{x.FrameCount}")})
            .To(x => x.Marks);

        Binder.Bind(x => new SliderMark[] {new(1, "1"), new(x.FrameCount / FrameDivider, $"{x.FrameCount / FrameDivider}")})
            .To(x => x.MarksFormFrameNth);
    }

    public VideoFileSplitRow(FileInfo fileInfo, IProgressReporter progressReporter)
    {
        FileInfo = fileInfo;
        ProgressReporter = progressReporter;

        Task.Run(FetchFrameInfo).AddTo(Anchors);
        Binder.Attach(this).AddTo(Anchors);
    }

    public FileInfo FileInfo { get; }
    
    public IProgressReporter ProgressReporter { get; }

    public long FrameCount { get; private set; }
        
    public bool IsBusy { get; private set; }
    
    public long StartFrameIdx { get; private set; }

    public long EndFrameIdx { get; private set; }
    
    public int FrameNth { get; set; } 
    
    public ErrorInfo? LastError { get; private set; }
    
    public SliderMark[] Marks { get; private set; }
    
    public SliderMark[] MarksFormFrameNth { get; private set; }
    
    public long ExpectedFrameCount { get; private set; }

    public async Task Run(CancellationToken cancellationToken)
    {
        using var isBusy = isBusyLatch.Rent();
        
        try
        {

            for (var i = 0; i <= 10; i++)
            {
                ProgressReporter.Update(i * 10);
                await Task.Delay(200);
            }

        }
        catch (Exception e)
        {
            Log.Warn("Failed to extract frames", e);
            LastError = new ErrorInfo()
            {
                Error = e,
                Message = e.Message,
            };
        }
    }
    
    private async Task FetchFrameInfo()
    {
        using var isBusy = isBusyLatch.Rent();
        
        try
        {
            FrameCount = VideoToFramesSplitter.GetFrameCount(FileInfo);
            StartFrameIdx = 0;
            EndFrameIdx = FrameCount;
        }
        catch (Exception e)
        {
            Log.Warn("Failed to fetch frames", e);
            LastError = new ErrorInfo()
            {
                Error = e,
                Message = e.Message,
            };
        }
    }

}