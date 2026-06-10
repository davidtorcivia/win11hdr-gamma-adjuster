using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HDRGammaController.Core;

namespace HDRGammaController
{
    /// <summary>
    /// Patch window for the post-apply verify pass, styled like the main calibration
    /// measurement screen: a patch rectangle of the SAME size and placement the user chose
    /// during calibration, on a black field, with a dim progress line — instead of the old
    /// full-screen color flashing. Windows applies the installed MHC2 profile at the
    /// compositor, so what the probe sees through this window IS the corrected output.
    /// </summary>
    public sealed class PatchDisplayWindow : Window
    {
        private readonly Border _patch;
        private readonly TextBlock _status;

        public PatchDisplayWindow(MonitorInfo monitor, double patchSize = 600, double offsetX = 0, double offsetY = 0)
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

            _patch = new Border
            {
                Width = patchSize,
                Height = patchSize,
                Background = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new TranslateTransform(offsetX, offsetY),
            };

            // Same idea as the calibration screen's progress text: dim, well away from the
            // patch so it doesn't meaningfully change what the probe integrates.
            _status = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(24, 0, 0, 18),
                Text = "Verifying calibration…",
            };

            var root = new Grid();
            root.Children.Add(_patch);
            root.Children.Add(_status);
            Content = root;
        }

        public void SetColor(double r, double g, double b)
        {
            _patch.Background = new SolidColorBrush(Color.FromRgb(
                (byte)Math.Round(Math.Clamp(r, 0, 1) * 255),
                (byte)Math.Round(Math.Clamp(g, 0, 1) * 255),
                (byte)Math.Round(Math.Clamp(b, 0, 1) * 255)));
        }

        public void SetStatus(string text) => _status.Text = text;
    }
}
