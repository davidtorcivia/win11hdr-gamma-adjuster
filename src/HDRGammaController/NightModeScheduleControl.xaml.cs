using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HDRGammaController.Core;

namespace HDRGammaController
{
    public partial class NightModeScheduleControl : UserControl
    {
        public List<ScheduleTriggerType> TriggerTypes { get; } = Enum.GetValues(typeof(ScheduleTriggerType)).Cast<ScheduleTriggerType>().ToList();

        public event Action? ScheduleChanged;
        public event Action<int?>? PreviewTemperatureRequested;

        private NightModeSettings _settings;
        private double? _lat;
        private double? _lon;

        // For Dragging
        private bool _isDragging = false;
        private SchedulePointViewModel? _dragItem = null;
        private DateTime _lastPreviewTime = DateTime.MinValue;
        private const int PreviewThrottleMs = 50; // Update preview at most every 50ms
        
        private const int GraphStartHour = 4;
        private const double GraphStartMins = GraphStartHour * 60;

        public NightModeScheduleControl()
        {
            InitializeComponent();
        }

        public void Initialize(NightModeSettings settings)
        {
            _settings = settings;
            _lat = settings.Latitude;
            _lon = settings.Longitude;

            if (_lat.HasValue) LatBox.Text = _lat.Value.ToString("F2");
            if (_lon.HasValue) LonBox.Text = _lon.Value.ToString("F2");

            // Ensure schedule exists
            _settings.EnsureSchedule(_lat, _lon);
            
            RefreshList();
            DrawGraph();
        }

        private void NotifyChange()
        {
            ScheduleChanged?.Invoke();
        }

        private void Location_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(LatBox.Text, out double lat)) _lat = lat;
            if (double.TryParse(LonBox.Text, out double lon)) _lon = lon;
            
            _settings.Latitude = _lat;
            _settings.Longitude = _lon;
            
            DrawGraph();
            NotifyChange();
        }

        private async void Detect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = await client.GetStringAsync("http://ip-api.com/json/?fields=lat,lon");
                
                var latMatch = System.Text.RegularExpressions.Regex.Match(response, "\"lat\":([\\d.-]+)");
                var lonMatch = System.Text.RegularExpressions.Regex.Match(response, "\"lon\":([\\d.-]+)");
                
                if (latMatch.Success && lonMatch.Success)
                {
                    if (double.TryParse(latMatch.Groups[1].Value, out double lat)) 
                    {
                        _lat = lat;
                        LatBox.Text = lat.ToString("F2");
                    }
                    if (double.TryParse(lonMatch.Groups[1].Value, out double lon)) 
                    {
                        _lon = lon;
                        LonBox.Text = lon.ToString("F2");
                    }
                    
                    _settings.Latitude = _lat;
                    _settings.Longitude = _lon;
                    DrawGraph();
                    NotifyChange();
                }
            }
            catch { }
        }

        private void RefreshList()
        {
            var viewModels = _settings.Schedule.Select(p => new SchedulePointViewModel(p) { Parent = this }).ToList();
            PointsGrid.ItemsSource = viewModels;
        }
        
        // Internal method for ViewModel to notify
        public void OnPointChanged()
        {
            DrawGraph();
            NotifyChange();
        }

        private void DrawGraph()
        {
            GraphCanvas.Children.Clear();
            if (GraphCanvas.ActualWidth == 0 || GraphCanvas.ActualHeight == 0) return;

            double w = GraphCanvas.ActualWidth;
            double h = GraphCanvas.ActualHeight;
            
            // Draw Grid
            var gridBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            var midBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)); // Lighter/Grey for midnight
            var textBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150));
            
            // Draw Vertical Time Lines (every 4 hours starting from GraphStartHour?)
            // We want to cover 24 hours. 0 to 24 relative to Start.
            for (int i = 0; i <= 24; i += 2) // Every 2 hours
            {
                double x = (i / 24.0) * w;
                int actualHour = (GraphStartHour + i) % 24;
                
                bool isMajor = (actualHour % 6 == 0); // 0, 6, 12, 18
                bool isMidnight = (actualHour == 0);
                
                var brush = isMidnight ? midBrush : gridBrush;
                var dash = isMidnight ? new DoubleCollection { 4, 2 } : (isMajor ? null : new DoubleCollection { 2, 4 });
                
                if (isMidnight || isMajor)
                {
                    var line = new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = brush, StrokeThickness = isMidnight ? 2 : 1 };
                    if (dash != null) line.StrokeDashArray = dash;
                    GraphCanvas.Children.Add(line);

                    var tb = new TextBlock { Text = $"{actualHour}:00", FontSize = 10, Foreground = textBrush };
                    Canvas.SetLeft(tb, x + 4);
                    Canvas.SetTop(tb, h - 16);
                    GraphCanvas.Children.Add(tb);
                }
            }
            
            // Temp Lines (6500 down to 2000)
            int[] temps = { 6500, 5000, 3500, 2000 };
            foreach (var k in temps)
            {
                double y = TempToY(k, h);
                var line = new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = gridBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 4 } };
                GraphCanvas.Children.Add(line);
                
                var tb = new TextBlock { Text = $"{k}K", FontSize = 9, Foreground = textBrush };
                Canvas.SetLeft(tb, 4);
                Canvas.SetTop(tb, y - 6);
                GraphCanvas.Children.Add(tb);
            }

            // --- Draw Curve ---
            // We reuse NightModeService logic structure to sample points
            // But simplify for visualization: sample every 10 mins (144 points)
            var points = new PointCollection();
            
            // Temporary service instance to calculate curve? Or duplicate logic?
            // Duplicating logic is safer to avoid side effects or heavy dependencies.
            // Actually, calculating the curve for 144 points is fast.
            
            // Resolve points once
            var resolved = _settings.Schedule.Select(p => 
            {
                var time = p.GetTimeOfDay(_lat, _lon);
                return (Time: time, Point: p); 
            }).OrderBy(x => x.Time).ToList();
            
            // Helper to get temp at time T (minutes from 0 to 1440)
            Func<double, double> getTemp = (minutes) =>
            {
                TimeSpan t = TimeSpan.FromMinutes(minutes);
                
                // Find current segment (same logic as Service)
                int idx = -1;
                for(int i=0; i<resolved.Count; i++) if (resolved[i].Time.TotalMinutes <= minutes) idx = i;
                
                (TimeSpan Time, NightModeSchedulePoint Point) curr, prev;
                
                if (idx == -1) // Early morning, use yesterday's last
                {
                    if (resolved.Count == 0) return 6500;
                    var last = resolved.Last();
                    curr = (last.Time - TimeSpan.FromHours(24), last.Point);
                    prev = resolved.Count > 1 ? resolved[resolved.Count-2] : last; // simplification
                    if (resolved.Count > 1) prev = (prev.Time - TimeSpan.FromHours(24), prev.Point);
                    else prev = (prev.Time - TimeSpan.FromHours(48), prev.Point);
                }
                else
                {
                    curr = resolved[idx];
                    if (idx > 0) prev = resolved[idx-1];
                    else 
                    {
                        var last = resolved.Last();
                        prev = (last.Time - TimeSpan.FromHours(24), last.Point);
                    }
                }
                
                double elapsed = (t - curr.Time).TotalMinutes;
                if (elapsed < 0) elapsed += 1440;
                
                double trg = curr.Point.TargetKelvin;
                double str = prev.Point.TargetKelvin;
                double fade = curr.Point.FadeMinutes;
                
                if (elapsed < fade && fade > 0)
                {
                     double p = elapsed / fade;
                     return str + (trg - str) * p;
                }
                return trg;
            };

            for (double relM = 0; relM <= 1440; relM += 5) // 5 min steps relative to Start
            {
                double absM = relM + GraphStartMins;
                if (absM >= 1440) absM -= 1440;
                
                double k = getTemp(absM);
                points.Add(new Point( (relM/1440.0)*w, TempToY(k, h) ));
            }
            
            var poly = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(255, 180, 0)),
                StrokeThickness = 2,
                Points = points
            };
            GraphCanvas.Children.Add(poly);
            
            // --- Draw Nodes ---
            foreach (var r in resolved)
            {
                double mx = TimeToX(r.Time, w);
                double my = TempToY(r.Point.TargetKelvin, h);
                
                var el = new Ellipse
                {
                    Width = 10, Height = 10, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1,
                    Tag = r.Point // Store model
                };
                Canvas.SetLeft(el, mx - 5);
                Canvas.SetTop(el, my - 5);
                
                // Events for drag
                el.MouseLeftButtonDown += Node_MouseDown;
                el.MouseLeftButtonUp += GraphCanvas_MouseUp; // Critical for robust release checking
                
                GraphCanvas.Children.Add(el);
            }
            
            // --- Draw "Now" Indicator ---
            double nowX = TimeToX(DateTime.Now.TimeOfDay, w);
            var nowLine = new Line { X1 = nowX, Y1 = 0, X2 = nowX, Y2 = h, Stroke = Brushes.Red, StrokeThickness = 1, Opacity = 0.5 };
            GraphCanvas.Children.Add(nowLine);
        }

        private double TempToY(double k, double h)
        {
            // Map 6500 -> 10% Height
            // Map 1900 -> 90% Height
            double tNorm = (k - 1900) / (6500 - 1900); // 0 (1900) to 1 (6500)
            return h - (tNorm * (h * 0.8) + (h * 0.1));
        }
        
        private double TimeToX(TimeSpan t, double w)
        {
            double m = t.TotalMinutes;
            double relM = m - GraphStartMins;
            if (relM < 0) relM += 1440;
            return (relM / 1440.0) * w;
        }
        
        private TimeSpan XToTime(double x, double w)
        {
            double relM = (x / w) * 1440;
            double absM = relM + GraphStartMins;
            if (absM >= 1440) absM -= 1440;
            if (absM < 0) absM += 1440; // Safety
            return TimeSpan.FromMinutes(absM);
        }
        
        private double YToTemp(double y, double h)
        {
            double tNorm = (h - y - (h * 0.1)) / (h * 0.8);
            double k = tNorm * (6500 - 1900) + 1900;
            return Math.Clamp(k, 1900, 6500);
        }

        private void Node_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Ellipse el && el.Tag is NightModeSchedulePoint p)
            {
                var vm = (PointsGrid.ItemsSource as List<SchedulePointViewModel>)?.FirstOrDefault(v => v.Model == p);
                if (vm != null)
                {
                    _dragItem = vm;
                    _isDragging = true;
                    el.CaptureMouse();
                    e.Handled = true;
                    
                    // Select row in grid
                    PointsGrid.SelectedItem = vm;
                    
                    // Show Overlay
                    DragOverlay.Visibility = Visibility.Visible;
                    UpdateOverlay(e.GetPosition(GraphCanvas), GraphCanvas.ActualWidth, vm);
                    
                    // Start Preview
                    PreviewTemperatureRequested?.Invoke(vm.TargetKelvin);
                }
            }
        }
        
        private void UpdateOverlay(Point pos, double containerWidth, SchedulePointViewModel vm)
        {
            try 
            {
                OverlayTime.Text = vm.DisplayTime;
                OverlayTemp.Text = $"{vm.TargetKelvin}K";
                
                // Smart Positioning
                double left = pos.X + 15;
                if (left + 100 > containerWidth) // Assuming tooltip width ~100
                {
                    left = pos.X - 115; // Shift to left side
                }
                
                DragOverlay.Margin = new Thickness(left, pos.Y - 40, 0, 0);
            }
            catch {}
        }

        private void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && _dragItem != null)
            {
                var pos = e.GetPosition(GraphCanvas);
                double w = GraphCanvas.ActualWidth;
                double h = GraphCanvas.ActualHeight;
                
                // Update Time
                double pctX = Math.Clamp(pos.X / w, 0, 1);
                
                // Used helper to get time from X
                TimeSpan newTime = XToTime(pos.X, w); // XToTime handles boundaries loosely, but we clamped X
                // Re-clamp X? XToTime doesn't clamp. 
                // But pos.X is not clamped yet.
                // Let's rely on XToTime for logic.
                // If pos.X is out of bounds, it might produce odd times? 
                // XToTime logic: pct = x/w. If x < 0 -> pct < 0. relM < 0. absM = relM + start. 
                // e.g. x=-10. relM = -10. absM = 230. 
                // We want to CLAMP x to 0..w first.
                
                double clampedX = Math.Clamp(pos.X, 0, w);
                newTime = XToTime(clampedX, w);
                
                double newMins = newTime.TotalMinutes;
                
                // Update Temp
                double newK = YToTemp(pos.Y, h);
                _dragItem.TargetKelvin = (int)newK;
                
                if (_dragItem.TriggerType == ScheduleTriggerType.FixedTime)
                {
                    _dragItem.Model.Time = newTime;
                }
                
                _dragItem.RefreshDisplay(); // Update UI string
                
                DrawGraph(); // Redraw dynamic
                
                // Update Overlay
                UpdateOverlay(pos, w, _dragItem);
                
                // Live visual preview (force temp), throttled
                var now = DateTime.Now;
                if ((now - _lastPreviewTime).TotalMilliseconds > PreviewThrottleMs)
                {
                    _lastPreviewTime = now;
                    PreviewTemperatureRequested?.Invoke(_dragItem.TargetKelvin);
                }
            }
        }

        private void GraphCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                _dragItem = null;
                Mouse.Capture(null); // Release capture global
                
                DragOverlay.Visibility = Visibility.Collapsed;
                
                // Stop preview, revert to schedule logic (which is now updated)
                PreviewTemperatureRequested?.Invoke(null);
                
                RefreshList(); // Sync grid
                NotifyChange(); // NOW we save
            }
        }
        
        private void GraphCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Click on empty space creates point?
            if (e.ClickCount == 2)
            {
                var pos = e.GetPosition(GraphCanvas);
                double w = GraphCanvas.ActualWidth;
                double h = GraphCanvas.ActualHeight;
                
                double temp = YToTemp(pos.Y, h);
                
                TimeSpan time = XToTime(Math.Clamp(pos.X, 0, w), w);
                
                var p = new NightModeSchedulePoint
                {
                    TriggerType = ScheduleTriggerType.FixedTime,
                    Time = time,
                    TargetKelvin = (int)temp
                };
                
                _settings.Schedule.Add(p);
                 RefreshList();
                DrawGraph();
                NotifyChange();
            }
        }

        private void GraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawGraph();
        }

        private void AddPoint_Click(object sender, RoutedEventArgs e)
        {
            _settings.Schedule.Add(new NightModeSchedulePoint 
            { 
                Time = DateTime.Now.TimeOfDay, 
                TargetKelvin = 3000, 
                FadeMinutes = 30 
            });
            RefreshList();
            DrawGraph();
            NotifyChange();
        }
        
        private void Reset_Click(object sender, RoutedEventArgs e)
        {
             _settings.Schedule.Clear();
             _settings.EnsureSchedule(_lat, _lon);
             RefreshList();
             DrawGraph();
             NotifyChange();
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is SchedulePointViewModel vm)
            {
                _settings.Schedule.Remove(vm.Model);
                RefreshList();
                DrawGraph();
                NotifyChange();
            }
        }
    }

    public class SchedulePointViewModel
    {
        public NightModeScheduleControl Parent { get; set; }
        public NightModeSchedulePoint Model { get; }
        
        public SchedulePointViewModel(NightModeSchedulePoint model) 
        {
            Model = model;
        }
        
        public ScheduleTriggerType TriggerType
        {
            get => Model.TriggerType;
            set { Model.TriggerType = value; Notify(); }
        }

        public string DisplayTime
        {
            get
            {
                if (Model.TriggerType == ScheduleTriggerType.FixedTime)
                    return Model.Time.ToString(@"hh\:mm");
                else
                    return (Model.OffsetMinutes >= 0 ? "+" : "") + Model.OffsetMinutes + "m";
            }
            set
            {
                if (Model.TriggerType == ScheduleTriggerType.FixedTime)
                {
                    if (TimeSpan.TryParse(value, out var t)) Model.Time = t;
                }
                else
                {
                    if (double.TryParse(value.Replace("m", ""), out var d)) Model.OffsetMinutes = d;
                }
                Notify();
            }
        }

        public int TargetKelvin
        {
            get => Model.TargetKelvin;
            set { Model.TargetKelvin = value; Notify(); }
        }
        
        public int FadeMinutes
        {
            get => Model.FadeMinutes;
            set { Model.FadeMinutes = value; Notify(); }
        }
        
        public void RefreshDisplay()
        {
            // Used to force UI update if backing fields change
        }
        
        private void Notify()
        {
             Parent?.OnPointChanged();
        }
    }
}
