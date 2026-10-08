using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using NetworkSpeedChecker.Helpers;
using NetworkSpeedChecker.Models;

namespace NetworkSpeedChecker.Services
{
    /// <summary>Persists completed speed tests on-device as JSON, newest first.</summary>
    public class HistoryService
    {
        private static HistoryService? _instance;
        public static HistoryService Instance => _instance ??= new HistoryService();

        private readonly string _historyPath;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private List<SpeedTestResult> _cache = new();

        private static readonly JsonSerializerSettings JsonOptions = new()
        {
            Formatting = Formatting.Indented,
        };

        public event EventHandler? HistoryChanged;

        private HistoryService()
        {
            string dir;
            try
            {
                dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NetworkSpeedChecker");
            }
            catch
            {
                dir = AppContext.BaseDirectory;
            }
            Directory.CreateDirectory(dir);
            _historyPath = Path.Combine(dir, "speed_test_history.json");
        }

        public IReadOnlyList<SpeedTestResult> Current => _cache;

        public async Task LoadAsync()
        {
            await _ioLock.WaitAsync();
            try
            {
                if (!File.Exists(_historyPath))
                {
                    _cache = new List<SpeedTestResult>();
                    return;
                }
                var json = await File.ReadAllTextAsync(_historyPath);
                _cache = JsonConvert.DeserializeObject<List<SpeedTestResult>>(json, JsonOptions) ?? new List<SpeedTestResult>();
                _cache.Sort((a, b) => b.TestedAt.CompareTo(a.TestedAt));
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HistoryService.LoadAsync");
                _cache = new List<SpeedTestResult>();
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<SpeedTestResult> AddAsync(SpeedTestResult result)
        {
            await _ioLock.WaitAsync();
            try
            {
                _cache.Insert(0, result);
                await PersistAsyncLocked();
            }
            finally
            {
                _ioLock.Release();
            }
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            return result;
        }

        public async Task DeleteAsync(Guid id)
        {
            await _ioLock.WaitAsync();
            try
            {
                var removed = _cache.RemoveAll(r => r.Id == id) > 0;
                if (removed)
                    await PersistAsyncLocked();
            }
            finally
            {
                _ioLock.Release();
            }
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task ClearAsync()
        {
            await _ioLock.WaitAsync();
            try
            {
                _cache.Clear();
                await PersistAsyncLocked();
            }
            finally
            {
                _ioLock.Release();
            }
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task PersistAsyncLocked()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_cache, JsonOptions);
                await File.WriteAllTextAsync(_historyPath, json);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HistoryService.PersistAsyncLocked");
            }
        }
    }
}
