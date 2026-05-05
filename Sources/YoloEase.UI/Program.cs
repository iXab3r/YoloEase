using PoeShared.Logging;

namespace YoloEase.UI;

/// <summary>
/// Hosts the desktop application entry point and top-level exception logging.
/// </summary>
public static class Program
{
    private static readonly IFluentLog Log = typeof(Program).PrepareLogger();

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var bootstrapper = new ProgramBootstrapper();
            bootstrapper.RunAsApp();
        }
        catch (Exception e)
        {
            Log.Error("Program encountered exception", e);
            throw;
        }
    }
}
