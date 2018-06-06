using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlueCats.Ble.Serial.BC0xx;
using BlueCats.Ble.Serial.BC0xx.Commands;
using BlueCats.Ble.Serial.BC0xx.Commands.Base;
using BlueCats.Ble.Serial.BC0xx.Events;
using BlueCats.Ble.Serial.BC0xx.Events.Base;

namespace BleScanner {


    public class BCCentralManager {

        public event EventHandler< DeviceDiscoveredEventArgs > DeviceDiscovered;
        public event EventHandler< string > ProtocolError;

        // Serial
        public const int BAUD_RATE = 921600;
        public string SerialPath { get; }
        public bool IsScanning { get; private set; }
        private SerialPort _serial;
        private readonly byte[] _serialBuf = new byte[ 4096 ]; // FIFO buffer
        private int _serialBufTail; // Data start index (inclusive)
        private int _serialBufHead; // Data end index (exclusive)
        private int SerialBufCount => _serialBufHead - _serialBufTail;

        // Protocol
        public byte[] CentralBluetoothAddress { get; private set; }
        private readonly SerialProtocol _protocol;
        private string _protocolError;
        private EventPdu _eventPdu;
        private CommandResponsePdu _responsePdu;
        private CancellationTokenSource _scanCancellationSource;


        public BCCentralManager( string serialPath ) {
            SerialPath = serialPath ?? throw new ArgumentNullException( nameof(serialPath) );

            // Hookup serial protocol
            _protocol = new SerialProtocol();
            _protocol.CommandResponseReceived += ( sender, responsePdu ) => _responsePdu = responsePdu;
            _protocol.EventReceived += ( sender, eventPdu ) => _eventPdu = eventPdu;
            _protocol.ProtocolError += ( sender, error ) => _protocolError = error;
        }

        public void StartScan() {
            try {
                if ( IsScanning ) return;

                // Check if serial is setup serial
                if ( _serial == null ) {
                    InitSerial();
                }
                if ( !_serial.IsOpen ) {
                    _serial.Open();
                }

                // Send StopScan command in case it was previously left in a scanning state
                IsScanning = true;
                SendCommand( SerialProtocol.CreateStopScanCommand() );
                GetResponse(); // The actual response doesn't matter in this case, just consuming it

                // Read MAC Address (Public Bluetooth Address) of BLE Scanner
                SendCommand( SerialProtocol.CreateReadBluetoothAddressCommand() );
                var response = GetResponse();
                var code = response.ResponseCode;
                if ( code != CommandResponseCode.Ack ) throw new Exception( $"Response code: {code}" );
                CentralBluetoothAddress = response.As< ReadBluetoothAddressCommandResponse >()?.BluetoothAddress;

                var macString = DataConverter.ByteArrayToHexString( CentralBluetoothAddress, true, ":" );
				Console.WriteLine( $"Staring scanning with BLE Scanner [ port={SerialPath}, mac={macString} ]" );
				
				// Send StartScan command
				SendCommand( SerialProtocol.CreateStartScanCommand() );
                response = GetResponse();
                if ( response.ResponseCode != CommandResponseCode.Ack ) {
                    throw new Exception( $"Received response code: {response.ResponseCode}" );
                }

                // Start polling for events in background
                _scanCancellationSource = new CancellationTokenSource();
                var cancelToken = _scanCancellationSource.Token;
                StartParsingInBackground( cancelToken );
            }
            catch {
                IsScanning = false;
            }
        }

        public void StopScan() {
            if ( !IsScanning ) return;
            if ( !_scanCancellationSource.IsCancellationRequested ) {
                _scanCancellationSource.Cancel();
            }

            // Send StopScan command to USB BLE Sniffer
            SendCommand( SerialProtocol.CreateStopScanCommand() );
            var response = GetResponse(); 
            if ( response.ResponseCode != CommandResponseCode.Ack ) {
                throw new Exception( $"Responded with: {response.ResponseCode}" );
            }
            IsScanning = false;
        }

        public void Close() {
            try {
                if ( _serial == null ) return;
                // If scanning but closed, reopen
                if ( !_serial.IsOpen && IsScanning ) {
                    try {
                        _serial.Open();
                    }
                    catch ( Exception ex ) {
                        Debug.WriteLine( $"Error while opening serial: {ex}" );
                    }
                }
                // Stop scanning if scanning
                if ( IsScanning ) {
                    try {
                        StopScan();
                    }
                    catch ( Exception ex ) {
                        Debug.WriteLine( $"Error while stopping scan: {ex}" );
                    }
                }
                // Close serial
                try {
                    _serial.Close();
                }
                catch ( Exception ex ) {
                    Debug.WriteLine( $"Error while closing serial: {ex}" );
                }
            }
            finally {
                _serial?.Dispose();
                _serial = null;
            }
        }

        private void InitSerial() {
            if ( _serial != null ) return;
            _serial = new SerialPort( SerialPath, BAUD_RATE ) {
                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = 1000
            };
            _serial.Open();
        }

        private byte ReadByteFromSerialBuffer() {
            // If serial buffer is empty, buffer up the next chunk of serial data
            if ( SerialBufCount <= 0 ) {
                _serialBufTail = 0;
                _serialBufHead = _serial.Read(
                    _serialBuf,
                    _serialBufTail,
                    _serialBuf.Length
                );
            }
            // Consume the next byte of data in a FIFO manner
            var b = _serialBuf[ _serialBufTail ];
            _serialBufTail++;
            return b;
        }

        private void StartParsingInBackground( CancellationToken cancelToken ) {
            Task.Run( () => {
                while ( !cancelToken.IsCancellationRequested ) {
                    var b = ReadByteFromSerialBuffer();
                    _protocol.Parse( b );
                    if ( CheckForProtocolError( out var error ) ) {
                        ProtocolError?.Invoke( this, error );
                    }
                    if ( CheckForEvent( out var eventPdu ) ) {
                        OnEventReceived( eventPdu );
                    }
                }
            }, cancelToken );
        }

        private void SendCommand( CommandPdu command ) {
            if ( _serial == null ) return;
            if ( !_serial.IsOpen ) return;
            var commandBytes = command.ToByteArray();
            _serial.Write( commandBytes, 0, commandBytes.Length );
        }

        private CommandResponsePdu GetResponse() {
            while ( true ) {
                var rxByte = ReadByteFromSerialBuffer();
                _protocol.Parse( rxByte );
                if ( CheckForProtocolError( out var error ) ) {
                    throw new Exception( $"Protocol error: {error}" );
                }
                if ( CheckForResponse( out var responsePdu ) ) {
                    return responsePdu;
                }
            }
        }

        private bool CheckForEvent( out EventPdu eventPdu ) {
            if ( _eventPdu == null ) {
                eventPdu = null;
                return false;
            }
            eventPdu = _eventPdu;
            _eventPdu = null;
            return true;
        }

        private bool CheckForResponse( out CommandResponsePdu responsePdu ) {
            if ( _responsePdu == null ) {
                responsePdu = null;
                return false;
            }
            responsePdu = _responsePdu;
            _responsePdu = null;
            return true;
        }

        private bool CheckForProtocolError( out string error ) {
            if ( _protocolError == null ) {
                error = null;
                return false;
            }
            error = _protocolError;
            _protocolError = null;
            return true;
        }

        private void OnEventReceived( EventPdu eventPdu ) {
            if ( eventPdu?.Header.EventCode == EventCode.DeviceDiscovered ) {
                var pdu = eventPdu.As<DeviceDiscoveredEvent>();
                DeviceDiscovered?.Invoke( this, new DeviceDiscoveredEventArgs( pdu ) );
            }
        }

    }

}