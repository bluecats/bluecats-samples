using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BlueCats.Bluetooth.Core.Base;
using BlueCats.Bluetooth.Core.Base.Models.Enums;
using BlueCats.Tools.Portable.Lib.Bluegiga;
using BlueCats.Tools.Portable.Lib.Bluegiga.BLE.Events.ATTClient;
using BlueCats.Tools.Portable.Lib.Bluegiga.Models.Enums;
using BlueCats.Tools.Portable.Util;



using Nito.AsyncEx;

namespace BlueCats.Bluetooth.Core.Bluegiga {

    internal class BGCharacteristic : Characteristic {

        internal BGCharacteristic(Peripheral localPeripheral, BGLibApi bgApi)
            : base(localPeripheral) {

            _bgApi = bgApi;
            _procedureCompletedWaitHandle = new AsyncAutoResetEvent( false );
            _attributeValueWaitHandle = new AsyncAutoResetEvent( false );
            _bgApi.ATTClientAttributeValue += BGApi_NotificationAndIndicationHandler;
        }

       
        private readonly BGLibApi _bgApi;
        private readonly AsyncAutoResetEvent _procedureCompletedWaitHandle;
        private readonly AsyncAutoResetEvent _attributeValueWaitHandle;
        private ExceptionDispatchInfo _exceptionToRethrow;
        private byte[] _valueOfLastAttributeRead;
        private UInt16 _handleOfLastAccessedAttribute;

        protected override void Dispose(bool disposing) {
            try {
                lock ( StateLock ) {
                    if (State == CharacteristicState.Disposed) return;
                    base.Dispose(disposing);
                    State = CharacteristicState.Disposed;
                }

                _bgApi.ATTClientAttributeValue -= BGApi_NotificationAndIndicationHandler;
                _bgApi.ATTClientAttributeValue -= BGApi_ATTClientAttributeValue;
                _bgApi.ATTClientProcedureCompleted -= BGApi_ATTClientProcedureCompleted;

            } catch (Exception ex) {
                Debug.WriteLine(ex, "Error while disposing: ");
            }
        }

        protected override async Task<byte[]> ReadFromAttributeAsync(ushort attHandle, int timeoutMs = 3000) {

            if (IsBusy) throw new Exception($"Cannot execute read, characteristic is already busy {State}");
            Debug.WriteLine($"Reading from attribute with handle={attHandle:X4}");

            Action init = () => {   
                State = CharacteristicState.Reading;              
                _bgApi.ATTClientAttributeValue += BGApi_ATTClientAttributeValue;
                _bgApi.ATTClientProcedureCompleted += BGApi_ATTClientProcedureCompleted;
            };
            Action cleanup = () => {
                State = CharacteristicState.Idle;
                _bgApi.ATTClientAttributeValue -= BGApi_ATTClientAttributeValue;
                _bgApi.ATTClientProcedureCompleted -= BGApi_ATTClientProcedureCompleted;
                _exceptionToRethrow = null;
            };

            try {
                ThrowIfDisposed();
                ThrowIfNotConnected();
                init();

                await _bgApi.ATTClientReadByHandleAsync( ConnectionHandle, attHandle ).ConfigureAwait( false );

                Task firstToComplete;
                Task procedureCompletedWaitTask;

                using ( var cts = new CancellationTokenSource( timeoutMs ) ) {
                    procedureCompletedWaitTask = _procedureCompletedWaitHandle.WaitAsync( cts.Token );
                    var attributeValueWaitTask = _attributeValueWaitHandle.WaitAsync( cts.Token );
                    try {
                        firstToComplete = await Task.WhenAny( 
                            procedureCompletedWaitTask, 
                            attributeValueWaitTask
                        ).ConfigureAwait( false );

                        if ( firstToComplete.IsCanceled ) 
                            throw new OperationCanceledException();
                        _exceptionToRethrow?.Throw();
                    }
                    catch ( OperationCanceledException ) {
                        throw new TimeoutException("Timeout occured while reading from BLE attribute");
                    }
                    finally { cts.Cancel(); }
                }

                if ( firstToComplete ==  procedureCompletedWaitTask ) {
                    throw new Exception( $"Failed to read from characteristic: {CharacteristicValueHandle}" );
                }

                if (_handleOfLastAccessedAttribute != attHandle)
                    throw new Exception("Attribute handle of value read does not match target attribute handle");
              
                Debug.WriteLine("Completed ReadAsync");
                return _valueOfLastAttributeRead ?? new byte[0];
            }
            finally {
                cleanup();
            }
        }

        protected override async Task WriteToAttributeAsync( ushort attHandle, IList< byte > data, int timeoutMs = 5000 ) {

            if ( IsBusy ) throw new Exception( $"Cannot execute write, characteristic is already busy {State}" );
            Debug.WriteLine( $"Writing to attribute with handle={attHandle:X4} data={data.ToHexString( false, ":" )}" );

            Action init = () => {
                State = CharacteristicState.Writing;
                _bgApi.ATTClientProcedureCompleted += BGApi_ATTClientProcedureCompleted;
            };
            Action cleanup = () => {
                State = CharacteristicState.Idle;
                _bgApi.ATTClientProcedureCompleted -= BGApi_ATTClientProcedureCompleted;
                _exceptionToRethrow = null;
            };

            try {
                ThrowIfDisposed();
                ThrowIfNotConnected();
                init();

                await _bgApi.ATTClientAttributeWriteAsync( 
                    ConnectionHandle, 
                    attHandle, 
                    data 
                ).ConfigureAwait( false );

                using ( var cts = new CancellationTokenSource( timeoutMs ) ) {
                    try {
                        await _procedureCompletedWaitHandle.WaitAsync( cts.Token ).ConfigureAwait( false );
                        _exceptionToRethrow?.Throw();
                    }
                    catch ( OperationCanceledException ) {
                        throw new TimeoutException( "Timeout occured while writing to BLE attribute" );
                    }
                }

                if ( _handleOfLastAccessedAttribute != attHandle )
                    throw new Exception( "Attribute handle of value written does not match target attribute handle" );
               
                Debug.WriteLine( "Completed WriteAsync" );
            }
            finally {
                cleanup();
            }
        }

        private void BGApi_ATTClientProcedureCompleted( object sender, ProcedureCompletedEventArgs e ) {
            try {
                lock ( StateLock )
                    if ( State == CharacteristicState.Disposed ) return;

                Debug.WriteLine( $"BGCharacteristic->ProcedureCompleted: conn={e.connection:X2} atthandle={e.atthandle:X4} result={e.result:X4}" );
                if ( e.result != 0x00 )
                    throw new Exception( $"ATTClientProcedureCompleted with error code: 0x{e.result:X4}" );

                _handleOfLastAccessedAttribute = e.atthandle;
            }
            catch ( Exception ex ) {
                if ( _exceptionToRethrow == null )
                    _exceptionToRethrow = ExceptionDispatchInfo.Capture( ex );
            }
            finally {
                _procedureCompletedWaitHandle.Set();
            }
        }

        private void BGApi_ATTClientAttributeValue( object sender, AttributeValueEventArgs e ) {

            try {
                lock ( StateLock )
                    if ( State == CharacteristicState.Disposed ) return;

                Debug.WriteLine( $"Read attribute: conn={e.connection:X2} atthandle={e.atthandle:X4} type={e.type:X2} value={e.value.ToHexString( false, ":" )}" );

                _handleOfLastAccessedAttribute = e.atthandle;
                _valueOfLastAttributeRead = e.value;
            }
            catch ( Exception ex ) {
                if ( _exceptionToRethrow == null )
                    _exceptionToRethrow = ExceptionDispatchInfo.Capture( ex );
            }
            finally {
                _attributeValueWaitHandle.Set();
            }
        }

        private void BGApi_NotificationAndIndicationHandler( object sender, AttributeValueEventArgs e ) {
            try {
                lock ( StateLock )
                    if ( State == CharacteristicState.Disposed ) return;

                if ( CharacteristicValueHandle == null || e.atthandle != CharacteristicValueHandle ) return;

                var type = (BGATTClientAttributeValueType) Enum.ToObject( typeof( BGATTClientAttributeValueType ), e.type );

                switch ( type ) {
                    case BGATTClientAttributeValueType.Notify:
                        Debug.WriteLine( $"Notification received: charValHand={CharacteristicValueHandle:X4} : conn={e.connection:X2} atthandle={e.atthandle:X4} type={e.type:X2} value={e.value.ToHexString( false, ":" )}" );
                        if ( !IsNotificationsEnabled ) break;
                        OnNotification( e.value );
                        break;
                    case BGATTClientAttributeValueType.Indicate:
                        Debug.WriteLine( $"Indication received: charValHand={CharacteristicValueHandle:X4} : conn={e.connection:X2} atthandle={e.atthandle:X4} type={e.type:X2} value={e.value.ToHexString( false, ":" )}" );
                        if ( !IsIndicationsEnabled ) break;
                        OnIndication( e.value );
                        break;
                }
            }
            catch ( Exception ex ) {
                if ( _exceptionToRethrow == null )
                    _exceptionToRethrow = ExceptionDispatchInfo.Capture( ex );
            }
        }
    }

}