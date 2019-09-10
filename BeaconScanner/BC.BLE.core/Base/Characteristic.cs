using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BlueCats.Bluetooth.Core.Base.Models.Enums;
using BlueCats.Bluetooth.Core.Models;
using BlueCats.Bluetooth.Core.Models.Enums;
using BlueCats.Tools.Portable.Util;

using Nito.AsyncEx;

namespace BlueCats.Bluetooth.Core.Base {

    public abstract class Characteristic : IDisposable {

        // Can only be constructed internally by Bluetooth.Core.Service subclasses.
        internal Characteristic(Peripheral localPeripheral) {
            LocalPeripheral = localPeripheral;
            ConnectionHandle = localPeripheral.ConnectionHandle;
        }

        public event EventHandler<byte[]> Notification;
        public event EventHandler<byte[]> Indication;

        public CharacteristicState State { get; protected set; }
        public bool IsBusy => State != CharacteristicState.Idle;
        public Service ParentService { get; internal set; }
        public Peripheral LocalPeripheral { get; }
        public byte[] UUID { get; internal set; }
        public bool CanRead => (PropertiesFlags & GATTCharacteristicProperties.Read) == GATTCharacteristicProperties.Read;
        public bool CanWrite => (PropertiesFlags & GATTCharacteristicProperties.Write) == GATTCharacteristicProperties.Write;
        public bool CanNotify => (PropertiesFlags & GATTCharacteristicProperties.Notify) == GATTCharacteristicProperties.Notify;
        public bool CanIndicate => (PropertiesFlags & GATTCharacteristicProperties.Indicate) == GATTCharacteristicProperties.Indicate;
        public bool IsNotificationsEnabled { get; private set; }
        public bool IsIndicationsEnabled { get; private set; }
        internal byte ConnectionHandle { get; set; }
        internal UInt16? CharacteristicDeclarationHandle { get; set; }
        internal UInt16? CharacteristicValueHandle { get; set; }
        internal UInt16? CharacteristicClientConfigurationHandle { get; set; }
        protected GATTCharacteristicProperties PropertiesFlags { get; private set; }
        protected object StateLock { get; } = new object();

        

        public async Task<byte[]> ReadAsync() { 
            ThrowIfDisposed();
            ThrowIfNotConnected();
            if (!CanRead) throw new Exception("Reading from characteristic is disabled");
            if (!CharacteristicValueHandle.HasValue)
                throw new Exception("Do not have access to characteristic value, missing handle");
            var data = await ReadFromAttributeAsync(CharacteristicValueHandle.Value).ConfigureAwait( false );
            return data;
        }

        public async Task WriteAsync(IList<byte> data) {
            ThrowIfDisposed();
            ThrowIfNotConnected();
            if (!CanWrite) throw new Exception("Writing to characteristic is disabled");
            if (!CharacteristicValueHandle.HasValue)
                throw new Exception("Do not have access to characteristic value, missing handle");
            await WriteToAttributeAsync(CharacteristicValueHandle.Value, data).ConfigureAwait( false );
        }

        public async Task DisableIndicationsAsync() {
            Debug.WriteLine("Disabling indications");
            ThrowIfDisposed();
            ThrowIfNotConnected();
            if (!CharacteristicClientConfigurationHandle.HasValue) 
                throw new Exception("Do not have access to characteristic client configuration, missing handle");

            var value = new byte[] { 0x00, 0x00 };
            await WriteToAttributeAsync(
                CharacteristicClientConfigurationHandle.Value, 
                value
            ).ConfigureAwait( false );
            IsIndicationsEnabled = false;
        }

        public async Task EnableIndicationsAsync() {
            Debug.WriteLine("Enabling indications");
            ThrowIfDisposed();
            ThrowIfNotConnected();
            if (!CharacteristicClientConfigurationHandle.HasValue) 
                throw new Exception("Do not have access to characteristic client configuration, missing handle");

            var value = BitConverter.GetBytes((UInt16) GATTClientCharacteristicClientConfigurations.Indication);
            await WriteToAttributeAsync(
                CharacteristicClientConfigurationHandle.Value, 
                value
            ).ConfigureAwait( false );
            IsIndicationsEnabled = true;
        }

        public async Task DisableNotificationsAsync() {
            Debug.WriteLine("Disabling notifications");
            ThrowIfDisposed();
            ThrowIfNotConnected();
            if (!CharacteristicClientConfigurationHandle.HasValue) 
                throw new Exception("Do not have access to characteristic client configuration, missing handle");

            var value = BitConverter.GetBytes((UInt16) GATTClientCharacteristicClientConfigurations.Notification);
            await WriteToAttributeAsync(
                CharacteristicClientConfigurationHandle.Value, 
                value
            ).ConfigureAwait( false );
            IsNotificationsEnabled = false;
        }

        public async Task EnableNotificationsAsync() {
            Debug.WriteLine("Enabling notifications");
            ThrowIfDisposed();
            ThrowIfNotConnected();
            if (!CharacteristicClientConfigurationHandle.HasValue) 
                throw new Exception("Do not have access to characteristic client configuration, missing handle");

            var value = BitConverter.GetBytes((UInt16) GATTClientCharacteristicClientConfigurations.Notification);
            await WriteToAttributeAsync(
                CharacteristicClientConfigurationHandle.Value, 
                value
            ).ConfigureAwait( false );
            IsNotificationsEnabled = true;
        }

        public override string ToString() {
            return $"uuid={UUID.ToHexString(true)} handle=0x{CharacteristicValueHandle:X4}";
        }

        public virtual void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            lock ( StateLock )
                if (State == CharacteristicState.Disposed) return;
            Notification = null;
            Indication = null;
        }

        protected void OnNotification(IEnumerable<byte> notificationData) {
            ThrowIfDisposed();
            ThrowIfNotConnected();
            if (!LocalPeripheral.IsConnected) return;
            if (!IsNotificationsEnabled) return;
            Notification?.Invoke(this, notificationData.ToArray());
        }

        protected void OnIndication(IEnumerable<byte> indicationData) {
            ThrowIfDisposed();
            ThrowIfNotConnected();
            if (!LocalPeripheral.IsConnected) return;
            if (!IsIndicationsEnabled) return;
            Indication?.Invoke(this, indicationData.ToArray());
        }

        protected abstract Task<byte[]> ReadFromAttributeAsync(UInt16 attHandle, int timeoutMs = 3000);

        protected abstract Task WriteToAttributeAsync(UInt16 attHandle, IList<byte> data, int timeoutMs = 5000);

        internal void ParseCharacteristicDeclarationData(IList<byte> data) {

            if (data == null || data.Count < 4)
                throw new Exception("Characteristic Declaration data is an invalid length");
            
            var propertiesBitField = data[0];
            PropertiesFlags = (GATTCharacteristicProperties)propertiesBitField;

            CharacteristicValueHandle = BitConverter.ToUInt16(data.ToArray(), 1);

            UUID = data.Skip(3).ToArray();
        }

        protected void ThrowIfNotConnected() {
            if (!LocalPeripheral.IsConnected) throw new Exception("Not connected to peripheral");
        }

        protected void ThrowIfDisposed() {
            lock ( StateLock )
                if (State == CharacteristicState.Disposed) throw new ObjectDisposedException(nameof(Characteristic));
        }

        ~Characteristic() {
            Dispose(false);
        }

    }

}