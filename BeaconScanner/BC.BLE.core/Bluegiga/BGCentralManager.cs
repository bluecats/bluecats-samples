using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

using BlueCats.Bluetooth.Core.Base;
using BlueCats.Bluetooth.Core.Base.Models.Enums;
using BlueCats.Bluetooth.Core.Base.Models.EventArgs;
using BlueCats.Tools.Portable.IO;
using BlueCats.Tools.Portable.Lib.Bluegiga;
using BlueCats.Tools.Portable.Lib.Bluegiga.BLE.Events.Connection;
using BlueCats.Tools.Portable.Lib.Bluegiga.BLE.Events.GAP;
using BlueCats.Tools.Portable.Lib.Bluegiga.Models.Enums;
using BlueCats.Tools.Portable.Util;


using Nito.AsyncEx;

using static System.StringComparison;

namespace BlueCats.Bluetooth.Core.Bluegiga {

    public class BGCentralManager : CentralManager {  

        public BGCentralManager( ISerialDevice serialDevice ) {
            _bgApi = new BGLibApi( serialDevice );
            _bgApi.GAPScanResponse += BGApi_OnGAPScanResponse;
            _bgApi.ConnectionStatus += BGApi_OnConnectionStatus;
            _bgApi.ConnectionDisconnected += BGApi_OnConnectionDisconnected;
            _connectionStatusWaitHandle = new AsyncAutoResetEvent(false);
            _connectionDisconnectedWaitHandle = new AsyncAutoResetEvent(false);
        }

        private const int CONNECTING_TIMEOUT_MS                     = 8000;

        /* BLE scanning settings */
        private const BGGAPDiscoverModes SCANNING_GAP_DISCOVER_MODE = BGGAPDiscoverModes.Observation;

        /* BLE connection settings */
        // Negotiates the interval where master and slave devices exchange a chunk of data by presenting a range. 
        // Slave device decides the sctual connection interval or rejects the connection attempt if its desired 
        // interval is outside this range.
        
        // Specification consts
        private const ushort CONNECTION_SUPERVISION_TIMEOUT_MAX_MS  = 32000; 
        private const ushort CONNECTION_SUPERVISION_TIMEOUT_MIN_MS  = 100;
        private const ushort CONNECTION_INTERVAL_MAX_MS             = 4000;
        private const ushort CONNECTION_INTERVAL_MIN_MS             = 8;
        private const ushort CONNECTION_SLAVE_LATENCY_MAX           = 500;
        private const ushort CONNECTION_SLAVE_LATENCY_MIN           = 0;    // Equivalent to DISABLED

        // Connection interval, determines how often communication happens during a connection
        private const ushort CONNECTION_INTERVAL_UPPER_LIMIT_MS     = 16;     
        private const ushort CONNECTION_INTERVAL_LOWER_LIMIT_MS     = 10;

        // Supervision Timeout defines how long the devices can be out of range before the connection is closed.
        // According to specification, the Supervision Timeout in milliseconds shall be larger than 
        // (1 + latency) * conn_interval_max * 2, where conn_interval_max is given in milliseconds.      
        private const ushort CONNECTION_SUPERVISION_TIMEOUT_MS      = 10000;

        // Slave Latency defines how many connection intervals a slave device can skip. Increasing slave latency 
        // will decrease the energy consumption of the slave in scenarios where slave does not have data to send 
        // at every connection interval. Increasing also make missed packet retries slower, increasing chance of
        // Supervision Timeout to occur (https://bluegiga.zendesk.com/entries/58974496-Connection-lost-after-some-time-working).        
        private const ushort CONNECTION_SLAVE_LATENCY               = 0;

        private readonly BGLibApi _bgApi;
        private AsyncAutoResetEvent _connectionStatusWaitHandle;
        private AsyncAutoResetEvent _connectionDisconnectedWaitHandle;
        private BGErrorCode _reasonForLastDisconnection;
        private ExceptionDispatchInfo _exceptionToRethow;
        private ExceptionDispatchInfo _scanningExceptionToRethow;
        private Task _softResetTask;

        public override async Task ScanForAllPeripheralsAsync( bool activeScanning = false, int scanIntervalMs = 125, int scanWindowMs = 125 ) {

            switch (State) {
                case CentralManagerState.Connected:  throw new Exception( "Attempting to scan while device is in Connected state" );
                case CentralManagerState.Connecting: throw new Exception( "Attempting to scan while device is in Connecting state" );
                case CentralManagerState.Scanning:   Debug.WriteLine( $"{nameof(ScanForAllPeripheralsAsync)} called while already scanning, ignoring call" ); return;
                case CentralManagerState.Idle:       break;
                case CentralManagerState.Disposed:   throw new ObjectDisposedException( nameof(CentralManager) );
            }
            State = CentralManagerState.Scanning;

            try {
                await _bgApi.GAPEndProcedureAsync().ConfigureAwait( false );
            }
            catch ( Exception ex ) {
                Debug.Fail( $"Error while sending GAPEndProcedure to sniffer: {ex.Message}" );
            }

            await _bgApi.GAPSetScanParametersAsync(
                scanIntervalMs, 
                scanWindowMs, 
                activeScanning
            ).ConfigureAwait( false );

            await _bgApi.GAPDiscoverAsync( SCANNING_GAP_DISCOVER_MODE ).ConfigureAwait( false );
        }

        public override Task ScanForPeripheralWithServicesAsync( IList< Guid > serviceUUIDs, int scanIntervalMs = 75, int scanWindowMs = 50 ) {
            // TODO: Implement
            throw new NotImplementedException();
        }

        public override async Task StopScanAsync() {

            Action throwScanExceptionIfExists = () => {
                if ( _scanningExceptionToRethow == null ) return;
                var ex = _scanningExceptionToRethow;
                _scanningExceptionToRethow = null;
                ex.Throw();  
            };

            switch (State) {

                case CentralManagerState.Scanning:
                    await _bgApi.GAPEndProcedureAsync().ConfigureAwait( false );
                    await base.StopScanAsync().ConfigureAwait( false );
                    throwScanExceptionIfExists();
                    break;
                    
                case CentralManagerState.Connected:
                    Debug.WriteLine( "StopScan() called while in Connected state, ignoring." );
                    throwScanExceptionIfExists();
                    return;

                case CentralManagerState.Connecting:
                    Debug.WriteLine( "StopScan() called while in Connecting state, ignoring." );
                    throwScanExceptionIfExists();
                    return;

                case CentralManagerState.Idle:
                    Debug.WriteLine( "StopScan() called while in Idle state, ignoring." );
                    throwScanExceptionIfExists();
                    return;

                case CentralManagerState.Disposed:
                    throw new ObjectDisposedException( nameof(CentralManager) );
            }
            State = CentralManagerState.Idle;
        }
      
        public override async Task ConnectPeripheralAsync(Peripheral peripheral) {

            switch (State) {

                case CentralManagerState.Scanning:
                    throw new Exception( "Cannot connect to a peripheral while in a scanning state" );

                case CentralManagerState.Connected:
                    if ( peripheral.Address.Equals( ConnectedPeripheral.Address, OrdinalIgnoreCase ) ) {
                        Debug.WriteLine( "Attempting to connect to the peripheral that it is already connected to, ignoring." );
                        return;
                    }
                    throw new Exception( 
                        $"Attempting to connect to a peripheral '{peripheral.Address}' " 
                      + $"while already connected to peripheral '{ConnectedPeripheral.Address}'" 
                    );

                case CentralManagerState.Idle:
                    break;

                case CentralManagerState.Connecting:
                    throw new Exception( 
                        "Attempting to connect to a peripheral while already in the process " 
                      + "of connecting to a peripheral" 
                    );

                case CentralManagerState.Disposed:
                    throw new ObjectDisposedException( nameof(CentralManager) );
            }
            State = CentralManagerState.Connecting;

            ConnectedPeripheral = peripheral;
            ((BGPeripheral) ConnectedPeripheral).OnConnecting();

            Debug.WriteLine($"Connecting to Peripheral with address: {peripheral.Address}");

            var addrType = peripheral.IsAddressPublic 
                ? BGGAPAdvertiserAddressType.PublicAddress 
                : BGGAPAdvertiserAddressType.RandomAddress;

            _connectionStatusWaitHandle = new AsyncAutoResetEvent( false );
            _connectionDisconnectedWaitHandle = new AsyncAutoResetEvent( false );

            var handle = await _bgApi.GAPConnectDirectAsync( 
                peripheral.Address,
                addrType,
                CONNECTION_INTERVAL_LOWER_LIMIT_MS,
                CONNECTION_INTERVAL_UPPER_LIMIT_MS,
                CONNECTION_SUPERVISION_TIMEOUT_MS,
                CONNECTION_SLAVE_LATENCY
            ).ConfigureAwait( false );

            Task connectionStatusWaitTask = null;
            Task connectionDisconnectedWaitTask = null;         
            Task firstToComplete = null;
            bool didTimeout = false;

            using ( var cts = new CancellationTokenSource( CONNECTING_TIMEOUT_MS ) ) {

                connectionStatusWaitTask = _connectionStatusWaitHandle.WaitAsync( cts.Token );
                connectionDisconnectedWaitTask = _connectionDisconnectedWaitHandle.WaitAsync( cts.Token );            
                
                try {
                    firstToComplete = await Task.WhenAny( 
                        connectionStatusWaitTask, 
                        connectionDisconnectedWaitTask
                    ).ConfigureAwait( false );

                    if ( firstToComplete.IsCanceled ) 
                        didTimeout = true;
                    _exceptionToRethow?.Throw();
                }
                catch ( OperationCanceledException ) { didTimeout = true; }
                finally { cts.Cancel(); }
            }

            if ( didTimeout ) {
                if ( State != CentralManagerState.Disposed )
                    State = CentralManagerState.Idle;
                ConnectedPeripheral.OnDisconnected( this );
                ConnectedPeripheral = null;
                throw new TimeoutException( $"Timed out while attempting to connect to peripheral: {peripheral.Address}" );
            }
            if ( firstToComplete == connectionStatusWaitTask ) {
                if ( State != CentralManagerState.Disposed )
                    State = CentralManagerState.Connected;
                ( (BGPeripheral) peripheral ).OnConnected( this, handle );
            }
            else if ( firstToComplete == connectionDisconnectedWaitTask ) {
                if ( State != CentralManagerState.Disposed )
                    State = CentralManagerState.Idle;
                await SoftResetDongleState().ConfigureAwait( false );
                throw new Exception( $"Failed to connect to peripheral: {peripheral.Address}" );
            }
            else {
                throw new Exception();
            }
        }

        public override async Task CancelPeripheralConnectionAsync( int timeoutMs = 3000 ) {

            switch (State) {

                case CentralManagerState.Scanning:
                    throw new Exception( "Attempting to cancel a peripheral connection while in Scanning state" );

                case CentralManagerState.Connected:
                    break;

                case CentralManagerState.Idle:
                    Debug.WriteLine( "Attempting to cancel a peripheral connection while in Idle state" );
                    return;

                case CentralManagerState.Connecting:
                    throw new Exception( "Attempting to cancel a peripheral connection while in Connecting state" );

                case CentralManagerState.Disposed:
                    throw new ObjectDisposedException( nameof(CentralManager) );
            }

            await _bgApi.ConnectionDisconnectAsync( ConnectedPeripheral.ConnectionHandle )
                .ConfigureAwait( false );

            var didTimeout = false;

            using ( var cts = new CancellationTokenSource( timeoutMs ) ) {
                try {
                    await _connectionDisconnectedWaitHandle.WaitAsync( cts.Token )
                        .ConfigureAwait( false );
                    _exceptionToRethow?.Throw();
                }
                catch ( OperationCanceledException ) { didTimeout = true; }
            }

            if ( didTimeout ) { 
                throw new Exception( "Timed-out while attempting to disconnect" );    
            }
        }

        protected override void Dispose( bool disposing ) {

            const string TYPE_NAME = nameof( BGCentralManager );
            const string MEMBER_NAME = nameof( Dispose );

            try {
                if ( State == CentralManagerState.Disposed ) return;
                Debug.WriteLine( "Disposing central manager" );

                try {
                    base.Dispose( disposing );
                }
                catch ( Exception ex ) {                 
                    Debug.Fail( $"Error in {TYPE_NAME}::{MEMBER_NAME} while disposing: {ex.Message}" );
                }
                State = CentralManagerState.Disposed;

                _connectionStatusWaitHandle?.Set();
                _connectionDisconnectedWaitHandle?.Set();

                if ( _bgApi == null ) return;

                _bgApi.GAPScanResponse -= BGApi_OnGAPScanResponse;
                _bgApi.ConnectionStatus -= BGApi_OnConnectionStatus;
                _bgApi.ConnectionDisconnected -= BGApi_OnConnectionDisconnected;

                _bgApi.Dispose();
            }
            catch ( Exception ex ) {
                Debug.Fail( $"Error in {TYPE_NAME}::{MEMBER_NAME}: {ex.Message}" );
            }
        }

        private async Task SoftResetDongleState() {
            try {
                if ( ( _bgApi == null ) || _bgApi.IsDisposed ) return;        
                await _bgApi.GAPEndProcedureAsync().ConfigureAwait( false );

                _connectionStatusWaitHandle = new AsyncAutoResetEvent( false );
                _connectionDisconnectedWaitHandle = new AsyncAutoResetEvent( false );
            }
            catch ( Exception ex ) {                
                Debug.WriteLine( ex, "Error while resetting state of dongle" );
            }
            
        }

        private void BGApi_OnGAPScanResponse(object sender, ScanResponseEventArgs e) {
            if ( State == CentralManagerState.Disposed ) return;

            try {
                if ( State != CentralManagerState.Scanning ) {
                    // Should not enter this unless device is in a weird state, 
                    // most likely it was scanning and not told to stop. Attempt
                    // stop scanning and reset state.
                    Debug.WriteLine( "Received a BGApi_OnGAPScanResponse at an unexpected time, attempting to reset...");
                    if ( _softResetTask != null ) return;
                    _softResetTask = SoftResetDongleState();
                    _softResetTask.ContinueWith( task => {
                        _softResetTask = null;
                        if ( task.IsFaulted ) {
                            Debug.Fail( $"Received a BGApi_OnGAPScanResponse at an unexpected time and attempts to recover failed: {task.Exception.Message}" );
                        }
                    } );
                    return;
                }

                var addressType = (BGGAPAdvertiserAddressType) Enum.ToObject(typeof (BGGAPAdvertiserAddressType), e.address_type);
                var isPublicAddress = addressType ==BGGAPAdvertiserAddressType.PublicAddress;
                var address = e.sender.ToHexString();

                var packetType = (BGGAPPacketType) Enum.ToObject(typeof (BGGAPPacketType), e.packet_type);
                var adData = (packetType != BGGAPPacketType.ScanResponse ? e.data : null) ?? new byte[0];
                var respData = (packetType == BGGAPPacketType.ScanResponse ? e.data : null) ?? new byte[0];
                var isConnectable = packetType == BGGAPPacketType.ConnectableAd;

                var peripheral = new BGPeripheral(address, isPublicAddress, _bgApi, e.rssi);

                var peripheralDiscoveredArgs = new PeripheralDiscoveredEventArgs {
                    Peripheral = peripheral,
                    AdvertisementData = adData,
                    ScanResponseData = respData,
                    IsConnectable = isConnectable,
                    RSSI = e.rssi
                };

                PeripheralWasDiscovered( peripheralDiscoveredArgs );
            }
            catch ( Exception ex ) {
                if ( _scanningExceptionToRethow == null )
                    _scanningExceptionToRethow = ExceptionDispatchInfo.Capture( ex );
            }
        }

        private void BGApi_OnConnectionStatus(object sender, StatusEventArgs e) {

            try {
                var addr = e.address.ToHexString( true );
                var flags = ( BGConnectionStatusFlags ) Enum.ToObject( typeof( BGConnectionStatusFlags ), e.flags );

                Debug.WriteLine( "BLE Connection established with: " 
                    + $"handle={e.connection} " 
                    + $"flags=({flags}) "
                    + $"btaddr={addr} "
                    + $"conn_interval_ms={e.conn_interval * 1.25} "
                    + $"timeout_ms={e.timeout * 10} "
                    + $"latency={e.latency} "
                    + $"bonding_handle=0x{e.bonding:X2} "
                );

                switch ( State ) {
                    case CentralManagerState.Connecting: break;
                    case CentralManagerState.Scanning:   return;
                    case CentralManagerState.Connected:  return; // TODO: Backlog - check for, and handle, async disconnections
                    case CentralManagerState.Idle:       return;
                    case CentralManagerState.Disposed:   return;
                }

                if (!string.Equals( addr, ConnectedPeripheral.Address, OrdinalIgnoreCase ) )
                    return;

                if ( !flags.HasFlag( BGConnectionStatusFlags.Connected | BGConnectionStatusFlags.Completed ) )
                    return;

                SupervisionTimeoutMs = e.timeout * 100;
                Latency = e.latency;
                ConnectionIntervalMs = (int) ( e.conn_interval * 1.25 );

                if (State != CentralManagerState.Disposed)
                    State = CentralManagerState.Connected;

            }
            catch ( Exception ex ) {
                if ( _exceptionToRethow == null )
                    _exceptionToRethow = ExceptionDispatchInfo.Capture( ex );
            }
            finally {
                _connectionStatusWaitHandle.Set();
            }
        }

        private void BGApi_OnConnectionDisconnected( object sender, DisconnectedEventArgs e ) {

            try {
                Debug.WriteLine( "BGApi_OnConnectionDisconnected Event" );
                if ( State == CentralManagerState.Disposed ) return;

                _reasonForLastDisconnection = (BGErrorCode) Enum.ToObject( typeof( BGErrorCode ), e.reason );

                if ( _reasonForLastDisconnection == BGErrorCode.ConnectionFailedToBeEstablished
                  && State != CentralManagerState.Connected )
                    return; // was never connected to begin with

                OnPeripheralDisconnected(
                    $"{_reasonForLastDisconnection} (0x{(UInt16) _reasonForLastDisconnection:X4})"
                );
            }
            catch ( Exception ex ) {
                if ( _exceptionToRethow == null )
                    _exceptionToRethow = ExceptionDispatchInfo.Capture( ex );
            }
            finally {
                _connectionDisconnectedWaitHandle.Set();
            }
        }

    }
}
