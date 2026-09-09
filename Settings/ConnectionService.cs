using FS.SDK.Network.API;
using System.Windows.Threading;
using VIBN_Tools.GlobalClasses;

namespace VIBN_Tools.Settings
{
    /// <summary>Polls the FEE SDK state and exposes confirmed connection transitions to the UI.</summary>
    public class FeeConnectionService : NotifyBase
    {
        public const string MissingConnectionMessage = "Keine Verbindung zu FEE vorhanden.";

        private readonly DispatcherTimer _timer;

        public event Action Connected;

        public bool LoadFeeDataOnConnect { get; set; }


        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                bool changed = SetPropertyChange(ref _isConnected, value);

                if (changed)
                {
                    OnPropertyChanged(nameof(CanUseFeeFeatures));
                    OnPropertyChanged(nameof(UnavailableReason));
                }

                if (changed && value)
                {
                    Connected?.Invoke();
                }
            }
        }

        /// <summary>
        /// Central capability used by all UI actions that require a confirmed
        /// Project-Settings connection. It deliberately follows the SDK state,
        /// not merely a completed Connect call.
        /// </summary>
        public bool CanUseFeeFeatures => IsConnected;

        /// <summary>Reason shown by disabled FEE-dependent controls.</summary>
        public string? UnavailableReason =>
            IsConnected ? null : MissingConnectionMessage;

        private bool _isConnecting;
        public bool IsConnecting
        {
            get => _isConnecting;
            private set => SetPropertyChange(ref _isConnecting, value);
        }

        public FeeConnectionService()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (sender, eventargs) => CheckConnection();
            _timer.Start();
        }

        private void CheckConnection()
        {
            // Project Settings initializes the shared SDK before normal UI use.
            // A disconnected design-time or smoke-test view must nevertheless
            // remain loadable without constructing the complete FEE runtime.
            if (Services.ApiInstance is null)
            {
                IsConnected = false;
                IsConnecting = false;
                return;
            }

            try
            {
                // Some FEE runtime versions throw while no interface endpoint
                // exists yet. Polling must remain silent until an explicit
                // connection attempt succeeds.
                var state = Services.ApiInstance.ApiState;
                IsConnected = state == NetworkState.Connected;
                IsConnecting = state == NetworkState.Connecting;
            }
            catch
            {
                IsConnected = false;
                IsConnecting = false;
            }

        }
    }
}
