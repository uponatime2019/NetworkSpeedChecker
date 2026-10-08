using System;
using NetworkSpeedChecker.Models;

namespace NetworkSpeedChecker.Services
{
    /// <summary>Small cross-view state bus: latest result, running flag, shared network status.</summary>
    public class AppStateService
    {
        public static AppStateService Instance { get; } = new();

        public event EventHandler? LatestResultChanged;
        public event EventHandler? TestRunningChanged;

        public HistoryService History => HistoryService.Instance;
        public CloudflareSpeedTestService SpeedTest => new();
        public NetworkStatusService NetworkStatus { get; } = new();

        private SpeedTestResult? _latestResult;
        public SpeedTestResult? LatestResult
        {
            get => _latestResult;
            set
            {
                _latestResult = value;
                LatestResultChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private bool _isTestRunning;
        public bool IsTestRunning
        {
            get => _isTestRunning;
            set
            {
                _isTestRunning = value;
                TestRunningChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
