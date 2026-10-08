using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using NetworkSpeedChecker.Helpers;
using NetworkSpeedChecker.Services;
using NetworkSpeedChecker.Views;

namespace NetworkSpeedChecker
{
    /// <summary>App shell: navy top bar with connection status plus a NavigationView page host.</summary>
    public sealed partial class MainWindow : Window
    {
        public Frame Frame => ContentFrame;

        public MainWindow()
        {
            InitializeComponent();
            App.MainWindowInstance = this;
            WindowHelper.SetAppIcon(this);

            TryCustomizeWindow();

            // Selection is set programmatically (never via IsSelected in XAML) so the
            // SelectionChanged handler runs only after all named fields exist.
            NavView.SelectedItem = NavItemSpeedTest;

            AppStateService.Instance.NetworkStatus.StatusChanged += OnNetworkStatusChanged;
            AppStateService.Instance.TestRunningChanged += OnTestRunningChanged;
            Closed += (_, _) =>
            {
                AppStateService.Instance.NetworkStatus.StatusChanged -= OnNetworkStatusChanged;
                AppStateService.Instance.TestRunningChanged -= OnTestRunningChanged;
            };

            if (Content is FrameworkElement rootElement)
            {
                rootElement.Loaded += async (_, _) =>
                {
                    var args = Environment.GetCommandLineArgs();
                    if (args.Any(a => a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            await Task.Delay(1000);
                            var rtb = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                            await rtb.RenderAsync(Content);
                            var pixelBuffer = await rtb.GetPixelsAsync();
                            var reader = Windows.Storage.Streams.DataReader.FromBuffer(pixelBuffer);
                            byte[] pixels = new byte[pixelBuffer.Length];
                            reader.ReadBytes(pixels);

                            string targetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "screenshot.png");
                            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                            using (var fs = File.Open(targetPath, FileMode.Create))
                            {
                                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                                    Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,
                                    fs.AsRandomAccessStream());
                                encoder.SetPixelData(
                                    Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                                    Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                                    (uint)rtb.PixelWidth,
                                    (uint)rtb.PixelHeight,
                                    96, 96,
                                    pixels);
                                await encoder.FlushAsync();
                            }

                            try
                            {
                                string cwdAssets = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "screenshot.png");
                                if (Path.GetFullPath(cwdAssets) != Path.GetFullPath(targetPath))
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(cwdAssets)!);
                                    File.Copy(targetPath, cwdAssets, true);
                                }
                            }
                            catch { }

                            Environment.Exit(0);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.LogException(ex, "MainWindow.CaptureScreenshot");
                            Environment.Exit(1);
                        }
                    }
                };
            }
        }

        private void TryCustomizeWindow()
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                appWindow.Title = "Network Speed Checker — Internet Speed Test";
                appWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));
                if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.PreferredMinimumWidth = 520;
                    presenter.PreferredMinimumHeight = 560;
                    presenter.Maximize();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "MainWindow.TryCustomizeWindow");
            }
        }

        private void OnNetworkStatusChanged(object? sender, ConnectionKind kind) =>
            UpdateStatusPill();

        private void OnTestRunningChanged(object? sender, EventArgs e) =>
            UpdateStatusPill();

        private void UpdateStatusPill()
        {
            try
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (StatusDot is null || StatusText is null) return;
                    if (AppStateService.Instance.IsTestRunning)
                    {
                        StatusDot.Fill = (Brush)Application.Current.Resources["AccentOrangeBrush"];
                        StatusText.Text = "Testing…";
                    }
                    else if (AppStateService.Instance.NetworkStatus.Current == ConnectionKind.Connected)
                    {
                        StatusDot.Fill = (Brush)Application.Current.Resources["GoodBrush"];
                        StatusText.Text = "Connected";
                    }
                    else
                    {
                        StatusDot.Fill = (Brush)Application.Current.Resources["BadBrush"];
                        StatusText.Text = "Offline";
                    }
                });
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "MainWindow.UpdateStatusPill");
            }
        }

        /// <summary>Programmatic navigation used by empty-state buttons on other pages.</summary>
        public void NavigateToSpeedTest() => NavView.SelectedItem = NavItemSpeedTest;

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            try
            {
                if (ContentFrame is null) return;
                if (args.SelectedItemContainer?.Tag is not string tag) return;
                var page = tag switch
                {
                    "History" => typeof(HistoryView),
                    "Charts" => typeof(ChartsView),
                    "FixInternet" => typeof(FixInternetView),
                    "About" => typeof(AboutView),
                    _ => typeof(SpeedTestView)
                };
                var currentType = ContentFrame.Content?.GetType();
                if (currentType != page)
                    ContentFrame.Navigate(page, null, new Microsoft.UI.Xaml.Media.Animation.EntranceNavigationTransitionInfo());
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "MainWindow.NavView_SelectionChanged");
            }
        }
    }
}
