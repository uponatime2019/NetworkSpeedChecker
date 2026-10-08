using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetworkSpeedChecker.Helpers;
using NetworkSpeedChecker.Models;

namespace NetworkSpeedChecker.Services
{
    public enum TestPhase
    {
        Idle,
        Connecting,
        Ping,
        Download,
        Upload,
        Complete
    }

    public enum TestFailureKind
    {
        Offline,
        Timeout,
        Server,
        Canceled,
        Unknown
    }

    /// <summary>User-facing test failure with a classification used to pick a helpful message.</summary>
    public class SpeedTestException : Exception
    {
        public TestFailureKind Kind { get; }

        public SpeedTestException(TestFailureKind kind, string message, Exception? inner = null)
            : base(message, inner)
        {
            Kind = kind;
        }
    }

    /// <summary>Live progress payload pushed to the UI during a test.</summary>
    public class SpeedTestProgress
    {
        public TestPhase Phase { get; set; }
        public string PhaseLabel { get; set; } = "";
        /// <summary>Current smoothed transfer rate in Mbps (download or upload phase).</summary>
        public double RateMbps { get; set; }
        /// <summary>Overall test progress 0..100 across all phases.</summary>
        public double OverallPercent { get; set; }
        public double PingMs { get; set; }
        public double JitterMs { get; set; }
        public double DownloadMbps { get; set; }
        public string ServerLabel { get; set; } = "Cloudflare";
    }

    /// <summary>Per-phase detail captured while measuring a transfer.</summary>
    public class TransferDetail
    {
        public double Mbps { get; set; }
        /// <summary>Peak smoothed rate seen during the measured window, Mbps.</summary>
        public double PeakMbps { get; set; }
        /// <summary>Payload bytes counted inside the measured window.</summary>
        public long Bytes { get; set; }
        /// <summary>Wall-clock seconds of the measured window.</summary>
        public double Seconds { get; set; }
    }

    /// <summary>
    /// Measures ping, download and upload throughput against Cloudflare's public speed-test
    /// endpoints (https://speed.cloudflare.com). All numbers come from real network transfers.
    /// </summary>
    public class CloudflareSpeedTestService
    {
        private const string BaseUrl = "https://speed.cloudflare.com";
        private const int BufferSize = 64 * 1024;

        // Phase weighting for the overall progress bar.
        private const double PingEnd = 12.0;
        private const double DownloadEnd = 60.0;
        private const double UploadEnd = 100.0;

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.None,
                UseProxy = false,
                AllowAutoRedirect = true
            };
            var client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = Timeout.InfiniteTimeSpan // per-stage timeouts via tokens
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NetworkSpeedChecker/1.0 (Windows; WinUI 3)");
            client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            return client;
        }

        /// <summary>
        /// Fetches the serving data center via /cdn-cgi/trace (reliable key=value response);
        /// doubles as the connectivity probe before any measurement starts.
        /// </summary>
        public async Task<ServerInfo> GetServerInfoAsync(CancellationToken ct)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                using var resp = await Http.GetAsync($"{BaseUrl}/cdn-cgi/trace", cts.Token).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                var body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                string colo = "";
                string loc = "";
                foreach (var rawLine in body.Split('\n'))
                {
                    var line = rawLine.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line[..eq];
                    var value = line[(eq + 1)..];
                    if (key == "colo") colo = value;
                    else if (key == "loc") loc = value;
                }

                var parts = new[] { colo, loc }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                return new ServerInfo
                {
                    Name = "Cloudflare",
                    Location = parts.Count > 0 ? string.Join(" · ", parts) : "Automatic selection",
                    Colo = colo
                };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new SpeedTestException(TestFailureKind.Timeout, "Could not reach Cloudflare in time.");
            }
            catch (Exception ex)
            {
                throw new SpeedTestException(TestFailureKind.Offline, "No internet connection detected.", ex);
            }
        }

        /// <summary>Runs the full sequence: server check → ping → download → upload.</summary>
        public async Task<SpeedTestResult> RunTestAsync(IProgress<SpeedTestProgress> progress, CancellationToken ct)
        {
            var state = new SpeedTestProgress { Phase = TestPhase.Connecting, PhaseLabel = "Connecting to Cloudflare…" };
            Report(progress, state, 0);

            var server = await GetServerInfoAsync(ct).ConfigureAwait(false);
            state.ServerLabel = server.Location != "Automatic selection" ? $"Cloudflare · {server.Location}" : "Cloudflare";

            // ---------- Ping ----------
            state.Phase = TestPhase.Ping;
            state.PhaseLabel = "Measuring ping…";
            Report(progress, state, 2);
            var (ping, jitter, minPing, maxPing) = await MeasurePingAsync(10, ct).ConfigureAwait(false);
            state.PingMs = ping;
            state.JitterMs = jitter;

            // ---------- Download ----------
            state.Phase = TestPhase.Download;
            state.PhaseLabel = "Testing download…";
            state.OverallPercent = PingEnd;
            Report(progress, state, PingEnd);
            var download = await MeasureDownloadAsync(state, progress, ct).ConfigureAwait(false);
            state.DownloadMbps = download.Mbps;

            // ---------- Upload ----------
            state.Phase = TestPhase.Upload;
            state.PhaseLabel = "Testing upload…";
            state.RateMbps = 0;
            Report(progress, state, DownloadEnd);
            var upload = await MeasureUploadAsync(state, progress, ct).ConfigureAwait(false);

            state.Phase = TestPhase.Complete;
            state.PhaseLabel = "Test complete";
            state.RateMbps = 0;
            state.OverallPercent = 100;
            Report(progress, state, 100);

            return new SpeedTestResult
            {
                TestedAt = DateTimeOffset.Now,
                PingMs = ping,
                JitterMs = jitter,
                DownloadMbps = download.Mbps,
                UploadMbps = upload.Mbps,
                DownloadPeakMbps = download.PeakMbps,
                DownloadDataMb = download.Bytes / 1e6,
                DownloadSeconds = download.Seconds,
                UploadPeakMbps = upload.PeakMbps,
                UploadDataMb = upload.Bytes / 1e6,
                UploadSeconds = upload.Seconds,
                MinPingMs = minPing,
                MaxPingMs = maxPing,
                ServerName = server.Name,
                ServerLocation = server.Location
            };
        }

        private async Task<(double median, double jitter, double min, double max)> MeasurePingAsync(int samples, CancellationToken ct)
        {
            var times = new List<double>();
            // Warm-up request opens the connection so measured samples are not skewed by TLS setup.
            await TimedPingAsync(ct).ConfigureAwait(false);
            for (int i = 0; i < samples; i++)
            {
                ct.ThrowIfCancellationRequested();
                times.Add(await TimedPingAsync(ct).ConfigureAwait(false));
            }
            if (times.Count < 3)
                throw new SpeedTestException(TestFailureKind.Server, "Server did not respond to latency probes.");

            var sorted = times.OrderBy(t => t).ToList();
            double median = sorted[sorted.Count / 2];
            double jitter = 0;
            for (int i = 1; i < times.Count; i++)
                jitter += Math.Abs(times[i] - times[i - 1]);
            jitter /= times.Count - 1;
            return (median, jitter, sorted[0], sorted[^1]);
        }

        private async Task<double> TimedPingAsync(CancellationToken ct)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var url = $"{BaseUrl}/__down?bytes=0&r={Guid.NewGuid():N}";
                var sw = Stopwatch.StartNew();
                using var resp = await Http.GetAsync(url, cts.Token).ConfigureAwait(false);
                await resp.Content.LoadIntoBufferAsync(0).ConfigureAwait(false);
                sw.Stop();
                if (!resp.IsSuccessStatusCode)
                    throw new SpeedTestException(TestFailureKind.Server, $"Server returned {(int)resp.StatusCode}.");
                return sw.Elapsed.TotalMilliseconds;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new SpeedTestException(TestFailureKind.Timeout, "Latency probe timed out.");
            }
            catch (SpeedTestException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                throw new SpeedTestException(TestFailureKind.Offline, "Connection lost while measuring ping.", ex);
            }
        }

        private async Task<TransferDetail> MeasureDownloadAsync(SpeedTestProgress state, IProgress<SpeedTestProgress> progress, CancellationToken ct)
        {
            const double targetSeconds = 8.0;
            // Cloudflare's __down caps at 99,999,999 bytes per request; stay below it.
            var sizes = new Queue<long>(new[] { 5L * 1024 * 1024, 10L * 1024 * 1024, 25L * 1024 * 1024, 50L * 1024 * 1024, 90L * 1024 * 1024 });

            // Warm-up transfer is excluded from the measurement window.
            await DownloadOnceAsync(1024 * 1024, (_, _) => { }, ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            long measuredBytes = 0;
            var sw = Stopwatch.StartNew();
            var windowStart = 0.0;
            long windowBytes = 0;
            double ema = 0;
            double peak = 0;
            var lastReport = TimeSpan.Zero;

            while (sw.Elapsed.TotalSeconds < targetSeconds && sizes.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var size = sizes.Dequeue();
                long stageBytes = 0;
                try
                {
                    await DownloadOnceAsync(size, (read, elapsedSeconds) =>
                    {
                        long total = measuredBytes + read;
                        long delta = total - (measuredBytes + stageBytes);
                        stageBytes = read;
                        windowBytes += delta;

                        var now = sw.Elapsed.TotalSeconds;
                        // Instantaneous rate over a rolling ~1.5 s window, smoothed with an EMA.
                        if (now - windowStart >= 1.5)
                        {
                            double inst = windowBytes * 8 / (now - windowStart) / 1e6;
                            ema = ema == 0 ? inst : ema * 0.6 + inst * 0.4;
                            peak = Math.Max(peak, ema);
                            windowStart = now;
                            windowBytes = 0;
                        }
                        state.RateMbps = ema != 0 ? ema : total * 8 / Math.Max(now, 0.001) / 1e6;

                        if (sw.Elapsed - lastReport >= TimeSpan.FromMilliseconds(120))
                        {
                            lastReport = sw.Elapsed;
                            double frac = Math.Min(0.95, now / targetSeconds);
                            state.OverallPercent = PingEnd + (DownloadEnd - PingEnd) * frac;
                            Report(progress, state, state.OverallPercent);
                        }
                    }, ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch (SpeedTestException ex) when (measuredBytes >= 2L * 1024 * 1024)
                {
                    // Already measured plenty: finish early instead of failing the test.
                    AppLogger.Log($"Download stage ({size / 1024 / 1024} MB) failed after {measuredBytes / 1024 / 1024} MB measured; finishing early. {ex.Message}");
                    break;
                }
                measuredBytes += stageBytes;
            }

            sw.Stop();
            if (measuredBytes < 256 * 1024)
                throw new SpeedTestException(TestFailureKind.Server, "Not enough data received to measure download speed.");

            double seconds = sw.Elapsed.TotalSeconds;
            double mbps = measuredBytes * 8 / seconds / 1e6;
            return new TransferDetail { Mbps = mbps, PeakMbps = Math.Max(peak, mbps), Bytes = measuredBytes, Seconds = seconds };
        }

        private async Task DownloadOnceAsync(long bytes, Action<long, double> onChunk, CancellationToken ct, TimeSpan timeout)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);
                var url = $"{BaseUrl}/__down?bytes={bytes}&r={Guid.NewGuid():N}";
                var sw = Stopwatch.StartNew();
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);

                var buffer = new byte[BufferSize];
                long totalRead = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).ConfigureAwait(false)) > 0)
                {
                    totalRead += read;
                    onChunk?.Invoke(totalRead, sw.Elapsed.TotalSeconds);
                }
                if (totalRead == 0)
                    throw new SpeedTestException(TestFailureKind.Server, "Server returned an empty download stream.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new SpeedTestException(TestFailureKind.Timeout, "Download timed out. The connection may be unstable.");
            }
            catch (HttpRequestException ex)
            {
                throw ClassifyHttpException("download", ex);
            }
            catch (SpeedTestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SpeedTestException(TestFailureKind.Unknown, "Download measurement failed.", ex);
            }
        }

        private async Task<TransferDetail> MeasureUploadAsync(SpeedTestProgress state, IProgress<SpeedTestProgress> progress, CancellationToken ct)
        {
            const double targetSeconds = 6.0;
            var sizes = new Queue<int>(new[] { 5 * 1024 * 1024, 10 * 1024 * 1024, 25 * 1024 * 1024, 50 * 1024 * 1024, 50 * 1024 * 1024 });

            var rnd = new Random(0x5C11);
            await UploadOnceAsync(1024 * 1024, rnd, _ => { }, ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            long measuredBytes = 0;
            var sw = Stopwatch.StartNew();
            var windowStart = 0.0;
            long windowBytes = 0;
            double ema = 0;
            double peak = 0;
            var lastReport = TimeSpan.Zero;

            while (sw.Elapsed.TotalSeconds < targetSeconds && sizes.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var size = sizes.Dequeue();
                int stageSent = 0;
                try
                {
                    await UploadOnceAsync(size, rnd, sent =>
                    {
                        long total = measuredBytes + sent;
                        long delta = total - (measuredBytes + stageSent);
                        stageSent = sent;
                        windowBytes += delta;

                        var now = sw.Elapsed.TotalSeconds;
                        if (now - windowStart >= 1.2)
                        {
                            double inst = windowBytes * 8 / (now - windowStart) / 1e6;
                            ema = ema == 0 ? inst : ema * 0.6 + inst * 0.4;
                            peak = Math.Max(peak, ema);
                            windowStart = now;
                            windowBytes = 0;
                        }
                        state.RateMbps = ema != 0 ? ema : total * 8 / Math.Max(now, 0.001) / 1e6;

                        if (sw.Elapsed - lastReport >= TimeSpan.FromMilliseconds(120))
                        {
                            lastReport = sw.Elapsed;
                            double frac = Math.Min(0.95, now / targetSeconds);
                            state.OverallPercent = DownloadEnd + (UploadEnd - DownloadEnd) * frac;
                            Report(progress, state, state.OverallPercent);
                        }
                    }, ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch (SpeedTestException ex) when (measuredBytes >= 512L * 1024)
                {
                    // Already measured enough: finish early instead of failing the test.
                    AppLogger.Log($"Upload stage ({size / 1024 / 1024} MB) failed after {measuredBytes / 1024 / 1024} MB sent; finishing early. {ex.Message}");
                    break;
                }
                measuredBytes += stageSent;
            }

            sw.Stop();
            if (measuredBytes < 128 * 1024)
                throw new SpeedTestException(TestFailureKind.Server, "Not enough data sent to measure upload speed.");

            double seconds = sw.Elapsed.TotalSeconds;
            double mbps = measuredBytes * 8 / seconds / 1e6;
            return new TransferDetail { Mbps = mbps, PeakMbps = Math.Max(peak, mbps), Bytes = measuredBytes, Seconds = seconds };
        }

        private async Task UploadOnceAsync(int bytes, Random rnd, Action<int> onSent, CancellationToken ct, TimeSpan timeout)
        {
            try
            {
                var payload = new byte[bytes];
                rnd.NextBytes(payload);
                var content = new ProgressableByteArrayContent(payload, (written, _) => onSent?.Invoke((int)written));

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/__up?r={Guid.NewGuid():N}") { Content = content };
                using var resp = await Http.SendAsync(req, cts.Token).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                // Drain the (tiny) response body.
                await resp.Content.LoadIntoBufferAsync(0).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new SpeedTestException(TestFailureKind.Timeout, "Upload timed out. The connection may be unstable.");
            }
            catch (HttpRequestException ex)
            {
                throw ClassifyHttpException("upload", ex);
            }
            catch (Exception ex) when (ex is not SpeedTestException)
            {
                throw new SpeedTestException(TestFailureKind.Unknown, "Upload measurement failed.", ex);
            }
        }

        private static SpeedTestException ClassifyHttpException(string phase, HttpRequestException ex)
        {
            if (ex.InnerException is SocketException se)
            {
                return se.SocketErrorCode switch
                {
                    SocketError.HostUnreachable or SocketError.NetworkUnreachable or
                    SocketError.ConnectionRefused or SocketError.TimedOut =>
                        new SpeedTestException(TestFailureKind.Offline, $"Connection lost during {phase} test.", ex),
                    _ => new SpeedTestException(TestFailureKind.Server, $"Server error during {phase} test.", ex)
                };
            }
            return new SpeedTestException(TestFailureKind.Server, $"Server error during {phase} test.", ex);
        }

        private static void Report(IProgress<SpeedTestProgress> progress, SpeedTestProgress state, double percent)
        {
            state.OverallPercent = Math.Max(0, Math.Min(100, percent));
            progress?.Report(state);
        }
    }

    /// <summary>Metadata about the endpoint serving the test.</summary>
    public class ServerInfo
    {
        public string Name { get; set; } = "Cloudflare";
        public string Location { get; set; } = "Automatic selection";
        public string Colo { get; set; } = "";
    }

    /// <summary>
    /// HTTP content that reports how many payload bytes have actually been handed to the
    /// network stack, giving real upload progress instead of a fabricated value.
    /// </summary>
    public class ProgressableByteArrayContent : ByteArrayContent
    {
        private readonly byte[] _payload;
        private readonly Action<long, long> _progress;

        public ProgressableByteArrayContent(byte[] payload, Action<long, long> progress) : base(payload)
        {
            _payload = payload;
            _progress = progress;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            const int chunk = 64 * 1024;
            for (long offset = 0; offset < _payload.Length; offset += chunk)
            {
                int len = (int)Math.Min(chunk, _payload.Length - offset);
                await stream.WriteAsync(new ReadOnlyMemory<byte>(_payload, (int)offset, len)).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                _progress?.Invoke(offset + len, _payload.Length);
            }
        }
    }
}
