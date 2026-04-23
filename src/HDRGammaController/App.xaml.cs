using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Hardcodet.Wpf.TaskbarNotification;
using HDRGammaController.Services;

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

                // Extract embedded ICM profiles if missing or updated
                int extracted = ResourceExtractor.ExtractIcmProfiles();
                if (extracted > 0)
                {
                    Console.WriteLine($"App.OnStartup: Extracted/updated {extracted} ICM profiles");
                }

                // Apply theme based on Windows settings; re-apply when the user flips
                // between Light and Dark in Windows Settings without restarting the app.
                ApplyTheme();
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                Exit += (_, _) => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

                Console.WriteLine("App.OnStartup: Creating MainWindow...");
                var mainWindow = new MainWindow();
                Console.WriteLine("App.OnStartup: MainWindow created.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("CRITICAL STARTUP ERROR: " + ex);
                System.IO.File.WriteAllText("startup_log.txt", ex.ToString());
                Shutdown(-1);
            }
        }

        private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            // UserPreferenceCategory.General fires on most color/theme changes.
            if (e.Category == UserPreferenceCategory.General ||
                e.Category == UserPreferenceCategory.Color)
            {
                Dispatcher.Invoke(ApplyTheme);
            }
        }

        private void ApplyTheme()
        {
            bool isDark = ThemeDetector.IsDarkMode();
            Console.WriteLine($"App.ApplyTheme: Dark mode = {isDark}");
            
            if (isDark)
            {
                Resources["MenuBackground"] = Resources["DarkMenuBackground"];
                Resources["MenuForeground"] = Resources["DarkMenuForeground"];
                Resources["MenuBorder"] = Resources["DarkMenuBorder"];
                Resources["MenuHighlight"] = Resources["DarkMenuHighlight"];
            }
            else
            {
                Resources["MenuBackground"] = Resources["LightMenuBackground"];
                Resources["MenuForeground"] = Resources["LightMenuForeground"];
                Resources["MenuBorder"] = Resources["LightMenuBorder"];
                Resources["MenuHighlight"] = Resources["LightMenuHighlight"];
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
