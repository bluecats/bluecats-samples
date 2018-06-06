using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using BlueCats.Ble.Serial.BC0xx.Events;

namespace BleScanner {

    class Program {

        static string[] ScannerSerialPaths;
        static List< BCCentralManager > CentralManagers = new List< BCCentralManager >();
        static string[] MacFilter;
        static readonly ManualResetEventSlim ExitWaitHandle = new ManualResetEventSlim( false );

        // Loop connection
        const int LOOP_PORT = 9942;
        static string LoopIP;
        static IPEndPoint Endpoint;
        static Socket LoopSocket;
        static bool IsLoopForwardingEnabled => !string.IsNullOrEmpty( LoopIP );
        
        static void Main( string[] args ) {
            try {
                // Parse args
                var success = TryParseArgs( args );
                if ( !success ) {
                    PrintHelp();
                    return;
                }

                // Make console fast for large screens (Windows)
                if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) ) {
                    Console.BufferHeight = 1024;
                }

                // Init UDP Client
                if ( IsLoopForwardingEnabled ) {
                    LoopSocket = new Socket( AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp );
                    if ( LoopIP.Equals( "255.255.255.255", StringComparison.Ordinal ) ) {
                        LoopSocket.SetSocketOption( SocketOptionLevel.Socket, SocketOptionName.Broadcast, true );
                        Endpoint = new IPEndPoint( IPAddress.Broadcast, LOOP_PORT );
                    }
                    else {
                        var ip = IPAddress.Parse( LoopIP );
                        Endpoint = new IPEndPoint( ip, LOOP_PORT );
                    }
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
#if DEBUG
            finally {
                Console.ReadKey();
            }
#endif
        }

        static void HandleDeviceDiscovered( object sender, DeviceDiscoveredEventArgs e ) {
            var cMan = (BCCentralManager) sender;

            // If filtering, check if BLE device's MAC is in list of MAC address to scan for
            var deviceMac = DataConverter.ByteArrayToHexString( e.DiscoveredEvent.BluetoothAddress, true, ":" );
            if ( MacFilter != null ) {
                var isInFilterList = MacFilter.Any( filterMac => 
                    filterMac.Equals( deviceMac, StringComparison.OrdinalIgnoreCase ) 
                );
                if ( !isInFilterList ) return;
            }
            // Get RSSI and raw Advertisement data
            var rssi = e.DiscoveredEvent.RSSI;
            var bleAdvRawData = DataConverter.ByteArrayToHexString( e.DiscoveredEvent.AdData, delimiter: " " );

            // Print event data to console
            Console.WriteLine( $"({cMan.SerialPath})  {deviceMac}  {rssi:###} dBm  {bleAdvRawData}" );

            // Forward device discovered event to Loop Location Engine endpoint
            if ( IsLoopForwardingEnabled ) { 
                ForwardScanEventToLoop( cMan.CentralBluetoothAddress, e.DiscoveredEvent );
            }  
        }

        static void ForwardScanEventToLoop( byte[] centralBluetoothAddress, DeviceDiscoveredEvent eventPdu ) {
            // Serialize into Loop BLE RSSI packet
            var stream = new MemoryStream();
            var writer = new BinaryWriter( stream );

            // ---[ Loop Local UDP Packet Format ]------------------------------------------------------------
            writer.Write( (UInt16) 3 );                    // Packet Type: BLE RSSI
            writer.Write( centralBluetoothAddress );       // BLE Scanner MAC Address (Bluetooth Address)
            writer.Write( eventPdu.BluetoothAddress );     // Remote Device Bluetooth Address
            writer.Write( (byte) 0x00 );                   // *Reserved
            writer.Write( eventPdu.RSSI );                 // RSSI
            writer.Write( (sbyte) -128 );                  // Indicates to delegate parsing of Measured Power
            writer.Write( (byte) 0x00 );                   // *Reserved
            writer.Write( (byte) 0x00 );                   // *Reserved
            writer.Write( (byte) 0x00 );                   // *Reserved
            writer.Write( (byte) eventPdu.AdData.Length ); // Payload Length
            writer.Write( eventPdu.AdData );               // Payload Data
            // ----------------------------------------------------------------------------------------------

            var loopPacket = stream.ToArray();
            writer.Dispose();
            stream.Dispose();

            // Send over UDP
            var args = new SocketAsyncEventArgs();
            args.RemoteEndPoint = Endpoint;
            args.SetBuffer( loopPacket, 0, loopPacket.Length );
            
            LoopSocket.SendToAsync( args );
        }

        static bool TryParseArgs( string[] args ) {
            try {
                if ( args.Length < 1 ) return false;

                // Parse options
                var param = "-h";
                if ( args.Any( arg => arg.Equals( param, StringComparison.Ordinal ) ) ) return false;

                param = "--loop-ip=";
                LoopIP = args.FirstOrDefault( arg => arg.StartsWith( param, StringComparison.Ordinal ) )
                    ?.Substring( param.Length );

                param = "--filter=";
                MacFilter = args.FirstOrDefault( arg => arg.StartsWith( param, StringComparison.Ordinal ) )
                    ?.Substring( param.Length )
                    ?.Split( ",", StringSplitOptions.RemoveEmptyEntries );

                // Parse serial paths
                ScannerSerialPaths = args.Where( arg => !arg.StartsWith( "-" ) ).ToArray();

                // Success if at least one serial path was given
                return ScannerSerialPaths.Length > 0;
            }
            catch {
                return false;
            }
        }

        static void PrintHelp() {
            Console.WriteLine();
            Console.WriteLine( "Usage:" );
            Console.WriteLine( "  BleScanner serialPath1 serialPath2 serialPath3... [options]" );
            Console.WriteLine();
            Console.WriteLine( "Example: ");
            Console.WriteLine( "  BleScanner com3 com12 --loop-ip=192.168.0.1 --filter=24:DF:54:63:24:3D,24:DF:54:63:24:3D" );
            Console.WriteLine();
            Console.WriteLine( "Options:" );
            Console.WriteLine( "  --loop-ip=<ip address>     Forward to Loop Location Engine IP address" );
            Console.WriteLine( "  --filter=<mac1,mac2...>    Only scan BLE devices with given public MAC addresses" );
            Console.WriteLine( "  -h, --help                 Print this message" );
        }

        static void PrintError( string message ) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine( message );
            Console.ResetColor();
        }

    }

}