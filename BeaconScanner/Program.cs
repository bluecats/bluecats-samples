using BlueCats.Bluetooth.BLEProtocols;
using BlueCats.Bluetooth.BLEProtocols.AdModels.Apple;
using BlueCats.Bluetooth.Core.Bluegiga;
using BlueCats.Tools.UWP.Lib.Bluegiga;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BlueCats.Tools.Portable.Util;

namespace BeaconScanner {
    class Program {

        static async Task Main(string[] args) {

            // Find BlueCats BLE scanner device
            var scanners = await BGDeviceFinder.GetConnectedSerialDevicesAsync();
            var scannerDevice = scanners.FirstOrDefault();
            if ( scannerDevice == null ) {
                Console.WriteLine( "Error: Please connect a BlueCats BLE Scanner and restart app." );
				Console.ReadLine();
                return;
            }
            Console.WriteLine( $"Found BLE Scanner device: {scannerDevice.Name}" );

            // Connect to scanner and register event handlers
            var bleCentralManager = new BGCentralManager( scannerDevice );
            var blePacketParser = new BCAdParser();
            bleCentralManager.PeripheralDiscovered += ( sender, scanEventArgs ) => {
                blePacketParser.Parse( scanEventArgs.AdvertisementData, scanEventArgs.Peripheral.Address );
            };
            blePacketParser.ParsedAppleIBeaconAd += ( sender, iBeaconAdvertisement ) => {
                OnIBeaconAdvertisementScanned( iBeaconAdvertisement );
            };

            // Start scanning
            Console.WriteLine( "Scanning for iBeacon packets..." + Environment.NewLine );
            await Task.Delay( 1000 );
            await bleCentralManager.ScanForAllPeripheralsAsync();

            // Stop scan when any key is pressed
            Console.ReadLine();
            await bleCentralManager.StopScanAsync();
			
			// Cleanup
			Console.WriteLine( "Scanning complete." + Environment.NewLine );
			Console.WriteLine( "Press [any key] to close.");
			bleCentralManager.Dispose();
			Console.ReadLine();
		}

        /// <summary>
        /// Consume the iBeacon BLE packets here
        /// </summary>
        /// <param name="advertisement">iBeacon BLE Advertisement</param>
        static void OnIBeaconAdvertisementScanned( IBeaconAdModel advertisement ) {
            var bluetoothMAC = advertisement.BluetoothAddress.ToByteArray(true).ToHexString( delimeter: ":" );
            var maj = advertisement.Major;
            var min = advertisement.Minor;
            var proxUUID = Guid.Parse(advertisement.ProximityUUID).ToString();

			Console.ForegroundColor = ConsoleColor.DarkGray;
			Console.Write($" {DateTime.Now.ToString("HH:mm:ss.fff")}: ");
			Console.ForegroundColor = ConsoleColor.DarkRed;
			Console.Write( $" {bluetoothMAC} " );
			Console.ForegroundColor = ConsoleColor.Gray;
			Console.Write($" | Major ");
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write(maj);
			Console.ForegroundColor = ConsoleColor.Gray;
			Console.Write($" | Minor ");
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write(min);
			Console.ForegroundColor = ConsoleColor.Gray;
			Console.Write($" | ProxUUID ");
			Console.ForegroundColor = ConsoleColor.DarkGreen;
			Console.WriteLine(proxUUID);
			Console.ForegroundColor = ConsoleColor.Gray;
		}
    }
}
