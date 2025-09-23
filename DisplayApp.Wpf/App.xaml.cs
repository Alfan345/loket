using System;
using System.IO;
using System.Windows;
using Serilog;
using LibVLCSharp.Shared;

namespace DisplayApp.Wpf
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            var logs = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(logs);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(Path.Combine(logs, "display-.log"),
                              rollingInterval: RollingInterval.Day,
                              retainedFileCountLimit: 7,
                              shared: true)
                .CreateLogger();

            Log.Information("DisplayApp starting");
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("DisplayApp exiting");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}