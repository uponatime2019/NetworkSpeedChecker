using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Windows.Foundation;

namespace NetworkSpeedChecker.Controls
{
    /// <summary>One plotted bar.</summary>
    public class ChartPoint
    {
        public DateTime Date { get; init; }
        public double Value { get; init; }
        public string Tooltip { get; set; } = "";
    }

    /// <summary>
    /// Lightweight bar chart drawn on XAML canvases: gridlines, rounded bars, hover
    /// highlighting, value/date tooltips and a dashed average line. Oldest → newest.
    /// </summary>
    public sealed partial class MetricBarChart : UserControl
    {
        private List<ChartPoint> _points = new();
        private Brush _barBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x08, 0x91, 0xB2));
        private Brush _barBrushHover = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x06, 0xB6, 0xD4));
        private string _unit = "";

        public MetricBarChart()
        {
            InitializeComponent();
            SizeChanged += (_, _) => Render();
        }

        public void SetData(IEnumerable<ChartPoint> points, Brush barBrush, Brush hoverBrush, string unit)
        {
            _points = points?.ToList() ?? new List<ChartPoint>();
            _barBrush = barBrush;
            _barBrushHover = hoverBrush;
            _unit = unit;
            Render();
        }

        private static double NiceMax(double value)
        {
            if (value <= 0) return 1;
            double exponent = Math.Floor(Math.Log10(value));
            double fraction = value / Math.Pow(10, exponent);
            double niceFraction = fraction switch
            {
                <= 1 => 1,
                <= 2 => 2,
                <= 5 => 5,
                _ => 10
            };
            return niceFraction * Math.Pow(10, exponent);
        }

        private void Render()
        {
            Plot.Children.Clear();
            LeftAxis.Children.Clear();
            BottomAxis.Children.Clear();
            if (_points.Count == 0) return;

            double plotW = Math.Max(60, Plot.ActualWidth);
            double plotH = Math.Max(60, Plot.ActualHeight);
            if (plotW < 80 || plotH < 60) return; // not measured yet

            var values = _points.Select(p => p.Value).ToList();
            double max = NiceMax(values.Max() * 1.08);
            double avg = values.Average();

            double topPad = 8;
            double bottomPad = 4;
            double usableH = plotH - topPad - bottomPad;
            double Y(double v) => topPad + usableH * (1 - v / max);

            // --- Gridlines + y labels ---
            const int lines = 4;
            for (int i = 0; i <= lines; i++)
            {
                double v = max * i / lines;
                double y = Y(v);
                var line = new Line
                {
                    X1 = 0,
                    X2 = plotW,
                    Y1 = y,
                    Y2 = y,
                    StrokeThickness = 1,
                    Stroke = (Brush)Application.Current.Resources["ContentGridBrush"]
                };
                if (i == 0) line.StrokeThickness = 1.5;
                Plot.Children.Add(line);

                var label = new TextBlock
                {
                    Text = v >= 100 ? v.ToString("0") : v.ToString("0.#"),
                    FontSize = 14,
                    Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"]
                };
                // WinUI Canvas has no SetRight; right-align via measured width once loaded.
                label.Loaded += (_, _) =>
                {
                    Canvas.SetLeft(label, Math.Max(0, LeftAxis.ActualWidth - label.ActualWidth - 6));
                    Canvas.SetTop(label, y - label.ActualHeight / 2);
                };
                Canvas.SetTop(label, y - 8);
                LeftAxis.Children.Add(label);
            }

            // --- Bars ---
            int n = _points.Count;
            double slot = plotW / n;
            double barW = Math.Max(6, Math.Min(46, slot * 0.6));

            for (int i = 0; i < n; i++)
            {
                var p = _points[i];
                double x = i * slot + (slot - barW) / 2;
                double y = Y(p.Value);
                double h = Math.Max(3, plotH - bottomPad - y);

                var bar = new Rectangle
                {
                    Width = barW,
                    Height = h,
                    RadiusX = Math.Min(6, barW / 2),
                    RadiusY = Math.Min(6, barW / 2),
                    Fill = _barBrush,
                    Opacity = 0.92,
                    Tag = i
                };
                ToolTipService.SetToolTip(bar, new ToolTip
                {
                    Content = p.Tooltip
                });
                bar.PointerEntered += (s, _) =>
                {
                    if (s is Rectangle r) { r.Fill = _barBrushHover; r.Opacity = 1; }
                };
                bar.PointerExited += (s, _) =>
                {
                    if (s is Rectangle r) { r.Fill = _barBrush; r.Opacity = 0.92; }
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y);
                Plot.Children.Add(bar);
            }

            // --- Average line ---
            double avgY = Y(avg);
            var avgLine = new Line
            {
                X1 = 0,
                X2 = plotW,
                Y1 = avgY,
                Y2 = avgY,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Stroke = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                Opacity = 0.65
            };
            Plot.Children.Add(avgLine);

            // --- X labels: up to 5 ticks spread across the bars ---
            int ticks = Math.Min(5, n);
            TimeSpan span = _points[n - 1].Date - _points[0].Date;
            for (int t = 0; t < ticks; t++)
            {
                double frac = ticks == 1 ? 0 : (double)t / (ticks - 1);
                int idx = (int)Math.Round(frac * (n - 1));
                var date = _points[idx].Date;

                var tb = new TextBlock
                {
                    Text = FormatDate(date, span),
                    FontSize = 14,
                    Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"]
                };
                tb.Loaded += (_, _) =>
                {
                    double left = frac * plotW - tb.ActualWidth / 2;
                    Canvas.SetLeft(tb, Math.Clamp(left, 0, Math.Max(0, plotW - tb.ActualWidth)));
                };
                BottomAxis.Children.Add(tb);
            }
        }

        private static string FormatDate(DateTime d, TimeSpan span)
        {
            if (span.TotalDays <= 2)
                return d.ToString("h:mm tt", CultureInfo.CurrentCulture);
            if (span.TotalDays <= 270)
                return d.ToString("MMM d", CultureInfo.CurrentCulture);
            return d.ToString("MMM yy", CultureInfo.CurrentCulture);
        }
    }
}
