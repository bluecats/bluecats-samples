using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BlueCats.Ble.Serial.BC0xx.Events;

namespace BleScanner {

    class Program {

        static string[] ScannerSerialPaths;
        static readonly ManualResetEventSlim ExitWaitHandle = new ManualResetEventSlim( false );
        static List< BCCentralManager > CentralManagers = new List< BCCentralManager >();

        // Loop Location Engine connection
        const int LOOP_PORT = 9942; // Example Loop port
        const string LOOP_HOST = "127.0.0.1"; // Example Loop IP
        static IPEndPoint Endpoint;
        static Socket LoopSocket;
        static bool LoopForwardingEnabled;

        static void Main( string[] args ) {

            // Parse args
            var success = TryParseArgs( args );
            if ( !success ) {
                PrintHelp();
                return;
            }

            try {
                // Make console fast for large screens (Windows)
                if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) ) {
                    Console.BufferHeight = 1024;
                }

                // Init UDP Client
                if ( LoopForwardingEnabled ) {
                    LoopSocket = new Socket( AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp );
                    Endpoint = new IPEndPoint( IPAddress.Parse( LOOP_HOST ), LOOP_PORT );
                }

                // Initialize all USB BLE Scanners and start scanning
                CentralManagers = new List< BCCentralManager >();
                foreach ( var serialPath in ScannerSerialPaths ) {
                    var cMan = new BCCentralManager( serialPath );
                    cMan.DeviceDiscovered += HandleDeviceDiscovered;
                    cMan.StartScan();
                    CentralManagers.Add( cMan );
                }
                
                // Keep process running until ready for completion
                Console.CancelKeyPress += ( sender, e ) => {
                    ExitWaitHandle.Set();
                    e.Cancel = true;
                };
                ExitWaitHandle.Wait();

                // Stop scanning and close serial ports
                foreach ( var cMan in CentralManagers ) {
                    cMan.StopScan();
                    cMan.Close();
                }
            }
            catch ( Exception ex ) {
                PrintError( ex.Message );
            }
        }

        static bool TryParseArgs( string[] args ) {
            try {
                if ( args.Length < 1 ) return false;

                // Parse options
                if ( args.Any( arg => arg.Equals( "-h", StringComparison.Ordinal ) ) ) return false;
                LoopForwardingEnabled = args.Any( arg => arg.Equals( "-l", StringComparison.Ordinal ) );

                // Parse BLE Scanner Serial paths
                ScannerSerialPaths = args.Where( arg => !arg.StartsWith( "-" ) ).ToArray();
                return ScannerSerialPaths.Length > 0;
            }
            catch {
                return false;
            }
        }

        static void PrintHelp() {
            Console.WriteLine();
            Console.WriteLine( "Usage:" );
            Console.WriteLine( "  BleScanner serialPath1 [serialPath2] [serialPath3]... [-lh]" );
            Console.WriteLine();
            Console.WriteLine( "Example:" );
            Console.WriteLine( "  BleScanner com3 com11 com14 -l" );
            Console.WriteLine();
            Console.WriteLine( "  -l                         Forward to Loop Location Engine (UDP)" );
            Console.WriteLine( "  -h, --help                 Print this message" );

        }

        static void PrintError( string message ) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine( message );
            Console.ResetColor();
        }

        static void HandleDeviceDiscovered( object sender, DeviceDiscoveredEventArgs e ) {
            var cMan = (BCCentralManager) sender;
            var mac = DataConverter.ByteArrayToHexString( e.DiscoveredEvent.BluetoothAddress, delimiter: ":" );
            var rssi = e.DiscoveredEvent.RSSI;
            var bleAdvRawData = DataConverter.ByteArrayToHexString( e.DiscoveredEvent.AdData, delimiter: " " );

            // Print event data to console
            if ( CentralManagers.Count <= 1 ) {
                Console.WriteLine( $"{mac}  {rssi:###} dBm  {bleAdvRawData}" );
            }
            else {
                Console.WriteLine( $"({cMan.SerialPath}) {mac}  {rssi:###} dBm  {bleAdvRawData}" );
            }
            

            // Forward device discovered event to Loop Location Engine endpoint
            ForwardScanEventToLoop( cMan.CentralBluetoothAddress, e.DiscoveredEvent );
        }

        private static void ForwardScanEventToLoop( byte[] centralBluetoothAddress, DeviceDiscoveredEvent eventPdu ) {
            // Serialize into Loop BLE RSSI packet
            var stream = new MemoryStream();
            var writer = new BinaryWriter( stream );
            writer.Write( (UInt16) 3 ); // Packet Type: BLE RSSI
            writer.Write( centralBluetoothAddress ); // BLE Scanner MAC Address (Bluetooth Address)
            writer.Write( eventPdu.BluetoothAddress ); // Remote Device Bluetooth Address
            writer.Write( (byte) 0x00 ); // *Reserved
            writer.Write( eventPdu.RSSI ); // RSSI
            writer.Write( (byte) 0x00 ); // *Reserved
            writer.Write( (byte) 0x00 ); // *Reserved
            writer.Write( (byte) 0x00 ); // *Reserved
            writer.Write( (byte) 0x00 ); // *Reserved
            writer.Write( (byte) eventPdu.AdData.Length ); // Payload Length
            writer.Write( eventPdu.AdData ); // Payload Data

            var loopPacket = stream.ToArray();
            writer.Dispose();
            stream.Dispose();

            // Send over UDP
            var args = new SocketAsyncEventArgs();
            args.RemoteEndPoint = Endpoint;
            args.SetBuffer( loopPacket, 0, loopPacket.Length );
            LoopSocket.SendToAsync( args );
        }

    }

}