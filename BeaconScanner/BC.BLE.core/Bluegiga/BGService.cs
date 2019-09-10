using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

using BlueCats.Bluetooth.Core.Base;
using BlueCats.Bluetooth.Core.Base.Models.Enums;
using BlueCats.Bluetooth.Core.Models.Enums;
using BlueCats.Tools.Portable.Lib.Bluegiga;
using BlueCats.Tools.Portable.Lib.Bluegiga.BLE.Events.ATTClient;
using BlueCats.Tools.Portable.Lib.Bluegiga.Models.Enums;
using BlueCats.Tools.Portable.Util;



using Nito.AsyncEx;

namespace BlueCats.Bluetooth.Core.Bluegiga {

    internal class BGService : Service {

        internal BGService(Peripheral localPeripheral, IEnumerable<byte> uuid, UInt16 startAttHandle, UInt16 endAttHandle, BGLibApi bgApi) 
            : base(localPeripheral, uuid, startAttHandle, endAttHandle) {

            _bgApi = bgApi;
            _procedureCompletedWaitHandle = new AsyncAutoResetEvent(false);
            _clientConfigurationAttributeHandles = new List<UInt16>();
        }

       
        private readonly BGLibApi _bgApi;
        private ExceptionDispatchInfo _exceptionToRethrow;
        private readonly AsyncAutoResetEvent _procedureCompletedWaitHandle;
        private readonly List< UInt16 > _clientConfigurationAttributeHandles;
        private GATTAttributeType _uuidOfAttributeBeingRead;

        public override async Task< IReadOnlyCollection< Characteristic > > DiscoverCharacteristicsAsync( IList< byte[] > characteristicUUIDs = null, int timeoutMs = 10000 ) {

            try {
                ThrowIfDisposed();
                ThrowIfNotConnected();

                if ( State != ServiceState.Idle )
                    throw new Exception( "Already busy discovering services" );
                State = ServiceState.DiscoveringCharacteristics;

                ClearCharacteristics();
                _bgApi.ATTClientAttributeValue += BGApi_ATTClientAttributeValue;
                _bgApi.ATTClientProcedureCompleted += BGApi_ATTClientProcedureCompleted;
                _clientConfigurationAttributeHandles.Clear();

                // Discover characteristic declaration attributes
                await ReadAttributeByType( 
                    GATTAttributeType.CharacteristicUUID, 
                    timeoutMs / 2 
                ).ConfigureAwait( false );

                if ( !Characteristics.Any() )
                    return Characteristics;

                 // Discover client characteristic configuration attributes
                await ReadAttributeByType( 
                    GATTAttributeType.CharacteristicClientConfigurationUUID, 
                    timeoutMs / 2 
                ).ConfigureAwait( false );

                // now determine which characteristic each client characteristic configuration handle belongs to
                if ( ( _clientConfigurationAttributeHandles.Count == 1 ) && ( Characteristics.Count == 1 ) ) {

                    Characteristics.First().CharacteristicClientConfigurationHandle =
                        _clientConfigurationAttributeHandles.First();
                }
                else if ( ( _clientConfigurationAttributeHandles.Count > 0 ) && ( Characteristics.Count > 0 ) ) {
                    // sort the characteristic list by handle (ascending)
                    var charsAscending = Characteristics.OrderBy(
                        c => c.CharacteristicDeclarationHandle ).ToArray();

                    // for each config handle, find the closest characteristic handle going up in values
                    foreach ( var cfgHnd in _clientConfigurationAttributeHandles ) {

                        Characteristic charWhereCurrConfigHandleBelongs = null;
                        for ( int i = 1; i < charsAscending.Length; i++ ) {

                            var currCharHnd = charsAscending[ i ].CharacteristicDeclarationHandle;
                            var lastCharHnd = charsAscending[ i - 1 ].CharacteristicDeclarationHandle;

                            if ( ( cfgHnd > lastCharHnd ) && ( cfgHnd < currCharHnd ) ) {
                                charWhereCurrConfigHandleBelongs = charsAscending[ i - 1 ];
                                break;
                            }
                        }
                        if ( charWhereCurrConfigHandleBelongs == null )
                            charWhereCurrConfigHandleBelongs = charsAscending.Last();
                        charWhereCurrConfigHandleBelongs.CharacteristicClientConfigurationHandle = cfgHnd;
                    }
                }
            }
            finally {
                _bgApi.ATTClientAttributeValue -= BGApi_ATTClientAttributeValue;
                _bgApi.ATTClientProcedureCompleted -= BGApi_ATTClientProcedureCompleted;
                _exceptionToRethrow = null;
                _uuidOfAttributeBeingRead = 0;
                _clientConfigurationAttributeHandles.Clear();
            }

            State = ServiceState.Idle;

            var results = FilterCharacteristicsByUUIDs( Characteristics, characteristicUUIDs );
            return results;
        }

        protected override void Dispose(bool disposing) {
            try {
                if (State == ServiceState.Disposed) return;
                base.Dispose(disposing);
                State = ServiceState.Disposed;

                _bgApi.ATTClientAttributeValue -= BGApi_ATTClientAttributeValue;
                _bgApi.ATTClientProcedureCompleted -= BGApi_ATTClientProcedureCompleted;

            } catch (Exception ex) {
                Debug.WriteLine(ex, "Error while disposing: ");
            }
        }

        private IReadOnlyCollection< Characteristic > FilterCharacteristicsByUUIDs( IReadOnlyCollection< Characteristic > characteristics, ICollection< byte[] > characteristicUUIDs ) {
            if ( characteristicUUIDs == null || characteristicUUIDs.Count == 0 ) {
                return new List< Characteristic >();
            }
            var filteredCharacteristics = (
                from ch in characteristics
                where characteristicUUIDs.Any(uuid => ch.UUID.SequenceEqual(uuid))
                select ch
            ).ToList();
            return filteredCharacteristics;
        }

        private async Task ReadAttributeByType( GATTAttributeType attributeType, int timeoutMs ) {

            Debug.WriteLine( $"Discovering characteristics of type: {attributeType}" );
            _uuidOfAttributeBeingRead = attributeType;

            await _bgApi.ATTClientReadByTypeAsync(
                ConnectionHandle,
                StartATTHandle,
                EndATTHandle,
                BitConverter.GetBytes( (UInt16) _uuidOfAttributeBeingRead )
            ).ConfigureAwait( false );

            using ( var cancel = new CancellationTokenSource( timeoutMs ) ) {
                try {
                    await _procedureCompletedWaitHandle
                        .WaitAsync( cancel.Token )
                        .ConfigureAwait( false );
                    _exceptionToRethrow?.Throw();
                }
                catch ( OperationCanceledException ) {
                    throw new TimeoutException( $"Timeout occured while discovering characteristics of type: {attributeType}" );  
                }
            }
        }

        private void BGApi_ATTClientAttributeValue( object sender, AttributeValueEventArgs e ) {

            try {
                if ( State == ServiceState.Disposed ) return;

                Debug.WriteLine( $"Found attribute: conn={e.connection:X2} attuuid={e.atthandle:X4} type={e.type:X2} value={e.value.ToHexString( false, "-" )} " );

                switch ( _uuidOfAttributeBeingRead ) {

                    case GATTAttributeType.CharacteristicUUID:
                        var characteristic = new BGCharacteristic( LocalPeripheral, _bgApi ) {
                            CharacteristicDeclarationHandle = (UInt16) e.atthandle
                        };
                        characteristic.ParseCharacteristicDeclarationData( e.value );
                        AddCharacteristic( characteristic );
                        Debug.WriteLine( "Created and added characteristic object: " + characteristic.UUID.ToHexString( true ) );
                        break;

                    case GATTAttributeType.CharacteristicClientConfigurationUUID:
                        _clientConfigurationAttributeHandles.Add( (UInt16) e.atthandle );
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch ( Exception ex ) {
                if ( _exceptionToRethrow == null )
                    _exceptionToRethrow = ExceptionDispatchInfo.Capture( ex );
            }
        }

        private void BGApi_ATTClientProcedureCompleted( object sender, ProcedureCompletedEventArgs e ) {

            try {
                if ( State == ServiceState.Disposed ) return;

                Debug.WriteLine( $"BGService->ProcedureCompleted: conn={e.connection:X2} atthandle={e.atthandle:X4} result={e.result:X4}" );
                if ( e.result != (UInt16) BGErrorCode.NoError ) {

                    var error = (BGErrorCode) Enum.ToObject( typeof( BGErrorCode ), e.result );

                    if ( error != BGErrorCode.AttributeNotFound )
                        throw new Exception( $"ATTClientProcedureCompleted with error={error} code=0x{e.result:X4}" );
                }
            }
            catch ( Exception ex ) {
                if ( _exceptionToRethrow == null )
                    _exceptionToRethrow = ExceptionDispatchInfo.Capture( ex );
            }
            finally {
                _procedureCompletedWaitHandle.Set();
            }
        }

    }

}