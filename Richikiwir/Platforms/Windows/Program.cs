using System;
using System.IO;
using System.Threading;

namespace Richikiwir.WinUI
{
    public static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var logPath = @"C:\Users\ThinkPad\source\repos\Richikiwir\Richikiwir\startup_debug.txt";
            try
            {
                File.WriteAllText(logPath, "Entering Program.Main...\n");

                AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
                {
                    try
                    {
                        File.AppendAllText(logPath, $"[FirstChance] {e.Exception.GetType().FullName}: {e.Exception.Message}\n{e.Exception.StackTrace}\n\n");
                    }
                    catch { }
                };

                WinRT.ComWrappersSupport.InitializeComWrappers();
                File.AppendAllText(logPath, "Initialized ComWrappers, starting WinUI Application...\n");

                Microsoft.UI.Xaml.Application.Start((p) =>
                {
                    try
                    {
                        File.AppendAllText(logPath, "Inside Application.Start callback...\n");
                        var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                        SynchronizationContext.SetSynchronizationContext(context);

                        new App();
                        File.AppendAllText(logPath, "App instantiated successfully.\n");
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(logPath, $"[Exception in App callback]: {ex}\n");
                        throw;
                    }
                });

                File.AppendAllText(logPath, "Application.Start completed normally.\n");
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(logPath, $"[Exception in Main]: {ex}\n");
                }
                catch { }
            }
        }
    }
}
