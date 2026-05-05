using System.Threading;
using AntDesign;
using PoeShared.Logging;
using PoeShared.Services;
using YoloEase.UI.Services;

namespace YoloEase.UI;

/// <summary>
/// Tracks the UI state and progress for splitting one video into annotation frames.
/// </summary>
public sealed class VideoFileSplitRow : DisposableReactiveObject, IHasError
{
    private static readonly Binder<VideoFileSplitRow> Binder = new();
    private static readonly IFluentLog Log = typeof(VideoFileSplitRow).PrepareLogger();
    private static int FrameDivider = 60;

    private readonly SharedResourceLatch isBusyLatch = new SharedResourceLatch();
    private readonly IVideoFrameExtractor videoFrameExtractor;

    static VideoFileSplitRow()
    {
        Binder.Bind(x => (long) VideoFrameSelection.GetExpectedFrameCount(x.FrameCount, x.StartFrameIdx, x.EndFrameIdx, x.FrameNth))
            .To(x => x.ExpectedFrameCount);
        
        Binder.Bind(x => x.isBusyLatch.IsBusy).To(x => x.IsBusy);
        
        Binder.Bind(x => new SliderMark[] {new(0, "0"), new(ToSliderValue(Math.Max(0, x.FrameCount - 1)), $"{Math.Max(0, x.FrameCount - 1)}")})
            .To(x => x.Marks);

        Binder.Bind(x => new SliderMark[] {new(1, "1"), new(ToSliderValue(Math.Max(1, x.FrameCount / FrameDivider)), $"{Math.Max(1, x.FrameCount / FrameDivider)}")})
            .To(x => x.MarksFormFrameNth);
    }

    private static int ToSliderValue(long value)
    {
        return checked((int) Math.Min(int.MaxValue, Math.Max(0, value)));
    }

    public VideoFileSplitRow(FileInfo fileInfo, IProgressReporter progressReporter, IVideoFrameExtractor videoFrameExtractor)
    {
        FileInfo = fileInfo;
        ProgressReporter = progressReporter;
        this.videoFrameExtractor = videoFrameExtractor;

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
            var probe = await videoFrameExtractor.ProbeAsync(FileInfo);
            FrameCount = probe.FrameCount;
            StartFrameIdx = 0;
            EndFrameIdx = Math.Max(0, FrameCount - 1);
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
