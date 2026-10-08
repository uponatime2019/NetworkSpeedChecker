using System;
using System.Collections.Generic;

namespace NetworkSpeedChecker.Helpers
{
    /// <summary>A qualitative rating for one metric: label plus accent color.</summary>
    public readonly struct Rating
    {
        public string Label { get; }
        public string Hex { get; }

        public Rating(string label, string hex)
        {
            Label = label;
            Hex = hex;
        }
    }

    /// <summary>Maps measured numbers to human-friendly quality labels shared by the result view, history details and share text.</summary>
    public static class RatingHelper
    {
        private const string Green = "#16A34A";   // excellent
        private const string Cyan = "#0891B2";    // very good (brand teal)
        private const string Blue = "#2563EB";    // good
        private const string Amber = "#D97706";   // fair
        private const string Red = "#DC2626";     // poor

        public static Rating RateDownload(double mbps) => mbps switch
        {
            >= 200 => new Rating("Excellent", Green),
            >= 100 => new Rating("Very good", Cyan),
            >= 25 => new Rating("Good", Blue),
            >= 10 => new Rating("Fair", Amber),
            _ => new Rating("Poor", Red)
        };

        public static Rating RateUpload(double mbps) => mbps switch
        {
            >= 100 => new Rating("Excellent", Green),
            >= 40 => new Rating("Very good", Cyan),
            >= 10 => new Rating("Good", Blue),
            >= 3 => new Rating("Fair", Amber),
            _ => new Rating("Poor", Red)
        };

        public static Rating RatePing(double ms) => ms switch
        {
            <= 20 => new Rating("Excellent", Green),
            <= 50 => new Rating("Good", Blue),
            <= 100 => new Rating("Fair", Amber),
            _ => new Rating("Poor", Red)
        };

        /// <summary>Connection use-cases the numbers comfortably support, shown as chips under the verdict.</summary>
        public static List<string> GetSuitabilityTags(double downloadMbps, double uploadMbps, double pingMs)
        {
            var tags = new List<string>();
            if (downloadMbps >= 100) tags.Add("4K streaming");
            else if (downloadMbps >= 25) tags.Add("4K on one screen");
            else if (downloadMbps >= 10) tags.Add("HD streaming");

            if (downloadMbps >= 50) tags.Add("Large downloads");
            if (uploadMbps >= 5 && pingMs <= 100) tags.Add("Video calls");
            else if (uploadMbps >= 1.5 && pingMs <= 150) tags.Add("Voice calls");
            if (pingMs <= 60) tags.Add("Online gaming");
            if (uploadMbps >= 10) tags.Add("Cloud backup");
            return tags;
        }

        /// <summary>What this download rate means in practice, shown under the download section.</summary>
        public static string DownloadVerdict(double mbps) => mbps switch
        {
            >= 200 => "Far exceeds the needs of 4K streaming, large downloads and shared homes.",
            >= 100 => "Easily handles 4K streaming and large downloads.",
            >= 25 => "Comfortably supports 4K streaming on one or two screens.",
            >= 10 => "Good for HD streaming and everyday browsing.",
            >= 3 => "Enough for SD streaming and basic browsing.",
            _ => "Slow — expect buffering on video streams."
        };

        /// <summary>What this upload rate means in practice, shown under the upload section.</summary>
        public static string UploadVerdict(double mbps) => mbps switch
        {
            >= 40 => "Excellent for video calls, live streams and cloud backups.",
            >= 10 => "Good for video calls, screen sharing and file uploads.",
            >= 3 => "Handles calls and everyday uploads; large files take longer.",
            _ => "Slow — posting photos and backups will take a while."
        };

        /// <summary>What this latency means in practice, shown under the latency section.</summary>
        public static string LatencyVerdict(double pingMs, double jitterMs) => pingMs switch
        {
            <= 20 => jitterMs <= 10
                ? "Extremely responsive and stable — ideal for competitive gaming."
                : "Extremely responsive, though jitter spikes may occur.",
            <= 50 => "Responsive enough for gaming and real-time calls.",
            <= 100 => "Fine for browsing, streaming and most calls.",
            _ => "High latency — real-time apps may feel laggy."
        };

        /// <summary>Parses a #RRGGBB string into a color with the given alpha.</summary>
        public static Windows.UI.Color ColorFromHex(string hex, byte alpha = 0xFF)
        {
            return Microsoft.UI.ColorHelper.FromArgb(alpha,
                Convert.ToByte(hex.Substring(1, 2), 16),
                Convert.ToByte(hex.Substring(3, 2), 16),
                Convert.ToByte(hex.Substring(5, 2), 16));
        }
    }
}
