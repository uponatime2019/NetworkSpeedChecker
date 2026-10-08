using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using NetworkSpeedChecker.Helpers;
using NetworkSpeedChecker.Models;
using NetworkSpeedChecker.Services;
using Windows.UI;

namespace NetworkSpeedChecker.Views
{
    /// <summary>Home page: one-tap speed test with live gauge, phase state and results.</summary>
    public sealed partial class SpeedTestView : Page
    {
        private const double NarrowWidth = 560;

        public SpeedTestView()
        {
            InitializeComponent();
            SizeChanged += (_, _) => ArrangeResponsive(ActualWidth);

            TestRunController.Instance.Progress += OnTestProgress;
            TestRunController.Instance.Completed += OnTestCompleted;
            TestRunController.Instance.Failed += OnTestFailed;
            TestRunController.Instance.Canceled += OnTestCanceled;

            Unloaded += (_, _) =>
            {
                TestRunController.Instance.Progress -= OnTestProgress;
                TestRunController.Instance.Completed -= OnTestCompleted;
                TestRunController.Instance.Failed -= OnTestFailed;
                TestRunController.Instance.Canceled -= OnTestCanceled;
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            SyncToControllerState();
        }

        private void SyncToControllerState()
        {
            StatusInfoBar.IsOpen = false;

            if (TestRunController.Instance.IsRunning)
            {
                EnterRunningUi();
                var p = TestRunController.Instance.LastProgress;
                if (p != null) ApplyProgress(p);
                return;
            }

            var latest = AppStateService.Instance.LatestResult ?? LatestFromHistory();
            if (latest != null)
                ShowResult(latest);
            else
                EnterIdleUi();
        }

        private static SpeedTestResult? LatestFromHistory()
        {
            var items = HistoryService.Instance.Current;
            return items.Count > 0 ? items[0] : null;
        }

        // ---------------- UI state transitions ----------------

        private void EnterIdleUi()
        {
            IdlePanel.Visibility = Visibility.Visible;
            GaugePanel.Visibility = Visibility.Collapsed;
            ResultsDetailPanel.Visibility = Visibility.Collapsed;
            StartTestButton.IsEnabled = true;
            PingValue.Text = DownloadValue.Text = UploadValue.Text = "—";
            PingHint.Text = "latency";
            DownloadHint.Text = "downlink speed";
            UploadHint.Text = "uplink speed";
            ServerFooter.Text = "Server: Cloudflare • Automatic selection";
        }

        private void EnterRunningUi()
        {
            IdlePanel.Visibility = Visibility.Collapsed;
            GaugePanel.Visibility = Visibility.Visible;
            ResultsDetailPanel.Visibility = Visibility.Collapsed;
            CompletedActions.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Visible;
            OverallProgressBar.IsIndeterminate = false;
            OverallProgressBar.Value = 0;
            StartTestButton.IsEnabled = false;
            Gauge.ShowIdle("STARTING", "—", "", "Connecting to Cloudflare…");
            UploadValue.Text = "—";
            UploadHint.Text = "upload waiting";
        }

        private void ShowResult(SpeedTestResult r)
        {
            IdlePanel.Visibility = Visibility.Collapsed;
            GaugePanel.Visibility = Visibility.Visible;
            ResultsDetailPanel.Visibility = Visibility.Visible;
            CompletedActions.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Collapsed;
            OverallProgressBar.IsIndeterminate = false;
            OverallProgressBar.Value = 100;

            // Quick overview row.
            PingValue.Text = r.PingMs >= 100 ? r.PingMs.ToString("0") : r.PingMs.ToString("0.0");
            PingHint.Text = $"latency • jitter {r.JitterMs:0.0} ms";
            DownloadValue.Text = FormatRate(r.DownloadMbps);
            DownloadHint.Text = "download measured";
            UploadValue.Text = FormatRate(r.UploadMbps);
            UploadHint.Text = "upload measured";

            Gauge.ShowComplete(r.DownloadMbps, "Mbps", $"{r.ServerName} • {r.ServerLocation}");
            PopulateDetailCards(r);
            ServerFooter.Text = $"Server: {r.ServerName}" +
                (string.IsNullOrEmpty(r.ServerLocation) ? "" : $" • {r.ServerLocation}") + " • Automatic selection";
            IdleServerLabel.Text = ServerFooter.Text;
        }

        /// <summary>Fills the separated download / upload / latency / server sections of the results card.</summary>
        private void PopulateDetailCards(SpeedTestResult r)
        {
            ResultsTimestamp.Text = r.TestedAt.ToString("ddd, MMM d • HH:mm");

            // --- Download section ---
            var dl = RatingHelper.RateDownload(r.DownloadMbps);
            ApplyRating(DownloadRatingBadge, DownloadRatingDot, DownloadRatingLabel, dl);
            DownloadDetailValue.Text = FormatRate(r.DownloadMbps);
            DownloadMetaText.Text = r.DownloadSeconds > 0
                ? $"Peak {FormatRate(r.DownloadPeakMbps)} Mbps   •   {r.DownloadDataMb:0.0} MB transferred in {r.DownloadSeconds:0.0} s"
                : "Sustained rate over the whole download phase";
            DownloadVerdictText.Text = RatingHelper.DownloadVerdict(r.DownloadMbps);

            // --- Upload section ---
            var ul = RatingHelper.RateUpload(r.UploadMbps);
            ApplyRating(UploadRatingBadge, UploadRatingDot, UploadRatingLabel, ul);
            UploadDetailValue.Text = FormatRate(r.UploadMbps);
            UploadMetaText.Text = r.UploadSeconds > 0
                ? $"Peak {FormatRate(r.UploadPeakMbps)} Mbps   •   {r.UploadDataMb:0.0} MB transferred in {r.UploadSeconds:0.0} s"
                : "Sustained rate over the whole upload phase";
            UploadVerdictText.Text = RatingHelper.UploadVerdict(r.UploadMbps);

            // --- Latency section ---
            var ping = RatingHelper.RatePing(r.PingMs);
            ApplyRating(PingRatingBadge, PingRatingDot, PingRatingLabel, ping);
            PingDetailValue.Text = r.PingMs >= 100 ? r.PingMs.ToString("0") : r.PingMs.ToString("0.0");
            LatencyMetaText.Text = r.MinPingMs > 0 && r.MaxPingMs > 0
                ? $"Median of 10 probes   •   jitter {r.JitterMs:0.0} ms\nBest {r.MinPingMs:0.0} ms   •   worst {r.MaxPingMs:0.0} ms"
                : $"Median of 10 probes   •   jitter {r.JitterMs:0.0} ms";
            LatencyVerdictText.Text = RatingHelper.LatencyVerdict(r.PingMs, r.JitterMs);

            // --- Server & session section ---
            ServerDetailValue.Text = r.ServerName;
            double totalMb = r.DownloadDataMb + r.UploadDataMb;
            ServerMetaText.Text = $"Edge location {r.ServerLocation}\nTested {r.TestedAt:ddd, MMM d • HH:mm}" +
                (totalMb > 0 ? $"\nTotal data transferred {totalMb:0.0} MB" : "");

            // --- Verdict + suitability chips ---
            VerdictText.Text = r.QualitySummary;
            SuitabilityChips.Children.Clear();
            foreach (var tag in RatingHelper.GetSuitabilityTags(r.DownloadMbps, r.UploadMbps, r.PingMs))
                SuitabilityChips.Children.Add(BuildChip(tag));
        }

        private static void ApplyRating(Border badge, Ellipse dot, TextBlock label, Rating rating)
        {
            var solid = new SolidColorBrush(RatingHelper.ColorFromHex(rating.Hex));
            dot.Fill = solid;
            label.Foreground = solid;
            label.Text = rating.Label;
            badge.Background = new SolidColorBrush(RatingHelper.ColorFromHex(rating.Hex, 0x22));
        }

        private static Border BuildChip(string text)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"]
            };
            return new Border
            {
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(12, 5, 12, 5),
                Background = (Brush)Application.Current.Resources["SubtleChipBrush"],
                BorderBrush = (Brush)Application.Current.Resources["SubtleChipBorderBrush"],
                BorderThickness = new Thickness(1),
                Child = label
            };
        }

        // ---------------- Controller events ----------------

        private void OnTestProgress(SpeedTestProgress p)
        {
            try
            {
                if (!TestRunController.Instance.IsRunning && p.Phase != TestPhase.Complete) return;
                ApplyProgress(p);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SpeedTestView.OnTestProgress");
            }
        }

        private void ApplyProgress(SpeedTestProgress p)
        {
            switch (p.Phase)
            {
                case TestPhase.Connecting:
                    Gauge.ShowIdle("STARTING", "—", "", "Connecting to Cloudflare…");
                    break;

                case TestPhase.Ping:
                    PingHint.Text = "measuring latency…";
                    if (p.PingMs > 0)
                    {
                        PingValue.Text = p.PingMs >= 100 ? p.PingMs.ToString("0") : p.PingMs.ToString("0.0");
                        Gauge.ShowPhase("PING", p.PingMs, "ms", p.OverallPercent, $"Jitter {p.JitterMs:0.0} ms");
                    }
                    else
                    {
                        Gauge.ShowIdle("PING", "…", "ms", "Measuring latency…");
                    }
                    break;

                case TestPhase.Download:
                    PingValue.Text = p.PingMs >= 100 ? p.PingMs.ToString("0") : p.PingMs.ToString("0.0");
                    PingHint.Text = $"latency • jitter {p.JitterMs:0.0} ms";
                    DownloadHint.Text = "measuring…";
                    DownloadValue.Text = FormatRate(p.RateMbps);
                    UploadHint.Text = "upload waiting";
                    Gauge.ShowPhase("DOWNLOAD", p.RateMbps, "Mbps", p.OverallPercent, $"Download test • {p.OverallPercent:0}%");
                    break;

                case TestPhase.Upload:
                    DownloadValue.Text = FormatRate(p.DownloadMbps);
                    DownloadHint.Text = "download measured";
                    UploadHint.Text = "measuring…";
                    UploadValue.Text = FormatRate(p.RateMbps);
                    Gauge.ShowPhase("UPLOAD", p.RateMbps, "Mbps", p.OverallPercent, $"Upload test • {p.OverallPercent:0}%");
                    break;

                case TestPhase.Complete:
                    break;
            }

            OverallProgressBar.Value = p.OverallPercent;
            if (!string.IsNullOrEmpty(p.ServerLabel))
                ServerFooter.Text = $"Server: Cloudflare • {p.ServerLabel}";
        }

        private void OnTestCompleted(SpeedTestResult result)
        {
            try
            {
                ShowResult(result);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SpeedTestView.OnTestCompleted");
            }
        }

        private void OnTestCanceled()
        {
            // A user cancel is not an error: return to the idle state quietly.
            try { EnterIdleUi(); }
            catch (Exception ex) { AppLogger.LogException(ex, "SpeedTestView.OnTestCanceled"); }
        }

        private void OnTestFailed(TestFailureKind kind, string message)
        {
            try
            {
                EnterIdleUi();
                StatusInfoBar.Message = kind switch
                {
                    TestFailureKind.Offline => "No internet connection. Check your network and try again.",
                    TestFailureKind.Timeout => "The test timed out — the connection may be unstable. Try again.",
                    TestFailureKind.Server => "Cloudflare's server could not complete the test. Try again in a moment.",
                    _ => message
                };
                StatusInfoBar.Severity = InfoBarSeverity.Error;
                StatusInfoBar.Title = kind == TestFailureKind.Offline ? "You're offline" : "Test failed";
                StatusInfoBar.IsOpen = true;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SpeedTestView.OnTestFailed");
            }
        }

        // ---------------- Commands ----------------

        private void StartTestButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusInfoBar.IsOpen = false;
                CancelButton.IsEnabled = true;
                EnterRunningUi();
                TestRunController.Instance.Start();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SpeedTestView.StartTestButton_Click");
                OnTestFailed(TestFailureKind.Unknown, "The test could not be started. Please try again.");
            }
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e) => StartTestButton_Click(sender, e);

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CancelButton.IsEnabled = false;
                TestRunController.Instance.Cancel();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SpeedTestView.CancelButton_Click");
            }
        }

        private void ShareButton_Click(object sender, RoutedEventArgs e)
        {
            var result = AppStateService.Instance.LatestResult ?? LatestFromHistory();
            if (result == null) return;

            var window = (App.MainWindowInstance as MainWindow);
            if (window == null || !SharingService.TryShare(window, result))
            {
                // Share sheet unavailable (e.g. unpackaged): clipboard is always available.
                if (SharingService.CopyToClipboard(result))
                    ShowTransientStatus("Share unavailable — results copied to clipboard instead.", InfoBarSeverity.Informational);
                else
                    ShowTransientStatus("Could not share or copy the results.", InfoBarSeverity.Error);
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var result = AppStateService.Instance.LatestResult ?? LatestFromHistory();
            if (result == null) return;

            bool ok = SharingService.CopyToClipboard(result);
            ShowTransientStatus(
                ok ? "Results copied to clipboard." : "Could not access the clipboard.",
                ok ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        }

        private void ShowTransientStatus(string message, InfoBarSeverity severity)
        {
            StatusInfoBar.Title = severity == InfoBarSeverity.Error ? "Copy failed" : "Done";
            StatusInfoBar.Message = message;
            StatusInfoBar.Severity = severity;
            StatusInfoBar.IsOpen = true;
        }

        // ---------------- Responsive layout ----------------

        private void ArrangeResponsive(double width)
        {
            bool narrow = width < NarrowWidth;
            MetricCardsGrid.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            MetricCardsStack.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;

            var metricTarget = narrow ? (System.Collections.Generic.IList<UIElement>)MetricCardsStack.Children
                                      : (System.Collections.Generic.IList<UIElement>)MetricCardsGrid.Children;
            if (!ReferenceEquals(PingCard.Parent, metricTarget))
            {
                MetricCardsGrid.Children.Clear();
                MetricCardsStack.Children.Clear();
                // Preserve order: ping, download, upload.
                metricTarget.Add(PingCard);
                metricTarget.Add(DownloadCard);
                metricTarget.Add(UploadCard);
            }

            // Same treatment for the detail sections: 2×2 grid when wide, vertical stack when narrow.
            DetailCardsGrid.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            DetailCardsStack.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;

            var detailTarget = narrow ? (System.Collections.Generic.IList<UIElement>)DetailCardsStack.Children
                                      : (System.Collections.Generic.IList<UIElement>)DetailCardsGrid.Children;
            if (!ReferenceEquals(DownloadDetailCard.Parent, detailTarget))
            {
                DetailCardsGrid.Children.Clear();
                DetailCardsStack.Children.Clear();
                // Preserve order: download, upload, latency, server.
                detailTarget.Add(DownloadDetailCard);
                detailTarget.Add(UploadDetailCard);
                detailTarget.Add(LatencyDetailCard);
                detailTarget.Add(ServerDetailCard);
            }
        }

        private static string FormatRate(double mbps) =>
            mbps >= 100 ? mbps.ToString("0") : mbps.ToString("0.00");
    }
}
