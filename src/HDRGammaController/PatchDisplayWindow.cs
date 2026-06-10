using System;
using System.Windows;
using System.Windows.Media;
using HDRGammaController.Core;

namespace HDRGammaController
{
    /// <summary>
    /// Minimal full-screen color patch window for the post-apply verify pass: a borderless,
    /// topmost window filling the target monitor so the probe reads the patch wherever it
    /// hangs. Windows applies the installed MHC2 profile at the compositor, so what the probe
    /// sees through this window IS the corrected output.
    /// </summary>
    public sealed class PatchDisplayWindow : Window
    {
        public PatchDisplayWindow(MonitorInfo monitor)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Background = Brushes.Black;
            Cursor = System.Windows.Input.Cursors.None;

            // Same monitor-bounds placement the calibration window uses (DXGI desktop rect).
            var b = monitor.MonitorBounds;
            if (b.Right > b.Left && b.Bottom > b.Top)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = b.Left;
                Top = b.Top;
                Width = b.Right - b.Left;
                Height = b.Bottom - b.Top;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Width = 800;
                Height = 600;
            }
        }

        public void SetColor(double r, double g, double b)
        {
            Background = new SolidColorBrush(Color.FromRgb(
                (byte)Math.Round(Math.Clamp(r, 0, 1) * 255),
                (byte)Math.Round(Math.Clamp(g, 0, 1) * 255),
                (byte)Math.Round(Math.Clamp(b, 0, 1) * 255)));
        }
    }
}
