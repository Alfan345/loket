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
            string logs;
            try
            {
                logs = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "QueueSystem", "Teller", "Logs");
                Directory.CreateDirectory(logs);
            }
            catch
            {
                logs = Path.Combine(Path.GetTempPath(), "QueueSystem_Teller_Logs");
                Directory.CreateDirectory(logs);
            }

            try
            {
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .Enrich.FromLogContext()
                    .WriteTo.File(Path.Combine(logs, "teller-.log"),
                                  rollingInterval: RollingInterval.Day,
                                  retainedFileCountLimit: 7,
                                  shared: true)
                    .CreateLogger();
                Log.Information("TellerApp logging to {Path}", logs);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal inisialisasi logging.\n{ex.Message}", "TellerApp", MessageBoxButton.OK, MessageBoxImage.Warning);
                Log.Logger = new LoggerConfiguration().CreateLogger();
            }

            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
                Log.Fatal(ex.ExceptionObject as Exception, "UnhandledException");
            this.DispatcherUnhandledException += (_, ex) =>
            {
                Log.Error(ex.Exception, "DispatcherUnhandledException");
                ex.Handled = true;
            };

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