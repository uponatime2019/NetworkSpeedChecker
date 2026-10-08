using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using NetworkSpeedChecker.Controls;
using NetworkSpeedChecker.Services;

namespace NetworkSpeedChecker.Views
{
    /// <summary>Historical trends: metric selector, range selector and an interactive bar chart.</summary>
    public sealed partial class ChartsView : Page
    {
        private enum Metric { Download = 0, Upload = 1, Ping = 2 }

        public ChartsView()
        {
            InitializeComponent();

            // Defaults set in code (never SelectedIndex in XAML) so handlers run only
            // after every named field from InitializeComponent exists.
            MetricSelector.SelectedIndex = 0;
            RangeSelector.SelectedIndex = 1;

            Loaded += (_, _) => Rebuild();
            HistoryService.Instance.HistoryChanged += OnHistoryChanged;
            Unloaded += (_, _) => HistoryService.Instance.HistoryChanged -= OnHistoryChanged;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Rebuild();
        }

        private void OnHistoryChanged(object? sender, EventArgs e) =>
            DispatcherQueue.TryEnqueue(Rebuild);

        private void MetricSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try { Rebuild(); }
            catch (Exception ex) { Helpers.AppLogger.LogException(ex, "ChartsView.MetricSelector_SelectionChanged"); }
        }

        private void RangeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try { Rebuild(); }
            catch (Exception ex) { Helpers.AppLogger.LogException(ex, "ChartsView.RangeSelector_SelectionChanged"); }
        }

        private void Rebuild()
        {
            var all = HistoryService.Instance.Current;
            bool hasAny = all.Count > 0;
            if (EmptyState is null || ChartCard is null) return; // handler fired before fields ready
            EmptyState.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;
            ChartCard.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
            Legend.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
            if (!hasAny) return;

            var metric = (Metric)Math.Max(0, MetricSelector.SelectedIndex);
            int range = RangeSelector.SelectedIndex switch
            {
                0 => 10,
                1 => 30,
                _ => int.MaxValue
            };

            // Oldest → newest for left-to-right plotting.
            var slice = all.Reverse().Take(range).ToList();

            string unit = metric == Metric.Ping ? "ms" : "Mbps";
            string valueFormat(double v) => v >= 100 ? v.ToString("0") : v.ToString("0.##");

            var points = slice.Select(r => new ChartPoint
            {
                Date = r.TestedAt.LocalDateTime,
                Value = metric switch
                {
                    Metric.Download => r.DownloadMbps,
                    Metric.Upload => r.UploadMbps,
                    _ => r.PingMs
                },
                Tooltip = metric switch
                {
                    Metric.Download => $"{valueFormat(r.DownloadMbps)} Mbps download\n{r.TestedAt.LocalDateTime.ToString("ddd, MMM d, h:mm tt", CultureInfo.CurrentCulture)}",
                    Metric.Upload => $"{valueFormat(r.UploadMbps)} Mbps upload\n{r.TestedAt.LocalDateTime.ToString("ddd, MMM d, h:mm tt", CultureInfo.CurrentCulture)}",
                    _ => $"{valueFormat(r.PingMs)} ms ping\n{r.TestedAt.LocalDateTime.ToString("ddd, MMM d, h:mm tt", CultureInfo.CurrentCulture)}"
                }
            }).ToList();

            var (bar, hover) = GetBrushes(metric);
            Chart.SetData(points, bar, hover, unit);

            double avg = points.Average(p => p.Value);
            string shown = range >= all.Count ? $"all {all.Count} tests" : $"{points.Count} tests";
            AverageLabel.Text = $"Average {valueFormat(avg)} {unit}  •  {shown}";
        }

        private static (Brush bar, Brush hover) GetBrushes(Metric metric)
        {
            var app = Application.Current;
            return metric switch
            {
                Metric.Upload => ((Brush)app.Resources["AccentTealBrush"], (Brush)app.Resources["AccentCyanBrush"]),
                Metric.Ping => ((Brush)app.Resources["AccentVioletBrush"], (Brush)app.Resources["AccentSkyBrush"]),
                _ => ((Brush)app.Resources["AccentCyanBrush"], (Brush)app.Resources["AccentTealBrush"])
            };
        }

        private void GoTestButton_Click(object sender, RoutedEventArgs e) =>
            App.MainWindowInstance?.NavigateToSpeedTest();
    }
}
