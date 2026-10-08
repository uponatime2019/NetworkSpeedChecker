using System;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using NetworkSpeedChecker.Models;

namespace NetworkSpeedChecker.Services
{
    /// <summary>Builds readable summaries and shares them via the Windows share sheet or clipboard.</summary>
    public class SharingService
    {
        public static string BuildSummary(SpeedTestResult r)
        {
            return
                "Internet speed test results" + Environment.NewLine +
                "────────────────────────────" + Environment.NewLine +
                $"Date:      {r.TestedAt:ddd, MMM d, yyyy  h:mm tt}" + Environment.NewLine +
                $"Download:  {r.DownloadMbps:0.00} Mbps" + Environment.NewLine +
                $"Upload:    {r.UploadMbps:0.00} Mbps" + Environment.NewLine +
                $"Ping:      {r.PingMs:0} ms" + Environment.NewLine +
                $"Jitter:    {r.JitterMs:0.0} ms" + Environment.NewLine +
                $"Server:    {r.ServerName}" + (string.IsNullOrEmpty(r.ServerLocation) ? "" : $" — {r.ServerLocation}") + Environment.NewLine +
                $"Verdict:   {r.QualitySummary}" + Environment.NewLine +
                "Measured with Network Speed Checker (Cloudflare endpoints)";
        }

        /// <summary>Opens the native Windows share sheet. Returns false when sharing is
        /// unavailable (e.g. unpackaged without identity), so callers can fall back.</summary>
        public static bool TryShare(Window window, SpeedTestResult result)
        {
            try
            {
                var manager = DataTransferManager.GetForCurrentView();
                manager.DataRequested += (s, e) =>
                {
                    var text = BuildSummary(result);
                    e.Request.Data.Properties.Title = "Internet speed test results";
                    e.Request.Data.Properties.Description = "Measured with Network Speed Checker";
                    e.Request.Data.SetText(text);
                };
                DataTransferManager.ShowShareUI(new ShareUIOptions
                {
                    Theme = ShareUITheme.Light
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Copies the summary to the clipboard. Returns success.</summary>
        public static bool CopyToClipboard(SpeedTestResult result)
        {
            try
            {
                var package = new DataPackage();
                package.SetText(BuildSummary(result));
                Clipboard.SetContent(package);
                Clipboard.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
