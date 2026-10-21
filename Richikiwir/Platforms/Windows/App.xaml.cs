using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace Richikiwir.WinUI
{
    public partial class App : MauiWinUIApplication
    {
        public App()
        {
            var logPath = @"C:\Users\ThinkPad\source\repos\Richikiwir\Richikiwir\crash_log.txt";
            try { File.WriteAllText(logPath, "App starting...\n"); } catch { }

            AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            {
                try
                {
                    File.AppendAllText(logPath, $"[FirstChance] {e.Exception.GetType().FullName}: {e.Exception.Message}\n{e.Exception.StackTrace}\n\n");
                }
                catch { }
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    File.AppendAllText(logPath, $"[Unhandled] {e.ExceptionObject}\n\n");
                }
                catch { }
            };

            this.UnhandledException += (s, e) =>
            {
                try
                {
                    File.AppendAllText(logPath, $"[WinUI Unhandled] {e.Message}\n{e.Exception}\n\n");
                }
                catch { }
            };

            this.InitializeComponent();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
