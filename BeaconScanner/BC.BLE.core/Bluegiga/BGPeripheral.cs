using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

using BlueCats.Bluetooth.Core.Base;
using BlueCats.Bluetooth.Core.Base.Models.Enums;
using BlueCats.Bluetooth.Core.Base.Models.EventArgs;
using BlueCats.Bluetooth.Core.Models.Enums;
using BlueCats.Tools.Portable.Lib.Bluegiga;
using BlueCats.Tools.Portable.Lib.Bluegiga.BLE.Events.ATTClient;
using BlueCats.Tools.Portable.Util;



using Nito.AsyncEx;

namespace BlueCats.Bluetooth.Core.Bluegiga {

    internal class BGPeripheral : Peripheral {

        internal BGPeripheral(string address, bool isPublicAddress, BGLibApi bgApi, sbyte rssi = 0, CentralManager source = null) 
            : base(address, isPublicAddress, rssi, source) {
            
            _bgApi = bgApi;
            _procedureCompletedWaitHandle = new AsyncAutoResetEvent(false);
        }

        private const UInt16 HANDLE_RANGE_START = 0x0001;
        private const UInt16 HANDLE_RANGE_END = 0xFFFF;

       
        private readonly BGLibApi _bgApi;
        private readonly AsyncAutoResetEvent _procedureCompletedWaitHandle;
        private ExceptionDispatchInfo _exceptionToRethrow;
        private List<byte[]> _targetServiceUUIDs;
        private CancellationTokenSource _cancellationSource;

        public override sbyte ReadRSSI() {
            // TODO: Implement - read and update internal value
            throw new NotImplementedException();
        }

        public override async Task< IReadOnlyCollection< Service > > DiscoverServicesAsync( IEnumerable< byte[] > serviceUUIDs = null, int timeoutMs = 4000 ) {

            switch ( State ) {
                case PeripheralState.Disconnected: throw new Exception( "No connection");
                case PeripheralState.Connecting:   throw new Exception( $"Peripheral is in {PeripheralState.Connecting} State, cannot discover Services yet");
                case PeripheralState.Connected:    break;
                case PeripheralState.Disposed:     throw new ObjectDisposedException( nameof(BGPeripheral) );
                default: throw new ArgumentOutOfRangeException();
            }

            void Init() {
                ClearServices();
                _bgApi.ATTClientGroupFound += BGApi_ATTClientGroupFound;
                _bgApi.ATTClientProcedureCompleted += BGApi_ATTClientProcedureCompleted;
                StateChanged += Peripheral_StateChanged;
                _targetServiceUUIDs = serviceUUIDs?.ToList() ?? new List< byte[] >();
            }

            void Cleanup() {
                _bgApi.ATTClientGroupFound -= BGApi_ATTClientGroupFound;
                _bgApi.ATTClientProcedureCompleted -= BGApi_ATTClientProcedureCompleted;
                StateChanged -= Peripheral_StateChanged;
                _targetServiceUUIDs = new List< byte[] >();
                _exceptionToRethrow = null;
            }

            try {

                ThrowIfDisposed();
                ThrowIfNotConnected();
                Init();

                var primaryUUIDBytes = BitConverter.GetBytes( (UInt16) GATTAttributeType.ServicePrimaryUUID );

                await _bgApi.ATTClientReadByGroupTypeAsync(
                    ConnectionHandle,
                    HANDLE_RANGE_START,
                    HANDLE_RANGE_END,
                    primaryUUIDBytes
                ).ConfigureAwait( false );

                _cancellationSource = new CancellationTokenSource( timeoutMs );
                try {
                    await _procedureCompletedWaitHandle.WaitAsync( _cancellationSource.Token )
                        .ConfigureAwait( false );
                    _exceptionToRethrow?.Throw();
                }
                catch ( OperationCanceledException ) {
                    if ( State == PeripheralState.Disconnected ) {
                        throw new Exception( "BLE connection was dropped" );
                    }
                    throw new TimeoutException( "Timeout while attempting to discover services" );
                }
                    
                return Services;
            }
            finally {
                Cleanup();
            }
        }

        private void Peripheral_StateChanged( object sender, PeripheralStateChangedEventArgs e ) {
            Debug.WriteLine( $"BGPeripheral State changed to: {e.NewState}" );
            switch ( e.NewState ) {
                case PeripheralState.Disconnected:
                    _cancellationSource?.Cancel();
                    break;
                case PeripheralState.Connecting:
                    break;
                case PeripheralState.Connected:
                    break;
                case PeripheralState.Disposed:
                    _cancellationSource?.Cancel();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected override void Dispose( bool disposing ) {
            const string TYPE_NAME = nameof( BGPeripheral );
            const string MEMBER_NAME = nameof( Dispose );

            if ( State == PeripheralState.Disposed ) return;
            try {
                base.Dispose( disposing );
            }
            catch ( Exception ex ) {                
                Debug.Fail( $"Error in {TYPE_NAME}::{MEMBER_NAME} while calling base.Dispose()): {ex.Message}" );
            }
            State = PeripheralState.Disposed;

            if ( _bgApi != null ) {
                _bgApi.ATTClientGroupFound -= BGApi_ATTClientGroupFound;
                _bgApi.ATTClientProcedureCompleted -= BGApi_ATTClientProcedureCompleted;
            }
        }

        private void BGApi_ATTClientProcedureCompleted( object sender, ProcedureCompletedEventArgs e ) {

            try {
                if ( State == PeripheralState.Disposed ) return;

                Debug.WriteLine( $"BGPeripheral->ProcedureCompleted: conn={e.connection:X2} atthandle={e.atthandle:X4} result={e.result:X4}" );
                if ( e.result != 0x00 )
                    throw new Exception( $"ATTClientProcedureCompleted with error code: 0x{e.result:X4}" );
            }
            catch ( Exception ex ) {
                if ( _exceptionToRethrow == null )
                    _exceptionToRethrow = ExceptionDispatchInfo.Capture( ex );
            }
            finally {
                _procedureCompletedWaitHandle.Set();
            }
        }

        private void BGApi_ATTClientGroupFound(object sender, GroupFoundEventArgs e) {

            try {
                if ( State == PeripheralState.Disposed ) return;

                var serviceUUID = e.uuid.Reverse().ToArray();
                Debug.WriteLine( $"Found service: {serviceUUID.ToHexString()}" );
                if ( _targetServiceUUIDs != null && _targetServiceUUIDs.Any( bytes => bytes.SequenceEqual( e.uuid ) ) ) {
                    AddService( new BGService( this, e.uuid, e.start, e.end, _bgApi ) );
                    Debug.WriteLine( $"Created and added service: {serviceUUID.ToHexString()}" );
                }
            }
            catch ( Exception ex ) {
                if ( _exceptionToRethrow == null )
                _exceptionToRethrow = ExceptionDispatchInfo.Capture( ex );
            }
        }

    }

}