using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using NetworkSpeedChecker.Helpers;
using NetworkSpeedChecker.Models;
using NetworkSpeedChecker.Services;

namespace NetworkSpeedChecker.Views
{
    /// <summary>Saved tests: newest first, with per-item details, delete, share and clear.</summary>
    public sealed partial class HistoryView : Page
    {
        public ObservableCollection<HistoryItem> Items { get; } = new();

        public HistoryView()
        {
            InitializeComponent();
            HistoryList.ItemsSource = Items;
            HistoryService.Instance.HistoryChanged += OnHistoryChanged;
            Unloaded += (_, _) => HistoryService.Instance.HistoryChanged -= OnHistoryChanged;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Refresh();
        }

        private void OnHistoryChanged(object? sender, EventArgs e) =>
            DispatcherQueue.TryEnqueue(Refresh);

        private void Refresh()
        {
            Items.Clear();
            foreach (var item in HistoryListBuilder.Build())
                Items.Add(item);

            bool hasAny = Items.Count > 0;
            EmptyState.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;
            LatestCard.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
            ListPanel.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
            SubtitleText.Text = hasAny
                ? $"{Items.Count} test{(Items.Count == 1 ? "" : "s")} saved on this device"
                : "Every completed test is saved on this device";

            if (hasAny)
            {
                var latest = Items[0].Result;
                LatestDownload.Text = FormatRate(latest.DownloadMbps);
                LatestUpload.Text = FormatRate(latest.UploadMbps);
                LatestMeta.Text =
                    $"{Items[0].DateLabel} • Ping {latest.PingMs:0} ms • Jitter {latest.JitterMs:0.0} ms • {latest.ServerName}" +
                    (string.IsNullOrEmpty(latest.ServerLocation) ? "" : $" ({latest.ServerLocation})");
            }
        }

        private SpeedTestResult? LatestResult =>
            Items.Count > 0 ? Items[0].Result : null;

        // ---------------- Item interactions ----------------

        private async void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not HistoryItem item) return;
            await ShowDetailsDialog(item.Result);
        }

        private async System.Threading.Tasks.Task ShowDetailsDialog(SpeedTestResult r)
        {
            var dialog = new ContentDialog
            {
                Title = "Test result",
                XamlRoot = XamlRoot,
                DefaultButton = ContentDialogButton.Close,
                CloseButtonText = "Close",
                Content = new Border
                {
                    Padding = new Thickness(4, 8, 4, 0),
                    Child = BuildDetailsPanel(r)
                }
            };
            await dialog.ShowAsync();
        }

        private static StackPanel BuildDetailsPanel(SpeedTestResult r)
        {
            var panel = new StackPanel { Spacing = 12, MinWidth = 340, MaxWidth = 460 };

            panel.Children.Add(MakeSection("DOWNLOAD", "", "#06B6D4",
                FormatRate(r.DownloadMbps), RatingHelper.RateDownload(r.DownloadMbps),
                r.DownloadSeconds > 0
                    ? $"Peak {FormatRate(r.DownloadPeakMbps)}  •  {r.DownloadDataMb:0.0} MB in {r.DownloadSeconds:0.0} s"
                    : null,
                RatingHelper.DownloadVerdict(r.DownloadMbps)));

            panel.Children.Add(MakeSection("UPLOAD", "", "#14B8A6",
                FormatRate(r.UploadMbps), RatingHelper.RateUpload(r.UploadMbps),
                r.UploadSeconds > 0
                    ? $"Peak {FormatRate(r.UploadPeakMbps)}  •  {r.UploadDataMb:0.0} MB in {r.UploadSeconds:0.0} s"
                    : null,
                RatingHelper.UploadVerdict(r.UploadMbps)));

            panel.Children.Add(MakeSection("LATENCY", "", "#7C8CF8",
                $"{r.PingMs:0.0} ms", RatingHelper.RatePing(r.PingMs),
                r.MinPingMs > 0 && r.MaxPingMs > 0
                    ? $"Jitter {r.JitterMs:0.0} ms  •  best {r.MinPingMs:0.0} / worst {r.MaxPingMs:0.0} ms"
                    : $"Jitter {r.JitterMs:0.0} ms",
                RatingHelper.LatencyVerdict(r.PingMs, r.JitterMs)));

            panel.Children.Add(MakeSection("SERVER", "", "#38BDF8",
                r.ServerName, null,
                (string.IsNullOrEmpty(r.ServerLocation) ? "" : $"Edge {r.ServerLocation}\n") +
                $"Tested {r.TestedAt:ddd, MMM d, yyyy  h:mm tt}",
                null));

            var summary = new TextBlock
            {
                Text = r.QualitySummary,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.85,
                Margin = new Thickness(0, 4, 0, 0)
            };
            var verdict = new StackPanel { Spacing = 2 };
            var verdictLabel = new TextBlock { Text = "Verdict", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = 0.7 };
            verdict.Children.Add(verdictLabel);
            verdict.Children.Add(summary);

            panel.Children.Add(new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x33, 0x88, 0x99, 0xAA)),
                Padding = new Thickness(0, 10, 0, 0),
                Child = verdict
            });
            return panel;
        }

        /// <summary>One separated result section: icon + caption + rating on the left, the value on the right, detail lines below.</summary>
        private static StackPanel MakeSection(string caption, string glyph, string accentHex, string value,
            Rating? rating, string? meta, string? verdict)
        {
            var accent = new SolidColorBrush(Helpers.RatingHelper.ColorFromHex(accentHex));

            var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = accent });
            left.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = accent
            });
            if (rating.HasValue)
            {
                left.Children.Add(new TextBlock { Text = "·", FontSize = 14, Opacity = 0.45 });
                left.Children.Add(new TextBlock
                {
                    Text = rating.Value.Label,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Helpers.RatingHelper.ColorFromHex(rating.Value.Hex))
                });
            }

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(left, 0);
            header.Children.Add(left);
            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            };
            Grid.SetColumn(valueText, 1);
            header.Children.Add(valueText);

            var section = new StackPanel { Spacing = 3 };
            section.Children.Add(header);
            if (!string.IsNullOrEmpty(meta))
                section.Children.Add(new TextBlock
                {
                    Text = meta,
                    FontSize = 14,
                    Opacity = 0.75,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20, 0, 0, 0)
                });
            if (!string.IsNullOrEmpty(verdict))
                section.Children.Add(new TextBlock
                {
                    Text = verdict,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20, 0, 0, 0)
                });
            return section;
        }

        private async void DeleteItemButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not HistoryItem item) return;

            var dialog = new ContentDialog
            {
                Title = "Delete this result?",
                Content = $"The test from {item.DateLabel} will be permanently removed.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await HistoryService.Instance.DeleteAsync(item.Result.Id);
        }

        private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Clear all history?",
                Content = "Every saved test on this device will be permanently removed. This cannot be undone.",
                PrimaryButtonText = "Clear all",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await HistoryService.Instance.ClearAsync();
        }

        // ---------------- Header actions ----------------

        private void ShareLatestButton_Click(object sender, RoutedEventArgs e)
        {
            var result = LatestResult;
            if (result == null) return;

            var window = App.MainWindowInstance;
            if (window == null || !SharingService.TryShare(window, result))
            {
                if (SharingService.CopyToClipboard(result))
                    ShowTransientTeaching("Share unavailable — latest result copied to clipboard.");
                else
                    ShowTransientTeaching("Could not share or copy the latest result.");
            }
        }

        private void CopyLatestButton_Click(object sender, RoutedEventArgs e)
        {
            var result = LatestResult;
            if (result == null) return;

            ShowTransientTeaching(SharingService.CopyToClipboard(result)
                ? "Latest result copied to clipboard."
                : "Could not access the clipboard.");
        }

        private async void ShowTransientTeaching(string message)
        {
            var dialog = new ContentDialog
            {
                Title = message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void GoTestButton_Click(object sender, RoutedEventArgs e) =>
            App.MainWindowInstance?.NavigateToSpeedTest();

        private static string FormatRate(double mbps) =>
            mbps >= 100 ? $"{mbps:0} Mbps" : $"{mbps:0.00} Mbps";
    }
}
