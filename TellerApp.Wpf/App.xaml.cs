using System;
using System.IO;
using System.Windows;
using Serilog;

namespace TellerApp.Wpf
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
                .WriteTo.File(Path.Combine(logs, "teller-.log"),
                              rollingInterval: RollingInterval.Day,
                              retainedFileCountLimit: 7,
                              shared: true)
                .CreateLogger();

            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
                Log.Fatal(ex.ExceptionObject as Exception, "UnhandledException");
            this.DispatcherUnhandledException += (_, ex) =>
            {
                Log.Error(ex.Exception, "DispatcherUnhandledException");
                ex.Handled = true;
            };

            Log.Information("TellerApp starting");
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("TellerApp exiting");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}