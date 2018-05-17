using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BlueCats.Ble.Serial.BC0xx;
using BlueCats.Ble.Serial.BC0xx.Commands;
using BlueCats.Ble.Serial.BC0xx.Commands.Base;
using BlueCats.Ble.Serial.BC0xx.Events;
using BlueCats.Ble.Serial.BC0xx.Events.Base;

namespace BleScanner {

    class Program {

        // Serial properties
        static int Baud = 921600;
        static string PortName;
        static SerialPort Serial;
        static readonly byte[] SerialBuffer = new byte[ 4096 ]; // FIFO
        static int SerialBufferTail; // Data start index (inclusive)
        static int SerialBufferHead; // Data end index (exclusive)
        static int SerialBufferCount => SerialBufferHead - SerialBufferTail;

        // SerialProtocol properties
        static SerialProtocol Protocol;
        static string ProtocolError;
        static EventPdu EventPdu;
        static CommandResponsePdu ResponsePdu;

        // Loop Location Engine properties
        const int LOOP_PORT = 19111; // Example Loop port
        const string LOOP_HOST = "127.0.0.1"; // Example Loop IP
        static IPEndPoint Endpoint;
        static Socket LoopSocket;
        static byte SequenceNumber;
        static bool LoopForwardingEnabled;

        // BLE Scanner properties
        static byte[] ScannerMacAddress;

        static void Main( string[] args ) {
            try {
                // Parse args
                var success = ParseArgs( args );
                if ( !success ) return;

                // Make console fast for large screens
                Console.BufferHeight = 1024;

                // Init UDP Client
                if ( LoopForwardingEnabled ) {
                    LoopSocket = new Socket( AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp );
                    Endpoint = new IPEndPoint( IPAddress.Parse( LOOP_HOST ), LOOP_PORT );
                }

                // Init serial port
                Serial = new SerialPort( PortName, Baud );
                Serial.ReadTimeout = SerialPort.InfiniteTimeout;
                Serial.WriteTimeout = 1000;
                Serial.Open();

                // Init serial protocol
                Protocol = new SerialProtocol();
                Protocol.CommandResponseReceived += ( sender, responsePdu ) => ResponsePdu = responsePdu;
                Protocol.EventReceived += ( sender, eventPdu ) => EventPdu = eventPdu;
                Protocol.ProtocolError += ( sender, error ) => ProtocolError = error;

                // Send StopScan command in-case it was left in a scanning state 
                WriteCommand( SerialProtocol.CreateStopScanCommand() );
                ReadResponse(); // Not checking result because this reset is optional (ensuring good starting point)

                // Read MAC Address of USB BLE Scanner
                if ( LoopForwardingEnabled ) {
                    WriteCommand( SerialProtocol.CreateReadBluetoothAddressCommand() );
                    var response = ReadResponse();
                    if ( response.ResponseCode != CommandResponseCode.Ack ) {
                        throw new Exception( $"Received response code: {response.ResponseCode}" );
                    }
                    ScannerMacAddress = response.As< ReadBluetoothAddressCommandResponse >()?.BluetoothAddress;
                }
                // *Asynchronously* start scanning for BLE devices
                var scanCancellation = new CancellationTokenSource();
                var cancelToken = scanCancellation.Token;
                var scanTask = Task.Run( async () => {
                    try {
                        WriteCommand( SerialProtocol.CreateStartScanCommand() );
                        var response = ReadResponse();
                        if ( response.ResponseCode != CommandResponseCode.Ack ) {
                            throw new Exception( $"Received response code: {response.ResponseCode}" );
                        }
                        // Start polling for events in background
                        await StartEventPollingTask( cancelToken ).ConfigureAwait( false );
                    }
                    catch {
                        scanCancellation.Cancel();
                        throw;
                    }
                }, cancelToken );

                // Display status and wait for either keypress or cancellation
                WaitForKeypress( cancelToken );
                
                if ( !scanCancellation.IsCancellationRequested ) {
                    scanCancellation.Cancel();
                }

                // Rethrow any exceptions from our async tasks (except cancellation)
                // Note: GetAwaiter().GetResult() unwraps AggregateExceptions
                try { scanTask.GetAwaiter().GetResult(); }
                catch ( OperationCanceledException ) {}
            }
            catch ( Exception ex ) {
                PrintError( ex.Message );
            }
            finally {
                Cleanup();
            }
        }

        static bool ParseArgs( string[] args ) {
            try {
                if ( args[ 0 ].StartsWith( "-" ) ) throw new Exception();
                PortName = args[ 0 ];
                if ( args.Length > 1 ) {
                    if ( args[ 1 ].Equals( "-l" ) ) {
                        LoopForwardingEnabled = true;
                    }
                }
                return true;
            }
            catch {
                PrintHelp();
                return false;
            }
        }

        static void PrintHelp() {
            Console.WriteLine();
            Console.WriteLine( "Usage:");
            Console.WriteLine( "  dotnet BleScanner.dll <serial name> [ -h | -l ]" );
            Console.WriteLine();
            Console.WriteLine( "Example:");
            Console.WriteLine( "  dotnet BleScanner.dll com3 -l" );
            Console.WriteLine();
            Console.WriteLine( "  -l                         Send to Loop Location Engine (UDP)" );
            Console.WriteLine( "  -h, --help                 Print this message" );

        }

        static void PrintError( string message ) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine( message );
            Console.ResetColor();
        }

        static void Cleanup() {
            try {
                // Send StopScan to reset the scanner state
                if ( Serial != null ) { 
                    WriteCommand( SerialProtocol.CreateStopScanCommand() );
                }
            }
            catch ( Exception ex ) {
                PrintError( ex.Message );
            }
            try {
                Serial?.Close();
            }
            catch ( Exception ex ) {
                PrintError( ex.Message );
            }
        }

        static void WaitForKeypress( CancellationToken cancelToken ) {
            try {
                var readKeyTask = Task.Run( () => Console.ReadKey( true ), cancelToken );
                readKeyTask.Wait( cancelToken );
            }
            catch ( Exception ex ) {
                if ( !( ex is OperationCanceledException ) ) {
                    Debug.WriteLine( ex );
                }
            }
        }

        static byte ReadByteFromSerialBuffer() {
            // If serial buffer is empty, buffer up the next chunk of serial data
            if ( SerialBufferCount <= 0 ) {
                SerialBufferTail = 0;
                SerialBufferHead = Serial.Read( 
                    SerialBuffer, 
                    SerialBufferTail, 
                    SerialBuffer.Length 
                );
            } 
            // Consume the next byte of data in a FIFO manner
            var b = SerialBuffer[ SerialBufferTail ];
            SerialBufferTail++;
            return b;
        }

        #region Serial Protocol Helpers

        static Task StartEventPollingTask( CancellationToken cancelToken ) {
            return Task.Run( () => {
                while ( !cancelToken.IsCancellationRequested ) {
                    var b = ReadByteFromSerialBuffer();
                    Protocol.Parse( b );
                    if ( CheckForProtocolError( out var error ) ) {
                        PrintError( $"Protocol error: {error}" );
                    }
                    if ( CheckForEvent( out var eventPdu ) ) {
                        OnEventReceived( eventPdu );
                    }
                }
            }, cancelToken );
        }

        static void WriteCommand( CommandPdu command ) {
            var commandBytes = command.ToByteArray();
            Serial.Write( commandBytes, 0, commandBytes.Length );
        }

        static CommandResponsePdu ReadResponse() {
            while ( true ) {
                var rxByte = ReadByteFromSerialBuffer();
                Protocol.Parse( rxByte );
                if ( CheckForProtocolError( out var error ) ) {
                    throw new Exception( $"Protocol error: {error}" );
                }
                if ( CheckForResponse( out var responsePdu ) ) {
                    return responsePdu;
                }
            }
        }

        static bool CheckForEvent( out EventPdu eventPdu ) {
            if ( EventPdu == null ) {
                eventPdu = null;
                return false;
            }
            eventPdu = EventPdu;
            EventPdu = null;
            return true;
        }

        static bool CheckForResponse( out CommandResponsePdu responsePdu ) {
            if ( ResponsePdu == null ) {
                responsePdu = null;
                return false;
            }
            responsePdu = ResponsePdu;
            ResponsePdu = null;
            return true;
        }

        static bool CheckForProtocolError( out string error ) {
            if ( ProtocolError == null ) {
                error = null;
                return false;
            }
            error = ProtocolError;
            ProtocolError = null;
            return true;
        }

        static void OnEventReceived( EventPdu eventPdu ) {
            if ( eventPdu.Header.EventCode != EventCode.DeviceDiscovered ) return;
            var discoveredEvent = eventPdu.As< DeviceDiscoveredEvent >();
            var mac = DataConverter.ByteArrayToHexString( discoveredEvent.BluetoothAddress, delimiter: ":" ); 
            var rssi = discoveredEvent.RSSI;
            var bleAdvRawData = DataConverter.ByteArrayToHexString( discoveredEvent.AdData, delimiter: " " );
            // Print to console
            Console.WriteLine( $"{mac}  {rssi:###} dBm  {bleAdvRawData}" );
            // Forward to Loop Location Engine
            SendBleRssiToLoop( discoveredEvent );
        }

        #endregion

        #region Loop Location Engine Helpers

        private static void SendBleRssiToLoop( DeviceDiscoveredEvent eventPdu ) {
            // Serialize into Loop BLE RSSI packet
            var stream = new MemoryStream();
            var writer = new BinaryWriter( stream );
            writer.Write( (UInt16) 3 );                    // Packet Type: BLE RSSI
            writer.Write( ScannerMacAddress );             // BLE Scanner MAC Address (Bluetooth Address)
            writer.Write( eventPdu.BluetoothAddress );     // Remote Device Bluetooth Address
            writer.Write( (byte) 0x00 );                   // *Reserved
            writer.Write( eventPdu.RSSI );                 // RSSI
            writer.Write( (byte) 0x00 );                   // *Reserved
            writer.Write( SequenceNumber++ );              // Sequence Number
            writer.Write( (byte) 0x00 );                   // *Reserved
            writer.Write( (byte) 0x00 );                   // *Reserved
            writer.Write( (byte) eventPdu.AdData.Length ); // Payload Length
            writer.Write( eventPdu.AdData );               // Payload Data

            var loopPacket = stream.ToArray();
            writer.Dispose();
            stream.Dispose();

            // Send over UDP
            LoopSocket.SendTo( loopPacket, Endpoint );
        }

        #endregion
    }

}