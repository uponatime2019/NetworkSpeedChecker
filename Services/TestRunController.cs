using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using NetworkSpeedChecker.Helpers;
using NetworkSpeedChecker.Models;

namespace NetworkSpeedChecker.Services
{
    /// <summary>
    /// Owns the running speed test so it survives page navigation. The measurement runs
    /// on the thread pool; every event is raised on the UI thread via the DispatcherQueue
    /// captured when Start() is called, so subscribers may touch XAML directly.
    /// Results are persisted to history as soon as they complete.
    /// </summary>
    public class TestRunController
    {
        public static TestRunController Instance { get; } = new();

        public event Action<SpeedTestProgress>? Progress;
        public event Action<SpeedTestResult>? Completed;
        public event Action<TestFailureKind, string>? Failed;
        public event Action? Canceled;

        public bool IsRunning { get; private set; }
        public SpeedTestProgress? LastProgress { get; private set; }

        private readonly CloudflareSpeedTestService _service = new();
        private CancellationTokenSource? _cts;
        private readonly object _startLock = new();

        public void Start()
        {
            lock (_startLock)
            {
                if (IsRunning) return;
                IsRunning = true;
            }
            AppStateService.Instance.IsTestRunning = true;
            LastProgress = null;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Captured on the UI thread (Start is invoked from a click handler).
            var dispatcher = DispatcherQueue.GetForCurrentThread();

            IProgress<SpeedTestProgress> progress = new Progress<SpeedTestProgress>(p =>
            {
                LastProgress = p;
                Progress?.Invoke(p);
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _service.RunTestAsync(progress, token);
                    await HistoryService.Instance.AddAsync(result);
                    AppStateService.Instance.LatestResult = result;

                    dispatcher.TryEnqueue(() =>
                    {
                        Progress?.Invoke(new SpeedTestProgress
                        {
                            Phase = TestPhase.Complete,
                            PhaseLabel = "Test complete",
                            OverallPercent = 100,
                            PingMs = result.PingMs,
                            JitterMs = result.JitterMs,
                            DownloadMbps = result.DownloadMbps,
                            RateMbps = result.DownloadMbps,
                            ServerLabel = result.ServerLocation
                        });
                        Completed?.Invoke(result);
                    });
                }
                catch (OperationCanceledException)
                {
                    dispatcher.TryEnqueue(() => Canceled?.Invoke());
                }
                catch (SpeedTestException ex)
                {
                    AppLogger.Log($"Test failed ({ex.Kind}): {ex.Message}");
                    dispatcher.TryEnqueue(() => Failed?.Invoke(ex.Kind, ex.Message));
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, "TestRunController.RunAsync");
                    dispatcher.TryEnqueue(() => Failed?.Invoke(
                        TestFailureKind.Unknown, "The test could not be completed. Please try again."));
                }
                finally
                {
                    lock (_startLock) IsRunning = false;
                    AppStateService.Instance.IsTestRunning = false;
                }
            });
        }

        public void Cancel() => _cts?.Cancel();
    }
}
