using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

using BlueCats.Bluetooth.Core.Base.Models.Enums;
using BlueCats.Bluetooth.Core.Base.Models.EventArgs;

using Nito.AsyncEx;


namespace BlueCats.Bluetooth.Core.Base {

    public abstract class Peripheral : IDisposable {
        
        // Can only be constructed internally by subclasses of CentralManager.
        internal Peripheral(string address, bool isPublicAddress, sbyte rssi = 0, CentralManager source = null) {

            State = PeripheralState.Disconnected;
            Address = address;
            IsAddressPublic = isPublicAddress;
            RSSI = rssi;
            ConnectedCentralDevice = source;
            _services = new ConcurrentBag<Service>();
        }

        public event EventHandler<PeripheralStateChangedEventArgs> StateChanged;

        public PeripheralState State { get; protected set; }
        public bool IsConnected => ConnectedCentralDevice != null && State == PeripheralState.Connected;
        public string Address { get; protected set; }
        public bool IsAddressPublic { get; protected set; }
        public bool IsAddressRandom {
            get {
                return !IsAddressPublic;
            }
            protected set {
                IsAddressPublic = !value;
            }
        }
        public string LocalName { get; internal set; }
        public sbyte RSSI { get; internal set; }
        public CentralManager ConnectedCentralDevice { get; protected set; }
        public IReadOnlyCollection<Service> Services => _services;
        internal byte ConnectionHandle { get; set; }
        private ConcurrentBag<Service> _services;

        public abstract sbyte ReadRSSI();

        public abstract Task<IReadOnlyCollection<Service>> DiscoverServicesAsync(IEnumerable<byte[]> serviceUUIDs = null, int timeoutMs = 4000);

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public override string ToString() {
            return "Peripheral: " + Address + (IsAddressPublic ? " (pub)" : " (rand)");
        }

        protected virtual void Dispose( bool disposing ) {

            switch ( State ) {
                case PeripheralState.Disposed:
                    return;
                case PeripheralState.Connected:
                    try {
                        ConnectedCentralDevice.CancelPeripheralConnectionAsync()
                            .GetAwaiter().GetResult();
                    }
                    catch ( ObjectDisposedException ) {
                        // Supress, was Disposed already
                    }
                    break;
            }
            ConnectedCentralDevice = null;

            foreach ( var service in _services ) {
                service.Dispose();
            }
            ClearServices();

            ConnectedCentralDevice = null;
        }

        internal void OnConnected(CentralManager connectionSource, byte connectionHandle) {
            ThrowIfDisposed();

            ConnectedCentralDevice = connectionSource;
            ConnectionHandle = connectionHandle;
            ClearServices();
            State = PeripheralState.Connected;
            StateChanged?.Invoke(this, new PeripheralStateChangedEventArgs(State));
        }

        internal void OnConnecting() {
            ThrowIfDisposed();

            State = PeripheralState.Connecting;
        }

        internal void OnDisconnected(CentralManager connectionSource) {
            ThrowIfDisposed();

            if (State == PeripheralState.Disconnected) return;
            State = PeripheralState.Disconnected;

            if (!ReferenceEquals(ConnectedCentralDevice, connectionSource)) return;
            ConnectedCentralDevice = null;

            foreach (var service in _services)
                service.Dispose();
            ClearServices();

            StateChanged?.Invoke(this, new PeripheralStateChangedEventArgs(State));
        }

        protected void AddService(Service service) => _services.Add(service);

        protected void ClearServices() => _services = new ConcurrentBag<Service>();

        protected void ThrowIfNotConnected() {
            if (!IsConnected)
                throw new Exception("Not connected to peripheral");
        }

        protected void ThrowIfDisposed() {
            if (State == PeripheralState.Disposed) throw new ObjectDisposedException(nameof(Peripheral));
        }

        ~Peripheral() {
            Dispose(false);
        }

    }

}