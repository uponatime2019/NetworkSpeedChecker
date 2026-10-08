using System;

namespace NetworkSpeedChecker.Models
{
    /// <summary>A single completed internet speed test measurement.</summary>
    public class SpeedTestResult
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTimeOffset TestedAt { get; set; } = DateTimeOffset.Now;

        /// <summary>Median round-trip latency in milliseconds.</summary>
        public double PingMs { get; set; }

        /// <summary>Latency variation (mean absolute consecutive difference) in milliseconds.</summary>
        public double JitterMs { get; set; }

        /// <summary>Measured download throughput in megabits per second.</summary>
        public double DownloadMbps { get; set; }

        /// <summary>Measured upload throughput in megabits per second.</summary>
        public double UploadMbps { get; set; }

        // --- Extended per-phase detail (0 = not recorded; keeps older history entries valid). ---

        /// <summary>Peak smoothed download rate observed during the phase, Mbps.</summary>
        public double DownloadPeakMbps { get; set; }

        /// <summary>Total download payload transferred, megabytes (SI, bytes / 1e6).</summary>
        public double DownloadDataMb { get; set; }

        /// <summary>Wall-clock duration of the measured download phase, seconds.</summary>
        public double DownloadSeconds { get; set; }

        /// <summary>Peak smoothed upload rate observed during the phase, Mbps.</summary>
        public double UploadPeakMbps { get; set; }

        /// <summary>Total upload payload transferred, megabytes (SI, bytes / 1e6).</summary>
        public double UploadDataMb { get; set; }

        /// <summary>Wall-clock duration of the measured upload phase, seconds.</summary>
        public double UploadSeconds { get; set; }

        /// <summary>Fastest of the latency samples, milliseconds (0 = not recorded).</summary>
        public double MinPingMs { get; set; }

        /// <summary>Slowest of the latency samples, milliseconds (0 = not recorded).</summary>
        public double MaxPingMs { get; set; }

        /// <summary>Human-readable name of the measurement endpoint.</summary>
        public string ServerName { get; set; } = "Cloudflare";

        /// <summary>Cloudflare data center (colo) that served the test.</summary>
        public string ServerLocation { get; set; } = "Automatic";

        /// <summary>Concise network quality summary derived from the numbers.</summary>
        public string QualitySummary
        {
            get
            {
                string video;
                if (DownloadMbps >= 100)
                    video = "easily handles 4K streaming and large downloads";
                else if (DownloadMbps >= 25)
                    video = "comfortably supports 4K streaming on one or two screens";
                else if (DownloadMbps >= 10)
                    video = "is good for HD streaming and everyday browsing";
                else if (DownloadMbps >= 3)
                    video = "is enough for SD streaming and basic browsing";
                else
                    video = "is slow — expect buffering on video streams";

                string gaming;
                if (PingMs <= 20 && JitterMs <= 10)
                    gaming = "latency is excellent for real-time gaming and calls";
                else if (PingMs <= 50)
                    gaming = "latency is good for gaming and video calls";
                else if (PingMs <= 100)
                    gaming = "latency is fair — fine for browsing and streaming";
                else
                    gaming = "latency is high — real-time apps may feel laggy";

                return $"Download speed {video}; {gaming}.";
            }
        }

        public override string ToString() =>
            $"{TestedAt:yyyy-MM-dd HH:mm}  ↓{DownloadMbps:0.00} Mbps  ↑{UploadMbps:0.00} Mbps  {PingMs:0} ms";
    }
}
