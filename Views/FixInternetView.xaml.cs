using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NetworkSpeedChecker.Services;
using Windows.System;

namespace NetworkSpeedChecker.Views
{
    /// <summary>Fix Internet page: Network diagnostics, speedup optimizations, and Wi-Fi 2.4 vs 5 GHz guide.</summary>
    public sealed partial class FixInternetView : Page
    {
        public ObservableCollection<DiagnosticItem> Items { get; } = new();

        public FixInternetView()
        {
            InitializeComponent();
            DiagnosticList.ItemsSource = Items;
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ScanButton.IsEnabled = false;
                ScanningPanel.Visibility = Visibility.Visible;
                ResultsPanel.Visibility = Visibility.Collapsed;
                Items.Clear();

                var report = await NetworkFixService.RunDiagnosticsAsync(step =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ScanningStepText.Text = step;
                    });
                });

                ScanningPanel.Visibility = Visibility.Collapsed;
                ResultsPanel.Visibility = Visibility.Visible;
                ScanButton.IsEnabled = true;

                foreach (var item in report.Items)
                {
                    Items.Add(item);
                }

                StatusBannerText.Text = report.OverallSummary;
                if (!report.IsConnected)
                {
                    StatusBanner.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x22, 0xDC, 0x26, 0x26));
                    StatusBanner.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x44, 0xDC, 0x26, 0x26));
                    StatusBannerIcon.Glyph = "\uE711";
                    StatusBannerIcon.Foreground = (Brush)Application.Current.Resources["BadBrush"];
                }
                else if (report.Needs5GhzRecommendation)
                {
                    StatusBanner.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x22, 0xD9, 0x77, 0x06));
                    StatusBanner.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x44, 0xD9, 0x77, 0x06));
                    StatusBannerIcon.Glyph = "\uE7BA";
                    StatusBannerIcon.Foreground = (Brush)Application.Current.Resources["AccentOrangeBrush"];
                }
                else
                {
                    StatusBanner.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x22, 0x14, 0xB8, 0xA6));
                    StatusBanner.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x44, 0x14, 0xB8, 0xA6));
                    StatusBannerIcon.Glyph = "\uE73E";
                    StatusBannerIcon.Foreground = (Brush)Application.Current.Resources["GoodBrush"];
                }
            }
            catch (Exception ex)
            {
                ScanningPanel.Visibility = Visibility.Collapsed;
                ScanButton.IsEnabled = true;
                Helpers.AppLogger.LogException(ex, "FixInternetView.ScanButton_Click");
            }
        }

        private async void OpenWifiSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Launcher.LaunchUriAsync(new Uri("ms-settings:network-wifi"));
            }
            catch (Exception ex)
            {
                Helpers.AppLogger.LogException(ex, "FixInternetView.OpenWifiSettingsButton_Click");
            }
        }
    }
}
