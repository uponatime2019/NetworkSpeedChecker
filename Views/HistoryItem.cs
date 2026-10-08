using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using NetworkSpeedChecker.Models;
using NetworkSpeedChecker.Services;

namespace NetworkSpeedChecker.Views
{
    /// <summary>Display wrapper for a stored result shown in the history list.</summary>
    public class HistoryItem
    {
        public SpeedTestResult Result { get; }

        public string DateLabel { get; }
        public string ServerLabel { get; }
        public string DownloadLabel { get; }
        public string UploadLabel { get; }
        public string PingLabel { get; }

        public HistoryItem(SpeedTestResult result)
        {
            Result = result;
            DateLabel = FormatFriendlyDate(result.TestedAt.LocalDateTime);
            ServerLabel = $"{result.ServerName}" +
                (string.IsNullOrEmpty(result.ServerLocation) ? "" : $" • {result.ServerLocation}");
            DownloadLabel = FormatRate(result.DownloadMbps);
            UploadLabel = FormatRate(result.UploadMbps);
            PingLabel = $"{result.PingMs:0} ms";
        }

        private static string FormatFriendlyDate(DateTime d)
        {
            var today = DateTime.Today;
            if (d.Date == today) return $"Today, {d.ToString("h:mm tt", CultureInfo.CurrentCulture)}";
            if (d.Date == today.AddDays(-1)) return $"Yesterday, {d.ToString("h:mm tt", CultureInfo.CurrentCulture)}";
            return d.ToString("MMM d, h:mm tt", CultureInfo.CurrentCulture);
        }

        private static string FormatRate(double mbps) =>
            mbps >= 100 ? $"{mbps:0} Mbps" : $"{mbps:0.00} Mbps";
    }

    /// <summary>Builds the observable collection the ListView binds to.</summary>
    public static class HistoryListBuilder
    {
        public static ObservableCollection<HistoryItem> Build()
        {
            var items = HistoryService.Instance.Current
                .Select(r => new HistoryItem(r));
            return new ObservableCollection<HistoryItem>(items);
        }
    }
}
