using System;
using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace NetworkSpeedChecker.Views
{
    /// <summary>About page: app identity, measurement methodology and privacy notice.</summary>
    public sealed partial class AboutView : Page
    {
        public AboutView()
        {
            InitializeComponent();
            TryFillVersion();
        }

        private void TryFillVersion()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                if (v != null)
                {
                    VersionText.Text = $"Version {v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
                    return;
                }
            }
            catch
            {
            }
            VersionText.Text = "Version 1.0.0";
        }
    }
}
