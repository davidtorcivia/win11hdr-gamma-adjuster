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
        public event Func<int?, Task>? PreviewTemperatureRequested;

        private NightModeSettings _settings = null!; // Set by Initialize
        private double? _lat;
        private double? _lon;

        private bool _isDragging = false;
        private SchedulePointViewModel? _dragItem = null;
        private DateTime _lastPreviewTime = DateTime.MinValue;
        private DateTime _lastDrawTime = DateTime.MinValue;
        private const int PreviewThrottleMs = 250; // Throttle to 4fps for heavy API calls
        private const int DrawThrottleMs = 16; // ~60fps for UI drawing
        private bool _isPreviewRunning = false; // Prevent stacking calls
        private bool _drawPending = false;

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
            DrawGrid();
            DrawCurve();
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

            DrawGrid();
            DrawCurve();
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

                    // Auto-convert default schedule to Sunset/Sunrise if simple
                    if (_settings.Schedule.Count == 2)
                    {
                         // Heuristic: If close to default 21:00 / 07:00
                         var p1 = _settings.Schedule[0];
                         var p2 = _settings.Schedule[1];
                         
                         p1.TriggerType = ScheduleTriggerType.Sunset;
                         p2.TriggerType = ScheduleTriggerType.Sunrise;
                         
                         // Reset offsets logic? Just set types.
                    }
                    
                    RefreshList();
                    DrawGrid();
                    DrawCurve();
                    NotifyChange();
                }
            }
            catch { }
        }

        private void RefreshList()
        {
            // Sort points chronologically by their resolved time of day
            var sortedPoints = _settings.Schedule
                .Select(p => new { Point = p, Time = p.GetTimeOfDay(_lat, _lon) })
                .OrderBy(x => x.Time)
                .Select(x => new SchedulePointViewModel(x.Point) { Parent = this })
                .ToList();

            PointsGrid.ItemsSource = sortedPoints;
        }
        
        // Internal method for ViewModel to notify
        public void OnPointChanged()
        {
            DrawCurve();
            NotifyChange();
        }

        private void DrawGrid()
        {
            GridCanvas.Children.Clear();
            if (GridCanvas.ActualWidth == 0 || GridCanvas.ActualHeight == 0) return;

            double w = GridCanvas.ActualWidth;
            double h = GridCanvas.ActualHeight;
            
            // Draw Grid
            var gridBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            // ... (rest of grid drawing logic targeting GridCanvas)
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
                    GridCanvas.Children.Add(line);

                    var tb = new TextBlock { Text = $"{actualHour}:00", FontSize = 10, Foreground = textBrush };
                    Canvas.SetLeft(tb, x + 4);
                    Canvas.SetTop(tb, h - 16);
                    GridCanvas.Children.Add(tb);
                }
            }
            
            // Temp Lines (6500 down to 2000)
            int[] temps = { 6500, 5000, 3500, 2000 };
            foreach (var k in temps)
            {
                double y = TempToY(k, h);
                var line = new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = gridBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 4 } };
                GridCanvas.Children.Add(line);
                
                var tb = new TextBlock { Text = $"{k}K", FontSize = 9, Foreground = textBrush };
                Canvas.SetLeft(tb, 4);
                Canvas.SetTop(tb, y - 6);
                GridCanvas.Children.Add(tb);
            }
        }

        private void DrawCurve()
        {
            if (CurveCanvas.ActualWidth == 0 || CurveCanvas.ActualHeight == 0) return;

            double w = CurveCanvas.ActualWidth;
            double h = CurveCanvas.ActualHeight;

            // Resolve points once
            var resolved = _settings.Schedule.Select(p => 
            {
                var time = p.GetTimeOfDay(_lat, _lon);
                return (Time: time, Point: p); 
            }).OrderBy(x => x.Time).ToList();

            // 1. Update/Create Polyline
            Polyline poly;
            if (CurveCanvas.Children.Count > 0 && CurveCanvas.Children[0] is Polyline p)
            {
                poly = p;
            }
            else
            {
                CurveCanvas.Children.Clear(); // Safety
                poly = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 180, 0)),
                    StrokeThickness = 2
                };
                CurveCanvas.Children.Add(poly);
            }

            var points = new PointCollection();
            
            // Helper to get temp at time T (minutes from 0 to 1440)
            // (Inlined or efficient reuse of service logic)
            Func<double, double> getTemp = (minutes) =>
            {
                TimeSpan t = TimeSpan.FromMinutes(minutes);
                int idx = -1;
                for(int i=0; i<resolved.Count; i++) if (resolved[i].Time.TotalMinutes <= minutes) idx = i;
                
                (TimeSpan Time, NightModeSchedulePoint Point) curr, prev;
                
                if (idx == -1)
                {
                    if (resolved.Count == 0) return 6500;
                    var last = resolved.Last();
                    curr = (last.Time - TimeSpan.FromHours(24), last.Point);
                    prev = resolved.Count > 1 ? resolved[resolved.Count-2] : last;
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

            // Optimization: Sample fewer points if dragging? No, 288 is fine if we just update PointCollection.
            // Actually PointCollection is a Freezeable?
            // Creating new PointCollection is fast.
            for (double relM = 0; relM <= 1440; relM += 5) 
            {
                double absM = relM + GraphStartMins;
                if (absM >= 1440) absM -= 1440;
                
                double k = getTemp(absM);
                points.Add(new Point( (relM/1440.0)*w, TempToY(k, h) ));
            }
            poly.Points = points;

            // 2. Update/Create Nodes
            // We assume Nodes are at indices 1..N
            // If count mismatches, clear and rebuild (e.g. added/removed)
            // Expected count = 1 (Polyline) + N (Nodes) + 1 (NowLine)
            if (CurveCanvas.Children.Count != 1 + resolved.Count + 1)
            {
                 // Full rebuild if mismatch
                 CurveCanvas.Children.Clear();
                 CurveCanvas.Children.Add(poly);
                 
                 foreach (var r in resolved)
                 {
                     var el = new Ellipse
                     {
                         Width = 10, Height = 10, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1,
                         Tag = r.Point // Store model
                     };
                     el.MouseLeftButtonDown += Node_MouseDown;
                     el.MouseLeftButtonUp += GraphCanvas_MouseUp;
                     CurveCanvas.Children.Add(el);
                 }
                 
                 // Now Line
                 var nowLine = new Line { Stroke = Brushes.Red, StrokeThickness = 1, Opacity = 0.5 };
                 CurveCanvas.Children.Add(nowLine);
            }

            // Sync Node Positions
            for(int i=0; i<resolved.Count; i++)
            {
                if (CurveCanvas.Children[1+i] is Ellipse el)
                {
                    var r = resolved[i];
                    double mx = TimeToX(r.Time, w);
                    double my = TempToY(r.Point.TargetKelvin, h);
                    Canvas.SetLeft(el, mx - 5);
                    Canvas.SetTop(el, my - 5);
                    el.Tag = r.Point; // Ensure Tag is updated if order changed? Tag refers to object reference, safe.
                }
            }
            
            // Sync Now Line
            if (CurveCanvas.Children.Count > 0 && CurveCanvas.Children[CurveCanvas.Children.Count-1] is Line nl)
            {
                 double nowX = TimeToX(DateTime.Now.TimeOfDay, w);
                 nl.X1 = nowX; nl.X2 = nowX;
                 nl.Y1 = 0; nl.Y2 = h;
            }
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
                    _dragItem.SuppressNotifications = true; // Suppress during drag
                    el.CaptureMouse();
                    e.Handled = true;

                    // Select row in grid
                    PointsGrid.SelectedItem = vm;

                    // Show Overlay
                    DragOverlay.Visibility = Visibility.Visible;
                    UpdateOverlay(e.GetPosition(CurveCanvas), CurveCanvas.ActualWidth, vm);

                    // Start Preview
                    // Trigger async fire
                    _ = PreviewTemperatureRequested?.Invoke(vm.TargetKelvin);
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

        private async void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && _dragItem != null)
            {
                var pos = e.GetPosition(CurveCanvas);
                double w = CurveCanvas.ActualWidth;
                double h = CurveCanvas.ActualHeight;

                double clampedX = Math.Clamp(pos.X, 0, w);
                TimeSpan newTime = XToTime(clampedX, w);

                // Update Temp
                double newK = YToTemp(pos.Y, h);
                _dragItem.TargetKelvin = (int)newK;

                if (_dragItem.TriggerType == ScheduleTriggerType.FixedTime)
                {
                    _dragItem.Model.Time = newTime;
                }

                _dragItem.RefreshDisplay(); // Update UI string

                // Update Overlay immediately (lightweight)
                UpdateOverlay(pos, w, _dragItem);

                // Throttle curve redraw to ~60fps for smooth UI
                var now = DateTime.Now;
                if ((now - _lastDrawTime).TotalMilliseconds >= DrawThrottleMs)
                {
                    _lastDrawTime = now;
                    DrawCurve();
                }
                else if (!_drawPending)
                {
                    // Schedule a final draw after throttle period
                    _drawPending = true;
                    _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                    {
                        _drawPending = false;
                        if (_isDragging) DrawCurve();
                    }));
                }

                // Live visual preview (force temp), throttled more aggressively for heavy API calls
                if ((now - _lastPreviewTime).TotalMilliseconds > PreviewThrottleMs && !_isPreviewRunning)
                {
                    _lastPreviewTime = now;
                    _isPreviewRunning = true;
                    // Fire and forget - don't await to keep UI responsive
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var task = PreviewTemperatureRequested?.Invoke(_dragItem?.TargetKelvin);
                            if (task != null) await task;
                        }
                        finally
                        {
                            _isPreviewRunning = false;
                        }
                    });
                }
            }
        }

        private void GraphCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                // Restore notifications before clearing drag state
                if (_dragItem != null)
                {
                    _dragItem.SuppressNotifications = false;
                }

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
                if (_settings.Schedule.Count >= 12)
                {
                    MessageBox.Show("Maximum of 12 schedule points allowed.", "Limit Reached", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var pos = e.GetPosition(CurveCanvas);
                double w = CurveCanvas.ActualWidth;
                double h = CurveCanvas.ActualHeight;
                
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
                DrawCurve();
                NotifyChange();
            }
        }

        private void GraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawGrid();
            DrawCurve();
        }

        private void AddPoint_Click(object sender, RoutedEventArgs e)
        {
            if (_settings.Schedule.Count >= 12)
            {
                MessageBox.Show("Maximum of 12 schedule points allowed.", "Limit Reached", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _settings.Schedule.Add(new NightModeSchedulePoint 
            { 
                Time = DateTime.Now.TimeOfDay, 
                TargetKelvin = 3000, 
                FadeMinutes = 30 
            });
            RefreshList();
            DrawCurve();
            NotifyChange();
        }
        
        private void Reset_Click(object sender, RoutedEventArgs e)
        {
             _settings.Schedule.Clear();
             _settings.EnsureSchedule(_lat, _lon);
             RefreshList();
             DrawCurve();
             NotifyChange();
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is SchedulePointViewModel vm)
            {
                _settings.Schedule.Remove(vm.Model);
                RefreshList();
                DrawCurve();
                NotifyChange();
            }
        }
    }

    public class SchedulePointViewModel
    {
        public NightModeScheduleControl Parent { get; set; } = null!; // Set by RefreshList
        public NightModeSchedulePoint Model { get; }

        /// <summary>
        /// When true, property setters don't trigger change notifications.
        /// Used during drag operations to avoid redundant updates.
        /// </summary>
        public bool SuppressNotifications { get; set; }

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
            if (!SuppressNotifications)
            {
                Parent?.OnPointChanged();
            }
        }
    }
}
