//
// A simpler wrapper API to BGLib's low-level constructs
//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BlueCats.Tools.Portable.IO;
using BlueCats.Tools.Portable.Lib.Bluegiga.BLE.Events.ATTClient;
using BlueCats.Tools.Portable.Lib.Bluegiga.BLE.Events.Connection;
using BlueCats.Tools.Portable.Lib.Bluegiga.BLE.Events.GAP;
using BlueCats.Tools.Portable.Lib.Bluegiga.BLE.Responses.ATTClient;
using BlueCats.Tools.Portable.Lib.Bluegiga.BLE.Responses.Connection;
using BlueCats.Tools.Portable.Lib.Bluegiga.BLE.Responses.GAP;
using BlueCats.Tools.Portable.Lib.Bluegiga.BLE.Responses.System;
using BlueCats.Tools.Portable.Lib.Bluegiga.Models.Enums;
using BlueCats.Tools.Portable.Util;


using Nito.AsyncEx;

namespace BlueCats.Tools.Portable.Lib.Bluegiga {

    public class BGLibApi : IDisposable {

        public BGLibApi( ISerialDevice serialDevice ) {
            _serialDevice = serialDevice;
            if ( !_serialDevice.IsAttached ) {
                serialDevice.AttachAsync( 256000 ).GetAwaiter().GetResult();
            }
            _bglib = new BGLib( _serialDevice );
            StartSerialRxConsumerTask();           
            _serialDevice.DataReceived += Serial_DataReceived;
            SubscribeToCommandResponses();
            SubscribeToEvents();
        }      

        public event ScanResponseEventHandler GAPScanResponse;
        public event StatusEventHandler ConnectionStatus;
        public event GroupFoundEventHandler ATTClientGroupFound;
        public event ProcedureCompletedEventHandler ATTClientProcedureCompleted;
        public event FindInformationFoundEventHandler ATTClientFindInformationFound;
        public event AttributeValueEventHandler ATTClientAttributeValue;
        public event IndicatedEventHandler ATTClientIndicated;
        public event DisconnectedEventHandler ConnectionDisconnected;
        public bool IsDisposed { get; private set; }

        private readonly BlockingCollection< byte[] > _serialRxQueue = new BlockingCollection< byte[] >();
        private readonly BGLib _bglib;
        private readonly ISerialDevice _serialDevice;
        private readonly AsyncAutoResetEvent _cmdRespWaitHandle = new AsyncAutoResetEvent( false );
        private UInt16 _lastResult;
        private object _lastResponseArgs;
        private Task _serialRxConsumerTask;
        private CancellationTokenSource _serialRxConsumerTaskCTS;

        public async Task< bool > SystemHello( int timeoutMs = 4000 ) {
            // This is a ping event. Return's true if device responds, 
            // false if the valid response is not received before the given timeout
            var cmd = _bglib.BLECommandSystemHello();

            await _bglib.SendCommandAsync(cmd).ConfigureAwait( false );

            bool didTimeout = false;
            using ( var cts = new CancellationTokenSource( timeoutMs ) ) {              
                try { await _cmdRespWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false ); }
                catch ( OperationCanceledException ) { didTimeout = true; }               
            }
            return !didTimeout && _lastResponseArgs is HelloEventArgs;
        }

        public async Task GAPSetScanParametersAsync(int scanIntervalMs, int scanWindowMs, bool activeScanningEnabled) {

            const int TIMEOUT_MS = 6000;

            var scanInt = (ushort) ConvertMsTo625UsUnits(scanIntervalMs);
            var scanWin = (ushort) ConvertMsTo625UsUnits(scanWindowMs);
            byte activeScan = activeScanningEnabled ? (byte) 1 : (byte) 0;

            var cmd = _bglib.BLECommandGAPSetScanParameters(scanInt, scanWin, activeScan);
            await _bglib.SendCommandAsync( cmd ).ConfigureAwait( false );

            using ( var cts = new CancellationTokenSource(TIMEOUT_MS) ) {
                bool didTimeout = false;
                try { await _cmdRespWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false ); }
                catch ( OperationCanceledException ) { didTimeout = true; }
                if (didTimeout) throw new TimeoutException();
            }

            if (_lastResult == 0) return;

            var error = (BGErrorCode) Enum.ToObject(typeof(BGErrorCode), _lastResult);
            
            // Continue on if this error is received. Happens at times but not critical error
            if ( error == BGErrorCode.DeviceInWrongState ) {
                Debug.WriteLine( $"Warning: GAPSetScanParameters returned: error={error}" );
                return;
            }

            string errMsg = $"GAPSetScanParameters returned: error={error} code={_lastResult:X4}";
            throw new Exception(errMsg);
        }

        public async Task GAPDiscoverAsync(BGGAPDiscoverModes bggapDiscoverMode) {

            const int TIMEOUT_MS = 6000;

            var cmd = _bglib.BLECommandGAPDiscover((byte) bggapDiscoverMode);
            await _bglib.SendCommandAsync(cmd).ConfigureAwait( false );

             using ( var cts = new CancellationTokenSource(TIMEOUT_MS) ) {
                bool didTimeout = false;
                try { await _cmdRespWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false ); }
                catch ( OperationCanceledException ) { didTimeout = true; }
                if (didTimeout) throw new TimeoutException();
            }

            if (_lastResult == 0) return;

            var error = Enum.ToObject(typeof(BGErrorCode), _lastResult);
            string errMsg = $"GAPDiscover returned: error={error} code={_lastResult:X4}";
            throw new Exception(errMsg);
        }

        public async Task GAPEndProcedureAsync() {

            // TODO: Make parsing faster because when scanning gets backed up, 
            // this will timeout before it can be reached possible
            const int TIMEOUT_MS = 600;

            var cmd = _bglib.BLECommandGAPEndProcedure();
            await _bglib.SendCommandAsync(cmd).ConfigureAwait( false );

            using ( var cts = new CancellationTokenSource( TIMEOUT_MS) ) {
                try { await _cmdRespWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false ); }
                catch ( OperationCanceledException ) {
                    throw new TimeoutException( "GAPEndProcedureAsync" );
                }
            }

            if (_lastResult == 0) return;

            var error = (BGErrorCode) Enum.ToObject(typeof(BGErrorCode), _lastResult);
            
            // Continue on if this error is received. Happens at times but not critical error
            if ( error == BGErrorCode.DeviceInWrongState ) {
                Debug.WriteLine( $"Warning: GAPSetScanParameters returned: error={error}" );
                return;
            }
            string errMsg = $"GAPEndProcedure returned: error={error} code={_lastResult:X4}";
            throw new Exception(errMsg);
        }

        public async Task< byte > GAPConnectDirectAsync(string bluetoothAddress, BGGAPAdvertiserAddressType addressType, ushort connectIntervalMinMs, ushort connectIntervalMaxMs, ushort supervisionTimeoutMs, ushort latency) {

            const int TIMEOUT_MS = 6000;

            if ( connectIntervalMinMs <= 7 ) {
                throw new ArgumentOutOfRangeException( 
                    nameof( connectIntervalMaxMs ), 
                    "Connection Interval Min must be greater than 7ms" 
                );
            }
            var supervisionTimeoutMinMs = ( 1 + latency ) * connectIntervalMaxMs * 2;
            if ( supervisionTimeoutMs <= supervisionTimeoutMinMs ) {
                throw new ArgumentException(
                    "According to the specification, the Supervision Timeout in milliseconds "
                  + "shall be larger than (1 + latency) * connectIntervalMaxMs * 2"
                );
            }           
            if ( connectIntervalMaxMs < connectIntervalMinMs ) {
                throw new ArgumentOutOfRangeException( 
                    nameof( connectIntervalMaxMs ), 
                    "Connection Interval Max must be greater than or equal to Connection Interval Min" 
                );
            }

            var btaddr = DataConverter.HexStringToByteArray(bluetoothAddress);
            var type = (byte) addressType;
            var connIntMin = (UInt16) ( connectIntervalMinMs / 1.25 );
            var connIntMax = (UInt16) ( connectIntervalMaxMs / 1.25 );
            var timeout = (UInt16) ( supervisionTimeoutMs / 10 );

            var cmd = _bglib.BLECommandGAPConnectDirect(btaddr, type, connIntMin, connIntMax, timeout, latency);
            await _bglib.SendCommandAsync(cmd).ConfigureAwait( false );

             using ( var cts = new CancellationTokenSource(TIMEOUT_MS) ) {
                bool didTimeout = false;
                try { await _cmdRespWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false ); }
                catch ( OperationCanceledException ) { didTimeout = true; }
                if (didTimeout) throw new TimeoutException();
            }

            if (_lastResult != 0) {
                var error = Enum.ToObject(typeof(BGErrorCode), _lastResult);
                string errMsg = $"GAPConnectDirect returned: error={error} code=0x{_lastResult:X4}";
                throw new Exception(errMsg);
            }

            byte connectionHandle = ((ConnectDirectEventArgs) _lastResponseArgs).connection_handle;
            return connectionHandle;
        }

        public async Task< byte > ATTClientReadByGroupTypeAsync(byte connectionHandle, ushort handleRangeStart, ushort handleRangeEnd, byte[] primaryUUIDBytes) {
            const int TIMEOUT_MS = 6000;

            var cmd = _bglib.BLECommandATTClientReadByGroupType(connectionHandle, handleRangeStart, handleRangeEnd, primaryUUIDBytes);

            await _bglib.SendCommandAsync(cmd).ConfigureAwait( false );

             using ( var cts = new CancellationTokenSource(TIMEOUT_MS) ) {
                bool didTimeout = false;
                try { await _cmdRespWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false ); }
                catch ( OperationCanceledException ) { didTimeout = true; }
                if (didTimeout) throw new TimeoutException();
            }

            if (_lastResult != 0) {
                var error = Enum.ToObject(typeof(BGErrorCode), _lastResult);
                string errMsg = $"ATTClientReadByGroupType returned: error={error} code={_lastResult:X4}";
                throw new Exception(errMsg);
            }

            byte conHandle = ((ReadByGroupTypeEventArgs) _lastResponseArgs).connection;
            return conHandle;
        }

        public async Task< byte > ATTClientFindInformationAsync(byte connectionHandle, UInt16 handleRangeStart, UInt16 handleRangeEnd) {
            const int TIMEOUT_MS = 6000;

            var cmd = _bglib.BLECommandATTClientFindInformation(connectionHandle, handleRangeStart, handleRangeEnd);

            await _bglib.SendCommandAsync(cmd).ConfigureAwait( false );

             using ( var cts = new CancellationTokenSource(TIMEOUT_MS) ) {
                bool didTimeout = false;
                try { await _cmdRespWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false ); }
                catch ( OperationCanceledException ) { didTimeout = true; }
                if (didTimeout) throw new TimeoutException();
            }

            if (_lastResult != 0) {
                var error = Enum.ToObject(typeof(BGErrorCode), _lastResult);
                string errMsg = $"ATTClientFindInformation returned: error={error} code={_lastResult:X4}";
                throw new Exception(errMsg);
            }

            byte conHandle = ((FindInformationEventArgs) _lastResponseArgs).connection;
            return conHandle;
        }

        public async Task< byte > ATTClientReadByHandleAsync(byte connectionHandle, UInt16 characteristicUUID) {
            const int TIMEOUT_MS = 4000;

            var cmd = _bglib.BLECommandATTClientReadByHandle(connectionHandle, characteristicUUID);

            await _bglib.SendCommandAsync(cmd).ConfigureAwait( false );

             using ( var cts = new CancellationTokenSource(TIMEOUT_MS) ) {
                bool didTimeout = false;
                try { await _cmdRespWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false ); }
                catch ( OperationCanceledException ) { didTimeout = true; }
                if (didTimeout) throw new TimeoutException();
            }

            if (_lastResult != 0) {
                var error = Enum.ToObject(typeof(BGErrorCode), _lastResult);
                string errMsg = $"ATTClientReadByHandle returned: error={error} code={_lastResult:X4}";
                throw new Exception(errMsg);
            }

            byte conHandle = ((ReadByHandleEventArgs) _lastResponseArgs).connection;
            return conHandle;
        }

        public async Task< byte > ATTClientReadByTypeAsync(byte connectionHandle, UInt16 startATTHandle, UInt16 endATTHandle, IEnumerable<byte> attributeTypeUUID) {
            const int TIMEOUT_MS = 6000;

            var cmd = _bglib.BLECommandATTClientReadByType(
                connectionHandle, 
                startATTHandle, 
                endATTHandle, 
                attributeTypeUUID.ToArray()
            );

            await _bglib.SendCommandAsync(cmd).ConfigureAwait( false );

             using ( var cts = new CancellationTokenSource(TIMEOUT_MS) ) {
                bool didTimeout = false;
                try { await _cmdRespWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false ); }
                catch ( OperationCanceledException ) { didTimeout = true; }
                if (didTimeout) throw new TimeoutException();
            }

            if (_lastResult != 0) {
                var error = Enum.ToObject(typeof(BGErrorCode), _lastResult);
                string errMsg = $"ATTClientReadByType returned: error={error} code={_lastResult:X4}";
                throw new Exception(errMsg);
            }

            byte conHandle = ((ReadByTypeEventArgs) _lastResponseArgs).connection;
            return conHandle;
        }

        public async Task< byte > ATTClientAttributeWriteAsync(byte connectionHandle, UInt16 attHandle, IEnumerable<byte> value) {
            const int TIMEOUT_MS = 6000;

            var cmd = _bglib.BLECommandATTClientAttributeWrite(connectionHandle, attHandle, value.ToArray());

            await _bglib.SendCommandAsync(cmd).ConfigureAwait( false );

             using ( var cts = new CancellationTokenSource(TIMEOUT_MS) ) {
                bool didTimeout = false;
                try { await _cmdRespWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false ); }
                catch ( OperationCanceledException ) { didTimeout = true; }
                if (didTimeout) throw new TimeoutException();
            }

            if (_lastResult != 0) {
                var error = Enum.ToObject(typeof(BGErrorCode), _lastResult);
                string errMsg = $"ATTClientAttributeWrite returned: error={error} code={_lastResult:X4}";
                throw new Exception(errMsg);
            }

            byte conHandle = ((AttributeWriteEventArgs) _lastResponseArgs).connection;
            return conHandle;
        }

        public async Task< byte > ConnectionDisconnectAsync(byte connectionHandle) {
            const int TIMEOUT_MS = 6000;

            var cmd = _bglib.BLECommandConnectionDisconnect(connectionHandle);

            await _bglib.SendCommandAsync(cmd).ConfigureAwait( false );

            using ( var cts = new CancellationTokenSource(TIMEOUT_MS) ) {
                bool didTimeout = false;
                try { await _cmdRespWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false ); }
                catch ( OperationCanceledException ) { didTimeout = true; }
                if (didTimeout) throw new TimeoutException();
            }

            if (_lastResult != 0) {
                var error = Enum.ToObject(typeof(BGErrorCode), _lastResult);
                string errMsg = $"ConnectionDisconnect returned: error={error} code={_lastResult:X4}";
                throw new Exception(errMsg);
            }

            byte conHandle = ((DisconnectEventArgs) _lastResponseArgs).connection;
            return conHandle;
        }

        public async Task< byte[] > AddressGetAsync() {

            const int TIMEOUT_MS = 4000;

            var cmd = _bglib.BLECommandSystemAddressGet();

            await _bglib.SendCommandAsync(cmd).ConfigureAwait( false );

             using ( var cts = new CancellationTokenSource(TIMEOUT_MS) ) {
                bool didTimeout = false;
                try { await _cmdRespWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false ); }
                catch ( OperationCanceledException ) { didTimeout = true; }
                if (didTimeout) throw new TimeoutException();
            }

            byte[] btaddr = ((AddressGetEventArgs) _lastResponseArgs).address;
            return btaddr.Reverse().ToArray();
        }

        public async Task< byte > RegReadAsync(UInt16 address) {

            const int TIMEOUT_MS = 6000;

            var cmd = _bglib.BLECommandSystemRegRead(address);

            await _bglib.SendCommandAsync(cmd).ConfigureAwait( false );

             using ( var cts = new CancellationTokenSource(TIMEOUT_MS) ) {
                bool didTimeout = false;
                try { await _cmdRespWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false ); }
                catch ( OperationCanceledException ) { didTimeout = true; }
                if (didTimeout) throw new TimeoutException();
            }

            byte value = ((RegReadEventArgs) _lastResponseArgs).value;
            return value;
        }

        public async Task SystemResetAsync(bool toDFUMode = false) {
            // Must reconstruct this api lib after this command to continue use
            const int RESET_WAIT_MS = 3000;

            var cmd = _bglib.BLECommandSystemReset((byte) (toDFUMode ? 0x01 : 0x00));

            await _bglib.SendCommandAsync(cmd).ConfigureAwait( false );

            //_serialDevice.Dispose(); 
            // TODO: replace this with a state reset command, then I can keep using this instance;

            await Task.Delay( RESET_WAIT_MS ).ConfigureAwait( false );   
        }

        public void Dispose() {
            // TODO: Remove dispose pattern from this class
            Debug.WriteLine( "BGLibApi Dispose() Called" );
            if ( IsDisposed ) return;
            IsDisposed = true;      
            GC.SuppressFinalize( this );    
             
            try {
                _serialDevice.DataReceived -= Serial_DataReceived;
                if ( _serialRxConsumerTask != null ) {
                    StopSerialRxConsumerAsync().Wait( 2000 ); // TODO: change back to 4 seconds if needed
                }
            }
            catch ( Exception ex ) {
                Debug.WriteLine( ex, "Error while attempting to stop SerialRx consumer Task" );
            }
            if ( _bglib != null ) {
                UnsubscribeFromEvents();
                UnsubscribeFromCommandResponses();
            }
            
            _serialRxQueue?.Dispose();
        }

        private void SubscribeToCommandResponses() {
            _bglib.BLEResponseGAPSetScanParameters += Bglib_BLEResponseGAPSetScanParameters;
            _bglib.BLEResponseGAPDiscover += Bglib_BLEResponseGAPDiscover;
            _bglib.BLEResponseGAPEndProcedure += Bglib_BLEResponseGAPEndProcedure;
            _bglib.BLEResponseGAPConnectDirect += Bglib_BLEResponseGAPConnectDirect;
            _bglib.BLEResponseATTClientReadByGroupType += Bglib_BLEResponseATTClientReadByGroupType;
            _bglib.BLEResponseATTClientFindInformation += Bglib_BLEResponseATTClientFindInformation;
            _bglib.BLEResponseATTClientReadByHandle += Bglib_BLEResponseATTClientReadByHandle;
            _bglib.BLEResponseATTClientReadByType += Bglib_BLEResponseATTClientReadByType;
            _bglib.BLEResponseATTClientAttributeWrite += Bglib_BLEResponseATTClientAttributeWrite;
            _bglib.BLEResponseConnectionDisconnect += Bglib_BLEResponseConnectionDisconnect;
            _bglib.BLEResponseSystemRegRead += Bglib_BLEResponseSystemRegRead;
            _bglib.BLEResponseSystemAddressGet += Bglib_BLEResponseSystemAddressGet;
            _bglib.BLEResponseSystemHello += Bglib_BLEResponseSystemHello;
        }       

        private void SubscribeToEvents() {
            _bglib.BLEEventGAPScanResponse += Bglib_BLEEventGAPScanResponse;
            _bglib.BLEEventConnectionStatus += Bglib_BLEEventConnectionStatus;
            _bglib.BLEEventATTClientGroupFound += Bglib_BLEEventATTClientGroupFound;
            _bglib.BLEEventATTClientProcedureCompleted += Bglib_BLEEventATTClientProcedureCompleted;
            _bglib.BLEEventATTClientFindInformationFound += Bglib_BLEEventATTClientFindInformationFound;
            _bglib.BLEEventATTClientAttributeValue += Bglib_BLEEventATTClientAttributeValue;
            _bglib.BLEEventATTClientIndicated += Bglib_BLEEventATTClientIndicated;
            _bglib.BLEEventConnectionDisconnected += Bglib_BLEEventConnectionDisconnected;
        }

        private void UnsubscribeFromCommandResponses() {
            _bglib.BLEResponseGAPSetScanParameters -= Bglib_BLEResponseGAPSetScanParameters;
            _bglib.BLEResponseGAPDiscover -= Bglib_BLEResponseGAPDiscover;
            _bglib.BLEResponseGAPEndProcedure -= Bglib_BLEResponseGAPEndProcedure;
            _bglib.BLEResponseGAPConnectDirect -= Bglib_BLEResponseGAPConnectDirect;
            _bglib.BLEResponseATTClientReadByGroupType -= Bglib_BLEResponseATTClientReadByGroupType;
            _bglib.BLEResponseATTClientFindInformation -= Bglib_BLEResponseATTClientFindInformation;
            _bglib.BLEResponseATTClientReadByHandle -= Bglib_BLEResponseATTClientReadByHandle;
            _bglib.BLEResponseATTClientReadByType -= Bglib_BLEResponseATTClientReadByType;
            _bglib.BLEResponseATTClientAttributeWrite -= Bglib_BLEResponseATTClientAttributeWrite;
            _bglib.BLEResponseSystemRegRead -= Bglib_BLEResponseSystemRegRead;
            _bglib.BLEResponseSystemAddressGet -= Bglib_BLEResponseSystemAddressGet;
            _bglib.BLEResponseSystemHello -= Bglib_BLEResponseSystemHello;
        }

        private void UnsubscribeFromEvents() {
            _bglib.BLEEventGAPScanResponse -= Bglib_BLEEventGAPScanResponse;
            _bglib.BLEEventConnectionStatus -= Bglib_BLEEventConnectionStatus;
            _bglib.BLEEventATTClientGroupFound -= Bglib_BLEEventATTClientGroupFound;
            _bglib.BLEEventATTClientProcedureCompleted -= Bglib_BLEEventATTClientProcedureCompleted;
            _bglib.BLEEventATTClientFindInformationFound -= Bglib_BLEEventATTClientFindInformationFound;
            _bglib.BLEEventATTClientAttributeValue -= Bglib_BLEEventATTClientAttributeValue;
            _bglib.BLEEventATTClientIndicated -= Bglib_BLEEventATTClientIndicated;
        }

        private int ConvertMsTo625UsUnits(int milliseconds) {
            return milliseconds*1000/625;
        }

        private void Serial_DataReceived(object sender, IReadOnlyList< byte > data) {
            Debug.Assert( data != null );
            _serialRxQueue.Add( data.ToArray() );
        } 

        private async Task StopSerialRxConsumerAsync() {
            _serialRxConsumerTaskCTS?.Cancel();
            try {
                await _serialRxConsumerTask.ConfigureAwait( false );
            }
            catch ( OperationCanceledException ) {}
            catch ( Exception ex ) {
                Debug.Fail( $"{nameof(BGLibApi)}'s serial consumer task failed during execution" );
            }
            finally {
                _serialRxConsumerTaskCTS?.Dispose();
            }
        } 

        private void StartSerialRxConsumerTask() {
            
            _serialRxConsumerTaskCTS = new CancellationTokenSource();
            var cancelToken = _serialRxConsumerTaskCTS.Token;

            _serialRxConsumerTask = Task.Factory.StartNew( () => {           
                while ( true ) {                
                    cancelToken.ThrowIfCancellationRequested();   
                    if ( _serialRxQueue.IsCompleted ) 
                        throw new Exception( "SerialRxQueue has been completed" );
                    if ( IsDisposed ) return;

                    byte[] chunk;
                    var success = _serialRxQueue.TryTake( out chunk, 5, cancelToken );
                    if ( success ) {
                        foreach ( var b in chunk ) {
                            _bglib.Parse( b );
                        }
                    }                    
                }
            },  cancelToken, // Recommendation via Stephen (Async man)
            //    TaskCreationOptions.RunContinuationsAsynchronously 
            //  | TaskCreationOptions.HideScheduler
           //   | TaskCreationOptions.DenyChildAttach
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default 
            );       
        }

        ~BGLibApi() {
            //Debug.Fail( "BGLibApi Dispose called from finalizer" );
            Dispose();
        }

        #region EventAndResponseHandlers
        private void Bglib_BLEResponseATTClientReadByHandle(object sender, ReadByHandleEventArgs e) {
            _lastResult = e.result;
            _lastResponseArgs = e;
            _cmdRespWaitHandle.Set();
        }

        private void Bglib_BLEResponseATTClientFindInformation(object sender, FindInformationEventArgs e) {
            _lastResult = e.result;
            _lastResponseArgs = e;
            _cmdRespWaitHandle.Set();
        }

        private void Bglib_BLEResponseATTClientReadByGroupType(object sender, ReadByGroupTypeEventArgs e) {
            _lastResult = e.result;
            _lastResponseArgs = e;
            _cmdRespWaitHandle.Set();
        }

        private void Bglib_BLEResponseGAPConnectDirect(object sender, ConnectDirectEventArgs e) {
            _lastResult = e.result;
            _lastResponseArgs = e;
            _cmdRespWaitHandle.Set();
        }

        private void Bglib_BLEResponseGAPEndProcedure(object sender, EndProcedureEventArgs e) {
            _lastResult = e.result;
            _lastResponseArgs = e;
            _cmdRespWaitHandle.Set();
        }

        private void Bglib_BLEResponseGAPDiscover(object sender, DiscoverEventArgs e) {
            _lastResult = e.result;
            _lastResponseArgs = e;
            _cmdRespWaitHandle.Set();
        }

        private void Bglib_BLEResponseGAPSetScanParameters(object sender, SetScanParametersEventArgs e) {
            _lastResult = e.result;
            _lastResponseArgs = e;
            _cmdRespWaitHandle.Set();
        }

        private void Bglib_BLEResponseATTClientAttributeWrite(object sender, AttributeWriteEventArgs e) {
            _lastResult = e.result;
            _lastResponseArgs = e;
            _cmdRespWaitHandle.Set();
        }

        private void Bglib_BLEResponseATTClientReadByType(object sender, ReadByTypeEventArgs e) {
            _lastResult = e.result;
            _lastResponseArgs = e;
            _cmdRespWaitHandle.Set();
        }

        private void Bglib_BLEResponseConnectionDisconnect(object sender, DisconnectEventArgs e) {
            _lastResult = e.result;
            _lastResponseArgs = e;
            _cmdRespWaitHandle.Set();
        }

        private void Bglib_BLEResponseSystemRegRead(object sender, RegReadEventArgs e) {
            _lastResponseArgs = e;
            _cmdRespWaitHandle.Set();
        }

        private void Bglib_BLEResponseSystemAddressGet(object sender, AddressGetEventArgs e) {
            _lastResponseArgs = e;
            _cmdRespWaitHandle.Set();
        }

        private void Bglib_BLEResponseSystemHello(object sender, HelloEventArgs e) {
            _lastResponseArgs = e;
            _cmdRespWaitHandle.Set();
        }

        private void Bglib_BLEEventATTClientAttributeValue( object sender, AttributeValueEventArgs e ) => ATTClientAttributeValue?.Invoke( this, e );

        private void Bglib_BLEEventATTClientFindInformationFound( object sender, FindInformationFoundEventArgs e ) => ATTClientFindInformationFound?.Invoke( this, e );

        private void Bglib_BLEEventATTClientProcedureCompleted( object sender, ProcedureCompletedEventArgs e ) => ATTClientProcedureCompleted?.Invoke( this, e );

        private void Bglib_BLEEventATTClientGroupFound( object sender, GroupFoundEventArgs e ) => ATTClientGroupFound?.Invoke( this, e );

        private void Bglib_BLEEventConnectionStatus( object sender, StatusEventArgs e ) => ConnectionStatus?.Invoke( this, e );

        private void Bglib_BLEEventGAPScanResponse( object sender, ScanResponseEventArgs e ) => GAPScanResponse?.Invoke( this, e );

        private void Bglib_BLEEventATTClientIndicated( object sender, IndicatedEventArgs e ) => ATTClientIndicated?.Invoke( this, e );

        private void Bglib_BLEEventConnectionDisconnected( object sender, DisconnectedEventArgs e ) => ConnectionDisconnected?.Invoke( this, e );
        #endregion

    }

}