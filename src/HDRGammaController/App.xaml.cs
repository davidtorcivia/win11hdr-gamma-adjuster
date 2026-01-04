using System;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;

namespace HDRGammaController
{
    public partial class App : Application
    {

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                Console.WriteLine("App.OnStartup: Starting...");
                base.OnStartup(e);

                // Create MainWindow (Settings) but valid properties
                Console.WriteLine("App.OnStartup: Creating MainWindow...");
                var mainWindow = new MainWindow();
                Console.WriteLine("App.OnStartup: MainWindow created.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("CRITICAL STARTUP ERROR: " + ex);
                System.IO.File.WriteAllText("startup_log.txt", ex.ToString());
                // MessageBox.Show("Startup Error: " + ex.Message);
                Shutdown(-1);
            }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Console.WriteLine("CRITICAL RUNTIME ERROR: " + e.Exception);
            System.IO.File.AppendAllText("crash_log.txt", DateTime.Now + ": " + e.Exception.ToString() + Environment.NewLine);
            // MessageBox.Show("Runtime Error: " + e.Exception.Message);
            e.Handled = true; // Prevent crash if possible
        }
    }
}
