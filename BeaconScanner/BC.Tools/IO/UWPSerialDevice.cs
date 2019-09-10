using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Windows.UI.Core;
using BlueCats.Tools.Portable.IO;
using BlueCats.Tools.Portable.Util;


using Nito.AsyncEx;
using Buffer = Windows.Storage.Streams.Buffer;
using SerialHandshake = BlueCats.Tools.Portable.IO.Models.SerialHandshake;
using SerialParity = BlueCats.Tools.Portable.IO.Models.SerialParity;
using SerialStopBitCount = BlueCats.Tools.Portable.IO.Models.SerialStopBitCount;

namespace BlueCats.Tools.UWP.IO {

    public class UWPSerialDevice : ISerialDevice {

        private UWPSerialDevice( DeviceInformation deviceInformation ) {
            DeviceInformation = deviceInformation;
            Name = DeviceInformation?.Name ?? "Unknown";
            State = SerialState.Detached;
        }

        public event EventHandler< IReadOnlyList< byte > > DataReceived;
        public event EventHandler< Exception > ErrorReceived;

        public SerialDevice InnerSerial { get; private set; }
        public DeviceInformation DeviceInformation { get; }
        public string Name { get; } 
        public string UniqueIdentifier => DeviceInformation.Id;
        public bool IsAttached => State == SerialState.Attached;
        public bool IsDisposed => State == SerialState.Disposed;
        public uint? BaudRate => InnerSerial?.BaudRate;
        public SerialParity? Parity {
            get {
                if ( InnerSerial == null ) return null;
                switch ( InnerSerial.Parity ) {
                    case Windows.Devices.SerialCommunication.SerialParity.None:
                        return SerialParity.None;
                    case Windows.Devices.SerialCommunication.SerialParity.Odd:
                        return SerialParity.Odd;
                    case Windows.Devices.SerialCommunication.SerialParity.Even:
                        return SerialParity.Even;
                    case Windows.Devices.SerialCommunication.SerialParity.Mark:
                        return SerialParity.Mark;
                    case Windows.Devices.SerialCommunication.SerialParity.Space:
                        return SerialParity.Space;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        public SerialStopBitCount? StopBitCount {
            get {
                if ( InnerSerial == null ) return null;
                switch ( InnerSerial.StopBits ) {
                    case Windows.Devices.SerialCommunication.SerialStopBitCount.One:
                        return SerialStopBitCount.One;
                    case Windows.Devices.SerialCommunication.SerialStopBitCount.OnePointFive:
                        return SerialStopBitCount.OnePointFive;
                    case Windows.Devices.SerialCommunication.SerialStopBitCount.Two:
                        return SerialStopBitCount.Two;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        public SerialHandshake? Handshake {
            get {
                if ( InnerSerial == null ) return null;
                switch ( InnerSerial.Handshake ) {
                    case Windows.Devices.SerialCommunication.SerialHandshake.None:
                        return SerialHandshake.None;
                    case Windows.Devices.SerialCommunication.SerialHandshake.RequestToSend:
                        return SerialHandshake.RequestToSend;
                    case Windows.Devices.SerialCommunication.SerialHandshake.XOnXOff:
                        return SerialHandshake.XOnXOff;
                    case Windows.Devices.SerialCommunication.SerialHandshake.RequestToSendXOnXOff:
                        return SerialHandshake.RequestToSendXOnXOff;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
     
        // Configuration
        public uint RxBufferSize { get; private set; } = 1024;
        public const uint RX_BUFFER_COUNT_DEFAULT = 16;
        public uint NumberOfRxBuffers = RX_BUFFER_COUNT_DEFAULT;
        private const InputStreamOptions _serialInputStreamOptions = InputStreamOptions.Partial | InputStreamOptions.ReadAhead;

        private string Handle => DeviceInformation.Id;      
        private SerialState State { get; set; }
        private Buffer[] _rxBuffers;
        private IAsyncOperationWithProgress< IBuffer, uint >[] _rxReadTasks;
        private Task _rxBackgroundListener;
        private CancellationTokenSource _rxBackgroundListenerCTS;
        private readonly AsyncManualResetEvent _detachingWaitHandle = new AsyncManualResetEvent( true );

       
        private static Dictionary< string, UWPSerialDevice > DeviceCache { get; } = new Dictionary< string, UWPSerialDevice >();

        public Task DetachCompletion => _detachingWaitHandle.WaitAsync();

        public static async Task< UWPSerialDevice[] > GetDevicesWithVidPidAsync( ushort vid, ushort pid ) {

            var advancedQuerySyntax = SerialDevice.GetDeviceSelectorFromUsbVidPid( vid, pid );
            var currentDeviceInfos = await DeviceInformation.FindAllAsync( advancedQuerySyntax )
                .AsTask()
                .ConfigureAwait( false );

            UpdateDeviceCache( currentDeviceInfos );

            var devices = new List< UWPSerialDevice >();
            foreach ( var deviceInfo in currentDeviceInfos ) {
                DeviceCache.TryGetValue( deviceInfo.Id, out UWPSerialDevice device );
                devices.Add( device );
            }
            
            return devices.ToArray();
        }

        public static async Task< List< UWPSerialDevice > > GetDevicesAsync() {
            // The caching is to ensure there is only a single UWPSerialDevice object
            // for every physical serial device at any moment in time.
            var advancedQuerySyntax = SerialDevice.GetDeviceSelector();
            var currentDeviceInfos = await DeviceInformation.FindAllAsync( advancedQuerySyntax )
                .AsTask()
                .ConfigureAwait( false );
            if ( currentDeviceInfos.Count == 0 ) return new List< UWPSerialDevice >();

            UpdateDeviceCache( currentDeviceInfos );

            var currentDevices = (
                from deviceInfo in currentDeviceInfos
                let deviceKey = deviceInfo.Id
                where DeviceCache.ContainsKey( deviceKey )
                select DeviceCache[ deviceKey ]
            ).ToList();

            return currentDevices;
        }

        public async Task AttachAsync( uint baudRate = 9600, SerialParity parity = SerialParity.None, SerialStopBitCount stopBitCount = SerialStopBitCount.One, SerialHandshake handshake = SerialHandshake.None ) {

            switch ( State ) {
                case SerialState.Attaching:
                    return;
                case SerialState.Attached:
                    return;
                case SerialState.Detaching:
                    await DetachCompletion.ConfigureAwait( false );
                    break;
                case SerialState.Disposed:
                    throw new ObjectDisposedException( nameof( UWPSerialDevice ) );
                case SerialState.Detached:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            State = SerialState.Attaching;

            try {
                try {
                    //await UIThreadHelper.RunAsync( async () => {
                        InnerSerial = await SerialDevice.FromIdAsync( DeviceInformation.Id )
                            .AsTask()
                            .ConfigureAwait( false );
                    //} ).ConfigureAwait( false );
                }
                catch (Exception ex) {
                    InnerSerial = null;
                }
               
                if ( InnerSerial == null ) {
                    var status = DeviceAccessInformation.CreateFromId( DeviceInformation.Id ).CurrentStatus;
                    switch ( status ) {
                        case DeviceAccessStatus.DeniedByUser:
                            throw new SerialDeviceAttachFailedException( $"Access to the device was blocked by the user. DeviceId={DeviceInformation.Id}" );
                        case DeviceAccessStatus.DeniedBySystem:
                            throw new SerialDeviceAttachFailedException( $"Access to the device was blocked by the system. DeviceId={DeviceInformation.Id}" );
                        default:
                            throw new SerialDeviceAttachFailedException( $"Unknown error, possibly opened by another app. DeviceId={DeviceInformation.Id}" );
                    }
                }

                InnerSerial.BaudRate = baudRate;
                InnerSerial.DataBits = 8;
                InnerSerial.WriteTimeout = TimeSpan.FromMilliseconds( 1000 );
                InnerSerial.ReadTimeout = TimeSpan.FromMilliseconds( 1 );

                switch ( parity ) {
                case SerialParity.None:
                    InnerSerial.Parity = Windows.Devices.SerialCommunication.SerialParity.None;
                    break;
                case SerialParity.Odd:
                    InnerSerial.Parity = Windows.Devices.SerialCommunication.SerialParity.Odd;
                    break;
                case SerialParity.Even:
                    InnerSerial.Parity = Windows.Devices.SerialCommunication.SerialParity.Even;
                    break;
                case SerialParity.Mark:
                    InnerSerial.Parity = Windows.Devices.SerialCommunication.SerialParity.Mark;
                    break;
                case SerialParity.Space:
                    InnerSerial.Parity = Windows.Devices.SerialCommunication.SerialParity.Space;
                    break;
                default:
                    throw new ArgumentOutOfRangeException( nameof( parity ), parity, null );
                }
                switch ( stopBitCount ) {
                case SerialStopBitCount.One:
                    InnerSerial.StopBits = Windows.Devices.SerialCommunication.SerialStopBitCount.One;
                    break;
                case SerialStopBitCount.OnePointFive:
                    InnerSerial.StopBits = Windows.Devices.SerialCommunication.SerialStopBitCount.OnePointFive;
                    break;
                case SerialStopBitCount.Two:
                    InnerSerial.StopBits = Windows.Devices.SerialCommunication.SerialStopBitCount.Two;
                    break;
                default:
                    throw new ArgumentOutOfRangeException( nameof( stopBitCount ), stopBitCount, null );
                }
                switch ( handshake ) {
                case SerialHandshake.None:
                    InnerSerial.Handshake = Windows.Devices.SerialCommunication.SerialHandshake.None;
                    break;
                case SerialHandshake.RequestToSend:
                    InnerSerial.Handshake = Windows.Devices.SerialCommunication.SerialHandshake.RequestToSend;
                    break;
                case SerialHandshake.XOnXOff:
                    InnerSerial.Handshake = Windows.Devices.SerialCommunication.SerialHandshake.XOnXOff;
                    break;
                case SerialHandshake.RequestToSendXOnXOff:
                    InnerSerial.Handshake = Windows.Devices.SerialCommunication.SerialHandshake.RequestToSendXOnXOff;
                    break;
                default:
                    throw new ArgumentOutOfRangeException( nameof( handshake ), handshake, null );
                }

                InnerSerial.ErrorReceived += SerialDevice_ErrorReceived;

                _rxBuffers = new Buffer[ NumberOfRxBuffers ];
                _rxReadTasks = new IAsyncOperationWithProgress< IBuffer, uint >[ NumberOfRxBuffers ];

                for ( var i = 0; i < NumberOfRxBuffers; i++ ) {
                    _rxBuffers[ i ] = new Buffer( RxBufferSize );
                    _rxReadTasks[ i ] = InnerSerial.InputStream.ReadAsync( 
                        _rxBuffers[ i ], 
                        RxBufferSize, 
                        _serialInputStreamOptions
                    );
                }

                StartRxBackgroundListener();

                State = SerialState.Attached;
            }
            catch {
                State = SerialState.Detached;
                throw;
            }
        }

        public async Task DetachAsync() {
            if ( InnerSerial != null ) {
                InnerSerial.ErrorReceived -= SerialDevice_ErrorReceived;
            }
            switch ( State ) {
                case SerialState.Attaching:
                    throw new Exception( $"Cannot detach, currently in state: {State}" );
                case SerialState.Detaching:                   
                    return;
                case SerialState.Detached:
                    return;
                case SerialState.Disposed:
                    throw new ObjectDisposedException( nameof( UWPSerialDevice ) );
                case SerialState.Attached:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            _detachingWaitHandle.Reset();
            State = SerialState.Detaching;

            try {
                await StopRxBackgroundTasksAsync().ConfigureAwait( false );
                await Task.Delay( 200 ).ConfigureAwait( false );
            }
            finally {
                State = SerialState.Detached;
                //UIThreadHelper.Run( () => InnerSerial?.Dispose() );
                InnerSerial?.Dispose();
                _detachingWaitHandle.Set();
            }
        }

        public async Task WriteAsync( IList< byte > dataOut ) {

            switch ( State ) {
                case SerialState.Attaching:
                    throw new Exception( $"Internal state invalid: {State}" );
                case SerialState.Attached:

                    var bytes = dataOut.ToArray();
                    if ( bytes.Length == 0 ) return;
                    var buffer = CryptographicBuffer.CreateFromByteArray( bytes );

                    await InnerSerial.OutputStream.WriteAsync( buffer )
                        .AsTask( _rxBackgroundListenerCTS.Token )
                        .ConfigureAwait( false );
                    
                    Debug.WriteLine( $"({Name}) Tx<-[ {bytes.ToHexString()} ]" );
                    break;
                case SerialState.Detaching:
                    throw new Exception( $"Internal state invalid: {State}" );
                case SerialState.Detached:
                    throw new Exception( "Cannot send data while in a detached state" );
                case SerialState.Disposed:
                    throw new ObjectDisposedException( nameof( UWPSerialDevice ) );
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void ConfigureRxBuffers( uint bufferSize, uint bufferCount = RX_BUFFER_COUNT_DEFAULT ) {
            switch ( State ) {
                case SerialState.Attaching:
                    throw new Exception( "Cannot set buffer size while attaching" );
                case SerialState.Attached:
                    throw new Exception( "Buffer size must be set before attaching to serial" );
                case SerialState.Detaching:
                    throw new Exception( "Cannot set buffer size while detaching" );
                case SerialState.Disposed:
                    throw new ObjectDisposedException( nameof( UWPSerialDevice ) );
                case SerialState.Detached:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            NumberOfRxBuffers = bufferCount;
            RxBufferSize = bufferSize;
        }

        private static void UpdateDeviceCache( IEnumerable< DeviceInformation > currentInfos ) {
            foreach ( var deviceInfo in currentInfos ) {
                if ( DeviceCache.ContainsKey( deviceInfo.Id ) ) continue;
                var newDevice = new UWPSerialDevice( deviceInfo );
                DeviceCache.Add( newDevice.Handle, newDevice );
            }
        }

        private void StartRxBackgroundListener() {

            _rxBackgroundListenerCTS = new CancellationTokenSource();
            var cancelToken = _rxBackgroundListenerCTS.Token;

            _rxBackgroundListener = Task.Factory.StartNew<Task>(async () => {
                uint rxTaskIdx = 0;

                while (true) {
                    cancelToken.ThrowIfCancellationRequested();

                    var buffer = await _rxReadTasks[rxTaskIdx];

                    var bytes = buffer.ToArray();
                    _rxReadTasks[rxTaskIdx] = InnerSerial.InputStream.ReadAsync(
                        _rxBuffers[rxTaskIdx],
                        RxBufferSize,
                        _serialInputStreamOptions
                    );

                    if (bytes?.Length <= 0) continue;
                    Debug.WriteLine($"({Name}) Rx->[ {bytes.ToHexString()} ]");
                    DataReceived?.Invoke(this, bytes);

                    rxTaskIdx = (rxTaskIdx + 1) % NumberOfRxBuffers;
                }
            }, cancelToken,
                TaskCreationOptions.RunContinuationsAsynchronously
              | TaskCreationOptions.HideScheduler
              | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default
            ).Unwrap();

            // TODO: Test this with birthing
            //_rxBackgroundListener = Task.Run( async () => {
            //    uint rxTaskIdx = 0;

            //    while ( true ) {
            //        cancelToken.ThrowIfCancellationRequested();

            //        var buffer = await _rxReadTasks[ rxTaskIdx ].AsTask( cancelToken ).ConfigureAwait( false );

            //        var bytes = buffer.ToArray();
            //        _rxReadTasks[ rxTaskIdx ] = InnerSerial.InputStream.ReadAsync( 
            //            _rxBuffers[ rxTaskIdx ], 
            //            RxBufferSize, 
            //            _serialInputStreamOptions
            //        );

            //        if ( bytes?.Length <= 0 ) continue;
            //        Debug.WriteLine( $"({Name}) Rx->[ {bytes.ToHexString()} ]" );
            //        DataReceived?.Invoke( this, bytes );

            //        rxTaskIdx = ( rxTaskIdx + 1 ) % NumberOfRxBuffers;
            //    }
            //}, cancelToken );
        }

        private async Task StopRxBackgroundTasksAsync() {
            _rxBackgroundListenerCTS.Cancel();

            //await UIThreadHelper.RunAsync( async () => {
                for ( var i = 0; i < NumberOfRxBuffers; i++ ) {
                    try {
                        _rxReadTasks[ i ].Cancel();
                        // TODO: Try not awating these
                        //await _rxReadTasks[ i ].AsTask().ConfigureAwait( false );
                    }
                    catch ( OperationCanceledException ) {}
                    catch ( Exception ex ) {
                        // Ignore the winey baby
                    }
                    try {
                        _rxReadTasks[ i ].Close();
                    }
                    catch ( OperationCanceledException ) {}
                    catch ( Exception ex ) {
                        Debug.WriteLine( ex, "_rxReadTasks[ i ].CloseAsync();" );
                    }
                    _rxReadTasks[ i ] = null;
                }

                try {
                    await _rxBackgroundListener.ConfigureAwait( false );
                }
                catch ( OperationCanceledException ) {}
                catch ( Exception ex ) {
                    // Ignore the winey baby
                }
                finally {
                    _rxBackgroundListenerCTS.Dispose();
                }
            //} ).ConfigureAwait( false );
        }

        private void OnErrorReceived( Exception ex ) {
            try {
                ErrorReceived?.Invoke( this, ex );
            }
            catch ( Exception ex2 ) {
                const string TYPE_NAME = nameof( UWPSerialDevice );
                const string MEMBER_NAME = nameof( OnErrorReceived );
                Debug.Fail( $"Error in {TYPE_NAME}::{MEMBER_NAME} while raising ErrorReceived event: {ex2}" );
            }
        }

        private void SerialDevice_ErrorReceived( SerialDevice sender, ErrorReceivedEventArgs args ) => OnErrorReceived( new Exception( "SerialDevice error: " + args.Error ) );

    }

}