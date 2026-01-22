using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HDRGammaController.Behaviors
{
    /// <summary>
    /// Attached behavior for slider center detent/snap functionality.
    /// When the slider value is within the snap threshold of the detent value,
    /// it snaps to the detent value for easier "return to default" behavior.
    /// </summary>
    public static class SliderDetent
    {
        #region DetentValue (double)
        public static readonly DependencyProperty DetentValueProperty =
            DependencyProperty.RegisterAttached(
                "DetentValue",
                typeof(double),
                typeof(SliderDetent),
                new PropertyMetadata(0.0, OnDetentPropertyChanged));

        public static double GetDetentValue(DependencyObject obj) => (double)obj.GetValue(DetentValueProperty);
        public static void SetDetentValue(DependencyObject obj, double value) => obj.SetValue(DetentValueProperty, value);
        #endregion

        #region SnapThreshold (double) - how close to snap
        public static readonly DependencyProperty SnapThresholdProperty =
            DependencyProperty.RegisterAttached(
                "SnapThreshold",
                typeof(double),
                typeof(SliderDetent),
                new PropertyMetadata(2.0, OnDetentPropertyChanged));

        public static double GetSnapThreshold(DependencyObject obj) => (double)obj.GetValue(SnapThresholdProperty);
        public static void SetSnapThreshold(DependencyObject obj, double value) => obj.SetValue(SnapThresholdProperty, value);
        #endregion

        #region IsEnabled (bool)
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(SliderDetent),
                new PropertyMetadata(false, OnDetentPropertyChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);
        #endregion

        private static void OnDetentPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Slider slider)
            {
                // Unsubscribe first to avoid duplicates
                slider.ValueChanged -= Slider_ValueChanged;
                slider.PreviewMouseUp -= Slider_PreviewMouseUp;

                if (GetIsEnabled(slider))
                {
                    slider.ValueChanged += Slider_ValueChanged;
                    slider.PreviewMouseUp += Slider_PreviewMouseUp;
                }
            }
        }

        private static void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider && !_isSnapping)
            {
                double detent = GetDetentValue(slider);
                double threshold = GetSnapThreshold(slider);
                double current = e.NewValue;

                // Check if we're close to the detent but not exactly on it
                if (Math.Abs(current - detent) <= threshold && Math.Abs(current - detent) > 0.001)
                {
                    // Visual feedback - we're in the "snap zone"
                    // The actual snap happens on mouse up
                }
            }
        }

        private static bool _isSnapping = false;

        private static void Slider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider)
            {
                double detent = GetDetentValue(slider);
                double threshold = GetSnapThreshold(slider);
                double current = slider.Value;

                // Snap to detent if within threshold
                if (Math.Abs(current - detent) <= threshold)
                {
                    _isSnapping = true;
                    slider.Value = detent;
                    _isSnapping = false;
                }
            }
        }
    }

    /// <summary>
    /// Attached behavior for click-to-edit functionality on TextBlocks.
    /// Shows a TextBox overlay when clicked for direct value entry.
    /// </summary>
    public static class ClickToEdit
    {
        #region TargetSlider
        public static readonly DependencyProperty TargetSliderProperty =
            DependencyProperty.RegisterAttached(
                "TargetSlider",
                typeof(Slider),
                typeof(ClickToEdit),
                new PropertyMetadata(null, OnTargetSliderChanged));

        public static Slider GetTargetSlider(DependencyObject obj) => (Slider)obj.GetValue(TargetSliderProperty);
        public static void SetTargetSlider(DependencyObject obj, Slider value) => obj.SetValue(TargetSliderProperty, value);
        #endregion

        #region FormatString
        public static readonly DependencyProperty FormatStringProperty =
            DependencyProperty.RegisterAttached(
                "FormatString",
                typeof(string),
                typeof(ClickToEdit),
                new PropertyMetadata("{0:F0}"));

        public static string GetFormatString(DependencyObject obj) => (string)obj.GetValue(FormatStringProperty);
        public static void SetFormatString(DependencyObject obj, string value) => obj.SetValue(FormatStringProperty, value);
        #endregion

        #region Suffix
        public static readonly DependencyProperty SuffixProperty =
            DependencyProperty.RegisterAttached(
                "Suffix",
                typeof(string),
                typeof(ClickToEdit),
                new PropertyMetadata(""));

        public static string GetSuffix(DependencyObject obj) => (string)obj.GetValue(SuffixProperty);
        public static void SetSuffix(DependencyObject obj, string value) => obj.SetValue(SuffixProperty, value);
        #endregion

        private static void OnTargetSliderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock textBlock)
            {
                textBlock.MouseLeftButtonDown -= TextBlock_MouseLeftButtonDown;
                textBlock.Cursor = null;

                if (e.NewValue is Slider)
                {
                    textBlock.MouseLeftButtonDown += TextBlock_MouseLeftButtonDown;
                    textBlock.Cursor = Cursors.Hand;
                    textBlock.ToolTip = "Click to enter value";
                }
            }
        }

        private static void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && GetTargetSlider(textBlock) is Slider slider)
            {
                e.Handled = true;
                ShowEditPopup(textBlock, slider);
            }
        }

        private static void ShowEditPopup(TextBlock textBlock, Slider slider)
        {
            // Create a simple input dialog
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = textBlock,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true
            };

            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3D, 0x3D, 0x3D)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4)
            };

            var textBox = new TextBox
            {
                Text = slider.Value.ToString("F2"),
                Width = 80,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 13,
                SelectionBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4)),
                CaretBrush = System.Windows.Media.Brushes.White
            };

            textBox.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    if (double.TryParse(textBox.Text, out double value))
                    {
                        // Clamp to slider range
                        value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, value));
                        slider.Value = value;
                    }
                    popup.IsOpen = false;
                }
                else if (args.Key == Key.Escape)
                {
                    popup.IsOpen = false;
                }
            };

            textBox.LostFocus += (s, args) =>
            {
                if (double.TryParse(textBox.Text, out double value))
                {
                    value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, value));
                    slider.Value = value;
                }
                popup.IsOpen = false;
            };

            border.Child = textBox;
            popup.Child = border;
            popup.IsOpen = true;

            // Focus and select all text
            textBox.Focus();
            textBox.SelectAll();
        }
    }
}
