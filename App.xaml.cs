using System;
using Microsoft.UI.Xaml;
using NetworkSpeedChecker.Helpers;
using NetworkSpeedChecker.Services;

namespace NetworkSpeedChecker
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        public static MainWindow? MainWindowInstance { get; set; }

        public App()
        {
            InitializeComponent();
            UnhandledException += OnUnhandledException;
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            AppLogger.Log("OnLaunched");
            try
            {
                _ = HistoryService.Instance.LoadAsync();
                _window = new MainWindow();
                MainWindowInstance = (MainWindow)_window;
                _window.Activate();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "App.OnLaunched");
                throw;
            }
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            AppLogger.LogException(e.Exception, $"App.UnhandledException: {e.Message}");
            e.Handled = true;
        }
    }
}
