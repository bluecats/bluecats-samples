using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using BlueCats.Bluetooth.Core.Base.Models.Enums;
using BlueCats.Bluetooth.Core.Base.Models.EventArgs;



namespace BlueCats.Bluetooth.Core.Base {

    public abstract class CentralManager : IDisposable {

        protected CentralManager() {
            _discoveredPeripheralCache = new ConcurrentDictionary<string, PeripheralDiscoveredEventArgs>();
            State = CentralManagerState.Idle;
        }

        public event EventHandler<PeripheralDiscoveredEventArgs> PeripheralDiscovered;
        public event EventHandler<PeripheralDisconnectedEventArgs> PeripheralDisconnected;

        public Peripheral ConnectedPeripheral { get; protected set; }
        public bool IsConnected => (ConnectedPeripheral != null) && (State == CentralManagerState.Connected);
        public CentralManagerState State {
            get {
                return _state;
            }
            protected set {
                _state = value;
                Debug.WriteLine($"CentralManager.State--> | {_state} |");
            }
        }
        public int ConnectionIntervalMs { get; protected set; }
        public int SupervisionTimeoutMs { get; protected set; }
        public int Latency { get; protected set; }

        private CentralManagerState _state;
        private readonly ConcurrentDictionary<string, PeripheralDiscoveredEventArgs> _discoveredPeripheralCache;

        public abstract Task ScanForAllPeripheralsAsync( bool activeScanning = false, int scanIntervalMs = 125, int scanWindowMs = 125 );

        public abstract Task ScanForPeripheralWithServicesAsync(IList<Guid> serviceUUIDs, int scanIntervalMs = 75, int scanWindowMs = 50);

        public virtual Task StopScanAsync() {
            ClearCache();
            return Task.CompletedTask;
        } 

        public abstract Task ConnectPeripheralAsync(Peripheral peripheral);

        public abstract Task CancelPeripheralConnectionAsync(int timeoutMs = 3000);

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (State == CentralManagerState.Disposed) return;

            try {
                switch (State) {
                    case CentralManagerState.Connected:
                    case CentralManagerState.Connecting:
                        CancelPeripheralConnectionAsync().Wait();
                        break;
                    case CentralManagerState.Scanning:
                        StopScanAsync();
                        break;
                    case CentralManagerState.Idle:
                        break;
                }
            }
            catch ( Exception ex ) {
                const string TYPE_NAME = nameof( CentralManager );
                const string MEMBER_NAME = nameof( Dispose );
                Debug.WriteLine( ex, $"Error in {TYPE_NAME}::{MEMBER_NAME} while disposing" );
            }

            ClearCache();
            PeripheralDiscovered = null;
            PeripheralDisconnected = null;
        }

        protected void PeripheralWasDiscovered(PeripheralDiscoveredEventArgs args) {
            ThrowIfDisposed();
            CacheAndUpdate( args );
            PeripheralDiscovered?.Invoke( this, args );
        }

        protected void OnPeripheralDisconnected(string error = "") {

            // This should be the only method setting Idle State
            ThrowIfDisposed();
            if ((State == CentralManagerState.Idle) || (State == CentralManagerState.Disposed))
                return;
            State = CentralManagerState.Idle;

            Debug.WriteLine($"Periperal disconnected: btaddr={ConnectedPeripheral.Address} reason='{error}'");

            var args = new PeripheralDisconnectedEventArgs {
                Error = error,
                Peripheral = ConnectedPeripheral
            };
            ConnectedPeripheral.OnDisconnected(this);
            ConnectedPeripheral = null;
                
            PeripheralDisconnected?.Invoke(this, args);
        }

        protected void ThrowIfDisposed() {
            if (State == CentralManagerState.Disposed) throw new ObjectDisposedException(nameof(CentralManager));
        }

        private void CacheAndUpdate(PeripheralDiscoveredEventArgs args) {
            if (State == CentralManagerState.Disposed) return;

            var address = args.Peripheral.Address;
            var newPeripheral = args.Peripheral;

            var cachedArgs = LoadFromCache(address);

            if (cachedArgs == null) {
                _discoveredPeripheralCache.TryAdd(address, args);
            } else {
                // update cache by starting with the received-args and updating ScanResponse and LocalName 
                // from the cache if they don't exist, then replace the cached-args with the newly updated 
                // received-args.
                var cachedPeripheral = cachedArgs.Peripheral;
                    newPeripheral.LocalName = cachedPeripheral.LocalName;
                if (args.ScanResponseData == null || args.ScanResponseData.Length == 0)
                    args.ScanResponseData = cachedArgs.ScanResponseData;
            }
        }

        private PeripheralDiscoveredEventArgs LoadFromCache(string address) {
            ThrowIfDisposed();
            PeripheralDiscoveredEventArgs args = null;
            _discoveredPeripheralCache.TryGetValue(address, out args);
            return args;
        }

        private void ClearCache() {
            if (State == CentralManagerState.Disposed) return;
            _discoveredPeripheralCache.Clear();
        }

        ~CentralManager() {
            Dispose(false);
        }

    }

}