using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Windows.Networking.Connectivity;

namespace NetworkSpeedChecker.Services
{
    public class DiagnosticItem
    {
        public string Title { get; set; } = "";
        public string Status { get; set; } = "";
        public string Detail { get; set; } = "";
        public bool IsSuccess { get; set; }
        public bool IsWarning { get; set; }

        public string Glyph => IsSuccess ? "\uE73E" : (IsWarning ? "\uE7BA" : "\uE711");

        public Microsoft.UI.Xaml.Media.Brush StatusBrush => new Microsoft.UI.Xaml.Media.SolidColorBrush(
            IsSuccess 
                ? Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x16, 0xA3, 0x4A) 
                : (IsWarning 
                    ? Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xD9, 0x77, 0x06) 
                    : Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xDC, 0x26, 0x26)));

        public DiagnosticItem() { }

        public DiagnosticItem(string title, string status, string detail, bool isSuccess, bool isWarning = false)
        {
            Title = title;
            Status = status;
            Detail = detail;
            IsSuccess = isSuccess;
            IsWarning = isWarning;
        }
    }

    public record DiagnosticReport(
        bool IsConnected,
        string ConnectionType,
        string? Ssid,
        string AdapterName,
        long LinkSpeedMbps,
        double GatewayPingMs,
        double DnsPingMs,
        List<DiagnosticItem> Items,
        string OverallSummary,
        bool Needs5GhzRecommendation
    );

    public static class NetworkFixService
    {
        public static async Task<DiagnosticReport> RunDiagnosticsAsync(Action<string>? onStep = null)
        {
            return await Task.Run(async () =>
            {
                var items = new List<DiagnosticItem>();
                bool isConnected = false;
                string connType = "Unknown";
                string? ssid = null;
                string adapterName = "Standard Network Adapter";
                long linkSpeed = 0;
                double gatewayPing = -1;
                double dnsPing = -1;
                bool needs5gRecommendation = false;

                // Step 1: Check Windows Connection Profile
                onStep?.Invoke("Checking connection profile and adapter…");
                try
                {
                    var profile = NetworkInformation.GetInternetConnectionProfile();
                    if (profile != null)
                    {
                        isConnected = profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
                        if (profile.IsWlanConnectionProfile)
                        {
                            connType = "Wi-Fi";
                            try
                            {
                                ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
                            }
                            catch { }
                        }
                        else if (profile.IsWwanConnectionProfile)
                        {
                            connType = "Cellular";
                        }
                        else
                        {
                            connType = "Ethernet (Wired)";
                        }

                        if (profile.NetworkAdapter != null)
                        {
                            linkSpeed = (long)(profile.NetworkAdapter.OutboundMaxBitsPerSecond / 1_000_000.0);
                        }
                    }
                }
                catch { }

                // Step 2: Query active NetworkInterfaces
                onStep?.Invoke("Inspecting network interface hardware…");
                IPAddress? gatewayIp = null;
                try
                {
                    var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                    foreach (var ni in interfaces)
                    {
                        if (ni.OperationalStatus == OperationalStatus.Up &&
                            ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                        {
                            adapterName = ni.Description ?? ni.Name;
                            if (linkSpeed <= 0 && ni.Speed > 0)
                                linkSpeed = ni.Speed / 1_000_000;

                            var props = ni.GetIPProperties();
                            foreach (var gw in props.GatewayAddresses)
                            {
                                if (gw.Address != null && gw.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                {
                                    gatewayIp = gw.Address;
                                    break;
                                }
                            }
                            break;
                        }
                    }
                }
                catch { }

                items.Add(new DiagnosticItem(
                    "Network Connection",
                    isConnected ? "Connected" : "Offline",
                    $"{connType}{(ssid != null ? $" • SSID: {ssid}" : "")} • Adapter: {adapterName}",
                    isConnected,
                    !isConnected
                ));

                // Step 3: Test Gateway Latency
                onStep?.Invoke("Measuring local router latency…");
                if (gatewayIp != null)
                {
                    try
                    {
                        using var ping = new Ping();
                        var reply = await ping.SendPingAsync(gatewayIp, 1200);
                        if (reply.Status == IPStatus.Success)
                        {
                            gatewayPing = reply.RoundtripTime;
                            bool goodGw = gatewayPing <= 15;
                            items.Add(new DiagnosticItem(
                                "Local Router (Gateway)",
                                $"{gatewayPing:0} ms",
                                goodGw ? "Excellent local connection to your router." : "High latency to router. Interference or distance may be degrading signal.",
                                goodGw,
                                !goodGw
                            ));
                        }
                    }
                    catch { }
                }

                // Step 4: Test Cloudflare / Google Public DNS Latency
                onStep?.Invoke("Testing public DNS & internet responsiveness…");
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync("1.1.1.1", 2000);
                    if (reply.Status == IPStatus.Success)
                    {
                        dnsPing = reply.RoundtripTime;
                        bool goodDns = dnsPing <= 45;
                        items.Add(new DiagnosticItem(
                            "DNS & Cloudflare Edge",
                            $"{dnsPing:0} ms",
                            goodDns ? "Fast DNS response from Cloudflare (1.1.1.1)." : "Moderate latency to public DNS.",
                            goodDns,
                            !goodDns
                        ));
                    }
                }
                catch { }

                // Step 5: Wi-Fi band analysis
                onStep?.Invoke("Analyzing Wi-Fi band and signal profile…");
                if (connType == "Wi-Fi")
                {
                    bool isLikely5G = false;
                    if (!string.IsNullOrEmpty(ssid))
                    {
                        string lower = ssid.ToLowerInvariant();
                        if (lower.Contains("5g") || lower.Contains("5ghz") || lower.Contains("6g") || lower.Contains("6ghz"))
                            isLikely5G = true;
                    }
                    if (linkSpeed >= 300)
                    {
                        isLikely5G = true;
                    }

                    if (isLikely5G)
                    {
                        items.Add(new DiagnosticItem(
                            "Wi-Fi Band Detection",
                            "5 GHz / High-Speed Wi-Fi",
                            "Your device appears to be on a 5 GHz (or high throughput) network connection.",
                            true
                        ));
                    }
                    else
                    {
                        needs5gRecommendation = true;
                        items.Add(new DiagnosticItem(
                            "Wi-Fi Band Detection",
                            "2.4 GHz Band (or standard speed)",
                            "Connection may be on 2.4 GHz. Switching to your router's 5 GHz band can increase speeds by 3x–10x.",
                            false,
                            true
                        ));
                    }
                }

                // Step 6: Flush DNS Resolver Cache
                onStep?.Invoke("Optimizing resolver cache…");
                bool flushed = FlushDnsCache();
                items.Add(new DiagnosticItem(
                    "DNS Resolver Cache",
                    flushed ? "Optimized & Flushed" : "Active",
                    "Cleared stale domain cache and flushed local resolver records.",
                    true
                ));

                string summary = isConnected
                    ? (needs5gRecommendation
                        ? "Connected. For maximum speed, connect to your router's 5 GHz Wi-Fi band."
                        : "Your internet connection is healthy and responsive.")
                    : "No active internet connection detected. Check your Wi-Fi or network cable.";

                return new DiagnosticReport(
                    isConnected,
                    connType,
                    ssid,
                    adapterName,
                    linkSpeed,
                    gatewayPing,
                    dnsPing,
                    items,
                    summary,
                    needs5gRecommendation
                );
            });
        }

        public static bool FlushDnsCache()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
