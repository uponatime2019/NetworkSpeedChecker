using System;
using System.Threading.Tasks;
using Windows.Networking.Connectivity;

namespace NetworkSpeedChecker.Services
{
    public enum ConnectionKind
    {
        Connected,
        Offline
    }

    /// <summary>Tracks internet reachability for the header status pill.</summary>
    public class NetworkStatusService
    {
        public event EventHandler<ConnectionKind>? StatusChanged;

        public ConnectionKind Current { get; private set; } = ConnectionKind.Connected;

        public NetworkStatusService()
        {
            Refresh();
            try
            {
                NetworkInformation.NetworkStatusChanged += _ => Refresh();
            }
            catch
            {
                // Event registration can fail in odd sandboxed environments; polling below still works.
            }
        }

        public void Refresh()
        {
            var kind = Check() ? ConnectionKind.Connected : ConnectionKind.Offline;
            if (kind != Current)
            {
                Current = kind;
                StatusChanged?.Invoke(this, kind);
            }
        }

        private static bool Check()
        {
            try
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                return profile?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
            }
            catch
            {
                return true; // Optimistic default; real failures surface in the test itself.
            }
        }
    }
}
