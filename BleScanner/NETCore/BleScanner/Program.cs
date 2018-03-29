using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using BlueCats.Ble.Serial.BC0xx;
using BlueCats.Ble.Serial.BC0xx.Commands.Base;
using BlueCats.Ble.Serial.BC0xx.Events;
using BlueCats.Ble.Serial.BC0xx.Events.Base;

namespace BleScanner {

    class Program {

        static int Baud = 921600;
        static string PortName;
        static SerialPort Serial;
        static SerialProtocol Protocol;
        static string ProtocolError;
        static EventPdu EventPdu;
        static CommandResponsePdu ResponsePdu;
        static byte[] SerialBuffer = new byte[ 4096 ]; // FIFO
        static int SerialBufferTail; // Data start index (inclusive)
        static int SerialBufferHead; // Data end index (exclusive)
        static int SerialBufferCount => SerialBufferHead - SerialBufferTail;

        static void Main( string[] args ) {
            try {
                // Handle args
                if ( args.Length < 1 ) {
                    PrintHelp();
                    return;
                }
                ParseArgs( args );

                // Make console fast for large screens
                Console.BufferHeight = 1024;

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
                ReadResponse(); // Not checking the result because this is just a reset attempt

                // *Asynchronously* start scanning for BLE devivces
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
                // Note: GetAwaiter().GetResult() unwraps AggregateExceptions before throwing
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

        static void OnEventReceived( EventPdu eventPdu ) {
            if ( eventPdu.Header.EventCode != EventCode.DeviceDiscovered ) return;
            var discoveredEvent = eventPdu.As< DeviceDiscoveredEvent >();
            var mac = DataConverter.ByteArrayToHexString( discoveredEvent.BluetoothAddress, delimiter: ":" ); 
            var rssi = discoveredEvent.RSSI;
            var bleAdvRawData = DataConverter.ByteArrayToHexString( discoveredEvent.AdData, delimiter: " " );
            Console.WriteLine( $"{mac}  {rssi:###} dBm  {bleAdvRawData}" );
        }

        static void WriteCommand( CommandPdu command ) {
            var commandBytes = command.ToByteArray();
            Serial.Write( commandBytes, 0, commandBytes.Length );
        }

        static Task StartEventPollingTask( CancellationToken cancelToken ) {
            return Task.Run( () => {
                while ( !cancelToken.IsCancellationRequested ) {
                    var b = ReadSerialByteBuffered();
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

        static CommandResponsePdu ReadResponse() {
            while ( true ) {
                var rxByte = ReadSerialByteBuffered();
                Protocol.Parse( rxByte );
                if ( CheckForProtocolError( out var error ) ) {
                    throw new Exception( $"Protocol error: {error}" );
                }
                if ( CheckForResponse( out var responsePdu ) ) {
                    return responsePdu;
                }
            }
        }

        static byte ReadSerialByteBuffered() {
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

        static void ParseArgs( string[] args ) {
            try {
                PortName = args[ 0 ];
                if ( args.Length > 1 ) {
                    Baud = int.Parse( args[ 1 ] );
                }
            }
            catch ( Exception ex ) {
                Debug.WriteLine( ex );
                PrintHelp();
            }
        }

        static void PrintHelp() {
            Console.WriteLine( "Usage: BleScanner serialport [baud] [options]" );
            Console.WriteLine();
            Console.WriteLine( "  baud                       Serial baud (default: 921600)" );
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
                WriteCommand( SerialProtocol.CreateStopScanCommand() );
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

    }

}