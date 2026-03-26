using PoeShared.Logging;

namespace YoloEase.UI;

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
