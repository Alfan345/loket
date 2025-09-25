using System;
using System.IO;
using System.Windows;
using Serilog;
using System.Threading;
using System.Threading.Tasks;

namespace DisplayApp.Wpf
{
    public partial class App : Application
    {
        private Mutex? _single;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Single instance
            bool created;
            _single = new Mutex(true, "QueueSystem.DisplayApp.SingleInstance", out created);
            if (!created)
            {
                // instance lain sudah jalan, keluar
                Shutdown();
                return;
            }

            // Logs ke LocalAppData
            var logs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QueueSystem", "Display", "Logs");
            Directory.CreateDirectory(logs);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(Path.Combine(logs, "display-.log"),
                              rollingInterval: RollingInterval.Day,
                              retainedFileCountLimit: 7,
                              shared: true)
                .CreateLogger();

            // Global exception logging
            this.DispatcherUnhandledException += (s, exArgs) =>
            {
                Log.Fatal(exArgs.Exception, "DispatcherUnhandledException");
                exArgs.Handled = true;
                MessageBox.Show("Aplikasi Display mengalami error. Detail dicatat di log.", "Display", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            };
            AppDomain.CurrentDomain.UnhandledException += (s, exArgs) =>
            {
                Log.Fatal(exArgs.ExceptionObject as Exception, "AppDomain.UnhandledException");
            };
            TaskScheduler.UnobservedTaskException += (s, exArgs) =>
            {
                Log.Fatal(exArgs.Exception, "TaskScheduler.UnobservedTaskException");
                exArgs.SetObserved();
            };

            Log.Information("DisplayApp starting");
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("DisplayApp exiting");
            Log.CloseAndFlush();
            _single?.ReleaseMutex();
            base.OnExit(e);
        }
    }
}