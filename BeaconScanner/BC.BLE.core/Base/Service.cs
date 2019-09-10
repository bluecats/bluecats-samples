using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using BlueCats.Bluetooth.Core.Base.Models.Enums;
using BlueCats.Tools.Portable.Util;



using Nito.AsyncEx;

namespace BlueCats.Bluetooth.Core.Base {

    public abstract class Service : IDisposable {

        // Can only be constructed internally by Peripheral subclasses.
        internal Service(Peripheral localPeripheral, IEnumerable<byte> uuid, ushort startAttHandle, ushort endAttHandle) {

            LocalPeripheral = localPeripheral;
            UUID = uuid.ToArray();
            ConnectionHandle = localPeripheral.ConnectionHandle;
            StartATTHandle = startAttHandle;
            EndATTHandle = endAttHandle;
            _characteristics = new ConcurrentBag<Characteristic>();
            State = ServiceState.Idle;
            Debug.WriteLine($"Service constructed: uuid={UUID.ToHexString(true)} starthandle={StartATTHandle:X4} endhandle={EndATTHandle:X4}");
        }
        
        public ServiceState State { get; protected set; }
        public Peripheral LocalPeripheral { get; private set; }
        public byte[] UUID { get; }
        public IReadOnlyCollection<Characteristic> Characteristics => _characteristics;

       
        internal ushort EndATTHandle { get; }
        internal ushort StartATTHandle { get; }
        internal byte ConnectionHandle { get; }
        private ConcurrentBag<Characteristic> _characteristics;

        public abstract Task<IReadOnlyCollection<Characteristic>> DiscoverCharacteristicsAsync(IList<byte[]> characteristicUUIDs = null, int timeoutMs = 10000);

        protected void ThrowIfNotConnected() {
            if (!LocalPeripheral.IsConnected) throw new Exception("Not connected to peripheral");
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {

            if ( State == ServiceState.Disposed ) return;

            foreach (var characteristic in Characteristics)
                characteristic.Dispose();
            ClearCharacteristics();

            LocalPeripheral = null;
        }

        protected void ThrowIfDisposed() {
            if (State == ServiceState.Disposed) throw new ObjectDisposedException(nameof(Service));
        }

        protected void AddCharacteristic(Characteristic characteristic) => _characteristics.Add(characteristic);

        protected void ClearCharacteristics() => _characteristics = new ConcurrentBag<Characteristic>();

        ~Service() {
            Dispose(false);
        }
    }

}