using System;
using System.IO;
using System.Windows;
using Serilog;

namespace AdminApp.Wpf
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
                    "QueueSystem", "Admin", "Logs");
                Directory.CreateDirectory(logs);
            }
            catch
            {
                logs = Path.Combine(Path.GetTempPath(), "QueueSystem_Admin_Logs");
                Directory.CreateDirectory(logs);
            }

            try
            {
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .Enrich.FromLogContext()
                    .WriteTo.File(Path.Combine(logs, "admin-.log"),
                                  rollingInterval: RollingInterval.Day,
                                  retainedFileCountLimit: 7,
                                  shared: true)
                    .CreateLogger();
                Log.Information("AdminApp logging to {Path}", logs);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal inisialisasi logging.\n{ex.Message}", "AdminApp", MessageBoxButton.OK, MessageBoxImage.Warning);
                Log.Logger = new LoggerConfiguration().CreateLogger(); // minimal
            }
                
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

            Log.Information("AdminApp starting");
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("AdminApp exiting");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}